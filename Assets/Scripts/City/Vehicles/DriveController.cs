using System.Collections;
using System.Globalization;
using OpeningBell.Gameplay;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Driving a car (spec §77–78): [E] at a parked car to get in (the camera glides to the seat), W/S/A/D,
    /// Space handbrake, C for chase or hood camera, E to get out (only when nearly stopped) into a clear spot
    /// beside the door. Burns fuel by game time, takes damage from hard hits. Lives on the player.
    /// </summary>
    public sealed class DriveController : MonoBehaviour
    {
        private GameBootstrap _game;
        private GameInput _input;
        private FirstPersonController _fpc;
        private PlayerInteractor _interactor;
        private InteractionHud _hud;
        private FleetView _fleetView;
        private CharacterController _body;
        private Transform _pivot, _camera;
        private Vector3 _cameraHome;

        private OwnedVehicle _vehicle;
        private CarController _car;
        private bool _chase = true;
        private bool _entering;
        private Vector3 _chasePosition;
        private float _lastImpactToast = -10f;
        private float _odometerCarry;
        private System.Func<float> _night;
        private Light _headlights;
        private AudioSource _engine;

        public bool IsDriving => _vehicle != null;
        public OwnedVehicle Vehicle => _vehicle;
        public CarController Car => _car;
        public bool ChaseCamera => _chase;

        public void Configure(GameBootstrap game, FirstPersonController fpc, PlayerInteractor interactor, InteractionHud hud, FleetView fleetView,
            System.Func<float> night)
        {
            _night = night;
            _game = game;
            _fpc = fpc;
            _input = fpc.Input;
            _interactor = interactor;
            _hud = hud;
            _fleetView = fleetView;
            _body = GetComponent<CharacterController>();
            _pivot = fpc.CameraPivot;
            _camera = _pivot.GetComponentInChildren<Camera>().transform;
            _cameraHome = _camera.localPosition;
        }

        public bool Enter(OwnedVehicle v)
        {
            if (IsDriving || _entering || !_fpc.ControlEnabled) return false;
            GameObject go = _fleetView.Shown(v);
            if (go == null) return false;
            _vehicle = v;
            _car = go.GetComponent<CarController>();
            StartCoroutine(EnterRoutine());
            return true;
        }

        private IEnumerator EnterRoutine()
        {
            _entering = true;
            _fpc.Suspended = true;
            _interactor.enabled = false;
            _body.enabled = false;
            _game.Vehicles.SetState(_vehicle, VehicleState.Riding);
            Vector3 fromPos = _camera.position;
            Quaternion fromRot = _camera.rotation;
            // Glide into the driver's seat, then sit the player there (parented, so they move with the car).
            Transform seat = _car.transform.Find("Seat");
            for (float t = 0f; t < 0.45f; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 0.45f);
                _camera.SetPositionAndRotation(Vector3.Lerp(fromPos, seat.position, k), Quaternion.Slerp(fromRot, seat.rotation, k));
                yield return null;
            }
            transform.SetParent(seat, false);
            transform.localPosition = new Vector3(0f, -_pivot.localPosition.y, 0f); // eyes at the seat's eye point
            transform.localRotation = Quaternion.identity;
            _pivot.localRotation = Quaternion.identity;
            _camera.localPosition = _cameraHome;
            _camera.localRotation = Quaternion.identity;
            _car.SetParked(false);
            _car.EngineOn = _vehicle.FuelLiters > 0;
            _car.Condition = (float)_vehicle.Condition;
            _car.TireCondition = (float)_vehicle.TireCondition;
            _car.Impact += OnImpact;
            _headlights = _car.GetComponentInChildren<Light>(true);
            // TryGetComponent, not ??: Unity's missing-component placeholder isn't C# null.
            _engine = _car.TryGetComponent(out AudioSource existing) ? existing : EngineSound(_car.gameObject);
            _engine.Play();
            _chasePosition = _car.transform.position - _car.transform.forward * 6f + Vector3.up * 2.5f;
            _entering = false;
            _hud.ShowToast("W gas · S brake/reverse · A/D steer · Space handbrake · C camera · E get out", 7f);
            if (_vehicle.FuelLiters <= 0) _hud.ShowToast("The tank is empty. Push it to a pump... or call someone.");
        }

        public void Exit()
        {
            if (!IsDriving || _entering) return;
            if (Mathf.Abs(_car.ForwardSpeed) > 2f)
            {
                _hud.ShowToast("Stop first.");
                return;
            }
            _car.Impact -= OnImpact;
            _car.Throttle = _car.Brake = _car.Steer = 0f;
            _car.Handbrake = true;
            _car.SetParked(true);
            if (_headlights != null) _headlights.enabled = false;
            if (_engine != null) _engine.Stop();
            Transform car = _car.transform;
            _game.Vehicles.Park(_vehicle, car.position.x, car.position.y, car.position.z, car.eulerAngles.y);
            Vector3 spot = FreeSpotBeside(car);
            transform.SetParent(null, true);
            _body.enabled = true;
            _fpc.Suspended = false;
            _fpc.PlaceAt(spot, car.eulerAngles.y);
            _camera.localPosition = _cameraHome;
            _camera.localRotation = Quaternion.identity;
            _interactor.enabled = true;
            _hud.SetStatus(null);
            _game.PlayerSavePosition = null;
            _vehicle = null;
            _car = null;
        }

        /// <summary>A clear spot by the driver's door (left), else the passenger side, behind or in front.</summary>
        private Vector3 FreeSpotBeside(Transform car)
        {
            float halfWidth = 1.4f, halfLength = 2.8f;
            foreach (Vector3 offset in new[] { Vector3.left * halfWidth, Vector3.right * halfWidth, Vector3.back * halfLength, Vector3.forward * halfLength })
            {
                Vector3 c = car.position + car.rotation * offset;
                c.y = car.position.y + 0.1f;
                Vector3 bottom = c + Vector3.up * (_body.radius + 0.15f), top = c + Vector3.up * (_body.height - _body.radius);
                if (!Physics.CheckCapsule(bottom, top, _body.radius, ~0, QueryTriggerInteraction.Ignore)) return c;
            }
            return car.position + Vector3.up * 1.6f; // climb out the top as a last resort
        }

        private void OnImpact(float speed, Collision collision)
        {
            if (speed < (float)Fleet.SafeImpactSpeed) return;
            double loss = _game.Vehicles.Impact(_vehicle, speed);
            _car.Condition = (float)_vehicle.Condition;
            if (Time.time - _lastImpactToast < 2f) return;
            _lastImpactToast = Time.time;
            _hud.ShowToast($"Crash! Condition -{loss:P0} (now {_vehicle.Condition:P0}).");
        }

        private void Update()
        {
            if (!IsDriving || _entering) return;
            if (_game.IsPaused || !_fpc.ControlEnabled)
            {
                _car.Throttle = _car.Brake = 0f;
                return;
            }
            _interactor.enabled = false;
            if (_input.Interact.WasPressedThisFrame())
            {
                Exit();
                return;
            }
            if (_input.CameraToggle.WasPressedThisFrame()) _chase = !_chase;

            Vector2 move = _input.Move.ReadValue<Vector2>();
            _car.Throttle = Mathf.Max(0f, move.y);
            _car.Brake = Mathf.Max(0f, -move.y);
            _car.Steer = move.x;
            _car.Handbrake = _input.Handbrake.IsPressed();

            // Fuel and wear follow what the car actually did.
            double gameSeconds = Time.deltaTime * _game.Clock.TimeScale;
            _game.Vehicles.SetFuel(_vehicle, _vehicle.FuelLiters - CarPhysics.Fuel(_car.Spec, _car.EnginePower, gameSeconds));
            if (_vehicle.FuelLiters <= 0 && _car.EngineOn)
            {
                _car.EngineOn = false;
                _hud.ShowToast("Out of fuel.");
            }
            _odometerCarry += Mathf.Abs(_car.ForwardSpeed) * Time.deltaTime;
            if (_odometerCarry > 5f)
            {
                _game.Vehicles.Ridden(_vehicle, _odometerCarry);
                _odometerCarry = 0f;
            }

            // Where the car and the player would be if the game saved now.
            Transform t = _car.transform;
            _vehicle.X = t.position.x;
            _vehicle.Y = t.position.y;
            _vehicle.Z = t.position.z;
            _vehicle.Yaw = t.eulerAngles.y;
            _game.PlayerSavePosition = t.position + t.rotation * (Vector3.left * 1.4f);
            if (_headlights != null) _headlights.enabled = _night() > 0.5f;
            // Engine note follows revs; louder under load.
            if (_engine != null)
            {
                _engine.pitch = Mathf.Clamp(_car.EngineRpm / 1800f, 0.35f, 4f);
                _engine.volume = _car.EngineOn ? 0.18f + 0.35f * _car.Throttle : 0f;
            }
            _hud.SetStatus(Status());
        }

        private void LateUpdate()
        {
            if (!IsDriving || _entering) return;
            Transform t = _car.transform;
            if (!_chase)
            {
                _camera.localPosition = _cameraHome;
                _camera.localRotation = Quaternion.identity;
                return;
            }
            // Chase: spring toward a spot behind and above, looking just over the roof; pull in for walls.
            Vector3 behind = t.position - t.forward * 6.2f + Vector3.up * 2.6f;
            _chasePosition = Vector3.Lerp(_chasePosition, behind, 1f - Mathf.Exp(-6f * Time.deltaTime));
            Vector3 focus = t.position + Vector3.up * 1.3f + t.forward * 1.5f;
            Vector3 toCam = _chasePosition - focus;
            if (Physics.SphereCast(focus, 0.25f, toCam.normalized, out RaycastHit wall, toCam.magnitude, ~0, QueryTriggerInteraction.Ignore) &&
                !wall.collider.transform.IsChildOf(t))
                _camera.position = focus + toCam.normalized * Mathf.Max(1f, wall.distance - 0.2f);
            else _camera.position = _chasePosition;
            _camera.rotation = Quaternion.LookRotation(focus - _camera.position);
        }

        /// <summary>TODO(audio): a synthesized engine loop until recorded engine sounds exist.</summary>
        private static AudioSource EngineSound(GameObject car)
        {
            var source = car.AddComponent<AudioSource>();
            source.clip = ProceduralSounds.Engine();
            source.loop = true;
            source.spatialBlend = 0.7f;
            source.minDistance = 2f;
            source.maxDistance = 40f;
            return source;
        }

        private string Status()
        {
            var c = CultureInfo.InvariantCulture;
            string gear = _car.InReverse ? "R" : (_car.Gear + 1).ToString(c);
            return $"{Mathf.Abs(_car.SpeedKmh).ToString("0", c)} km/h · {gear} · {_car.EngineRpm.ToString("0", c)} rpm · Fuel {_vehicle.FuelFraction.ToString("P0", c)}" +
                   (_car.Wheelspin ? " · WHEELSPIN" : "") + (_vehicle.Condition < 0.4 ? " · engine damaged" : "");
        }
    }
}
