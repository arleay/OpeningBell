using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Gameplay;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Riding (spec §29–31, §77–78): takes over the player's body from the walking controller, feeds keyboard
    /// input and the ground under the wheels into <see cref="RideDynamics"/>, and moves the same
    /// CharacterController, so walls, curbs and stairs work as they do on foot (boards can't climb curbs; bikes
    /// hop them). Crashes cost condition. First-person or chase camera. Lives on the player.
    /// </summary>
    public sealed class RideController : MonoBehaviour
    {
        private const float WalkingStepOffset = 0.3f;
        private const float EyeHeight = 1.62f;

        private GameBootstrap _game;
        private GameInput _input;
        private FirstPersonController _fpc;
        private PlayerInteractor _interactor;
        private InteractionHud _hud;
        private Kit _kit;
        private FleetView _fleetView;
        private CharacterController _body;
        private Transform _pivot, _camera;

        private OwnedVehicle _vehicle;
        private VehicleModel _model;
        private RideSpec _spec;
        private RideState _state;
        private VehicleVisual _visual;
        private NpcBody _rider;
        private bool _chase;
        private float _headYaw, _pitch, _vertical, _pushPose;
        private Vector3 _cameraHome;
        private readonly HashSet<VehicleKind> _hinted = new HashSet<VehicleKind>();
        private readonly RaycastHit[] _hits = new RaycastHit[6];
        private bool _hitPerson, _hitHard;
        private float _lastSorry = -10f;
        private float _walkingMinMove = 0.001f;

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!IsRiding || hit.normal.y > 0.5f) return; // the ground
            LastHit = hit.collider.name;
            if (hit.collider.GetComponentInParent<NpcBody>() != null) _hitPerson = true;
            else _hitHard = true;
        }

        public bool IsRiding => _vehicle != null;
        public OwnedVehicle Vehicle => _vehicle;
        public RideState State => _state;
        public bool ChaseCamera => _chase;
        /// <summary>Last ground seen under the wheels (tests, HUD).</summary>
        public RideGround Ground { get; private set; }
        /// <summary>Name of the last thing bumped into (diagnostics).</summary>
        public string LastHit { get; private set; }


        public void Configure(GameBootstrap game, FirstPersonController fpc, PlayerInteractor interactor, InteractionHud hud, Kit kit, FleetView fleetView)
        {
            _game = game;
            _fpc = fpc;
            _input = fpc.Input;
            _interactor = interactor;
            _hud = hud;
            _kit = kit;
            _fleetView = fleetView;
            _body = GetComponent<CharacterController>();
            _pivot = fpc.CameraPivot;
            _camera = _pivot.GetComponentInChildren<Camera>().transform;
            _cameraHome = _camera.localPosition;
        }

        // ---- mount / dismount ----

        public bool Mount(OwnedVehicle v)
        {
            if (IsRiding || !_fpc.ControlEnabled || !_game.Vehicles.Catalog.TryGetModel(v.ModelId, out _model)) return false;
            _vehicle = v;
            _spec = _game.Vehicles.Catalog.EffectiveSpec(v);
            _state = new RideState();
            if (v.Kind == VehicleKind.Skateboard)
            {
                _state.Heading = transform.eulerAngles.y * Mathf.Deg2Rad;
            }
            else
            {
                // Step onto the bike where it stands; its parked collider goes away with it.
                GameObject parked = _fleetView.Shown(v);
                if (parked != null) parked.GetComponent<Collider>().enabled = false;
                _body.enabled = false;
                transform.SetPositionAndRotation(new Vector3((float)v.X, (float)v.Y, (float)v.Z), Quaternion.Euler(0f, (float)v.Yaw, 0f));
                _body.enabled = true;
                _state.Heading = v.Yaw * Mathf.Deg2Rad;
            }
            _game.Vehicles.SetState(v, VehicleState.Riding);

            _visual = VehicleVisual.Build(_kit, transform, _model);
            _rider = NpcBody.Create(_kit, transform, "Rider", 7070, new Color(0.25f, 0.3f, 0.38f));
            _rider.transform.localPosition = v.Kind == VehicleKind.Skateboard
                ? new Vector3(0f, _visual.SaddleHeight, 0f)
                : new Vector3(0f, _visual.SaddleHeight - 0.92f, -0.14f);
            _rider.transform.localRotation = Quaternion.Euler(0f, v.Kind == VehicleKind.Skateboard ? -70f : 0f, 0f);
            _rider.gameObject.SetActive(_chase);

            _fpc.Suspended = true;
            _interactor.enabled = false;
            _body.stepOffset = v.Kind == VehicleKind.Skateboard ? 0.04f : 0.22f;
            // Rolling slowly at high frame rates moves less than the default 1 mm per frame, which the
            // controller would silently drop.
            _walkingMinMove = _body.minMoveDistance;
            _body.minMoveDistance = 0f;
            _headYaw = 0f;
            _pitch = Signed(_pivot.localEulerAngles.x);
            _vertical = 0f;
            if (_hinted.Add(v.Kind)) _hud.ShowToast(Hint(v.Kind), 7f);
            return true;
        }

        private static string Hint(VehicleKind kind) => kind == VehicleKind.Skateboard
            ? "W push · S foot brake · A/D carve · R or E step off · C camera"
            : "W pedal · Shift sprint · S brake · A/D steer · E get off · C camera" + (kind == VehicleKind.EBike ? " · Q assist" : "");

        public void Dismount(string reason = null)
        {
            if (!IsRiding) return;
            float heading = (float)(_state.Heading * Mathf.Rad2Deg);
            Vector3 p = transform.position;
            Vector3 stand = p;
            if (_vehicle.Kind == VehicleKind.Skateboard)
            {
                _game.Vehicles.SetState(_vehicle, VehicleState.Carried);
            }
            else
            {
                _game.Vehicles.Park(_vehicle, p.x, p.y, p.z, heading);
                stand = FreeSpotBeside(p, heading);
            }
            if (_visual != null) Destroy(_visual.gameObject);
            if (_rider != null) Destroy(_rider.gameObject);
            _visual = null;
            _rider = null;
            _vehicle = null;
            SetChase(false);
            _pivot.localPosition = new Vector3(0f, EyeHeight, 0f);
            _body.stepOffset = WalkingStepOffset;
            _body.minMoveDistance = _walkingMinMove;
            _fpc.Suspended = false;
            _fpc.PlaceAt(stand, heading + _headYaw);
            _interactor.enabled = true;
            _hud.SetStatus(null);
            if (reason != null) _hud.ShowToast(reason);
        }

        /// <summary>Somewhere to stand next to a parked bike: right side, then left, behind, in front (spec §78).</summary>
        private Vector3 FreeSpotBeside(Vector3 p, float heading)
        {
            Quaternion q = Quaternion.Euler(0f, heading, 0f);
            foreach (Vector3 offset in new[] { Vector3.right * 0.9f, Vector3.left * 0.9f, Vector3.back * 1.5f, Vector3.forward * 1.5f })
            {
                Vector3 c = p + q * offset;
                Vector3 bottom = c + Vector3.up * (_body.radius + 0.05f), top = c + Vector3.up * (_body.height - _body.radius);
                if (!Physics.CheckCapsule(bottom, top, _body.radius, ~0, QueryTriggerInteraction.Ignore)) return c;
            }
            return p;
        }

        private void SetChase(bool chase)
        {
            _chase = chase;
            if (_rider != null) _rider.gameObject.SetActive(chase);
            if (chase) return;
            _camera.localPosition = _cameraHome;
            _camera.localRotation = Quaternion.identity;
        }

        // ---- frame ----

        private void Update()
        {
            if (!IsRiding)
            {
                if (_fpc.ControlEnabled && !_game.IsPaused && _input.Ride.WasPressedThisFrame())
                {
                    OwnedVehicle board = _game.Vehicles.Carried;
                    if (board != null) Mount(board);
                    else _hud.ShowToast("You're not carrying a skateboard.");
                }
                return;
            }
            // Pause menu (control handed to UI) or paused clock: the world holds still.
            if (_game.IsPaused || !_fpc.ControlEnabled) return;
            _interactor.enabled = false;

            if (_input.Interact.WasPressedThisFrame() || (_vehicle.Kind == VehicleKind.Skateboard && _input.Ride.WasPressedThisFrame()))
            {
                Dismount();
                return;
            }
            if (_input.CameraToggle.WasPressedThisFrame()) SetChase(!_chase);
            if (_vehicle.Kind == VehicleKind.EBike && _input.Assist.WasPressedThisFrame()) _state.Assist = (_state.Assist + 1) % RideDynamics.AssistRatio.Length;

            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            Vector2 move = _input.Move.ReadValue<Vector2>();
            var input = new RideInput
            {
                Throttle = Mathf.Max(0f, move.y),
                Brake = Mathf.Max(0f, -move.y),
                Steer = move.x,
                Sprint = _input.Sprint.IsPressed(),
            };
            Ground = Probe();
            double battery = _vehicle.BatteryWh;
            RideDynamics.Step(_vehicle.Kind, _spec, _state, input, Ground, dt, _game.Clock.TimeScale, _vehicle.Condition, _vehicle.TireCondition, ref battery);
            if (_vehicle.BatteryCapacityWh > 0) _game.Vehicles.SetBattery(_vehicle, battery);
            if (_state.Bailed)
            {
                _game.Vehicles.Impact(_vehicle, 5);
                Dismount("Speed wobbles. You bailed. Too fast for this board.");
                return;
            }

            // Move the body; walls and curbs push back.
            var forward = new Vector3(Mathf.Sin((float)_state.Heading), 0f, Mathf.Cos((float)_state.Heading));
            _vertical = _body.isGrounded && _vertical < 0f ? -1f : _vertical - 18f * dt;
            Vector3 start = transform.position;
            float intended = (float)(_state.Speed * dt);
            _hitPerson = _hitHard = false;
            _body.Move(forward * intended + Vector3.up * (_vertical * dt));
            Vector3 moved = transform.position - start;
            moved.y = 0f;
            float along = Vector3.Dot(moved, forward);
            if (_state.Speed > 1.2 && along < intended * 0.5f && _hitPerson && !_hitHard)
            {
                // Bumped into someone: awkward, not a crash.
                _state.Speed *= 0.3;
                if (Time.time - _lastSorry > 4f) _hud.ShowSubtitle("You", "Sorry! Sorry.");
                _lastSorry = Time.time;
            }
            else if (_state.Speed > 1.2 && along < intended * 0.5f)
            {
                double speed = _state.Speed;
                double loss = _game.Vehicles.Impact(_vehicle, speed);
                if (speed > 5.5 || (_vehicle.Kind == VehicleKind.Skateboard && speed > 3))
                {
                    Dismount(_vehicle.Kind == VehicleKind.Skateboard && speed <= 5.5
                        ? "Caught the curb and stepped off. Use the curb ramps at crosswalks."
                        : $"Crash! {(loss > 0 ? $"Condition -{loss:P0}." : "")}");
                    return;
                }
                _state.Speed = Math.Max(0, along / dt);
            }
            _game.Vehicles.Ridden(_vehicle, moved.magnitude);
            float headingDeg = (float)(_state.Heading * Mathf.Rad2Deg);
            transform.rotation = Quaternion.Euler(0f, headingDeg, 0f);

            // Where a save would leave it: beside the rider, not under them.
            Vector3 aside = transform.position + transform.right * 0.9f;
            _vehicle.X = aside.x;
            _vehicle.Y = transform.position.y;
            _vehicle.Z = aside.z;
            _vehicle.Yaw = headingDeg;

            Animate(moved.magnitude, dt);
            Look(dt);
            _hud.SetStatus(Status());
        }

        /// <summary>Grade along the heading and roughness from the ground under the wheels.</summary>
        private RideGround Probe()
        {
            var forward = new Vector3(Mathf.Sin((float)_state.Heading), 0f, Mathf.Cos((float)_state.Heading));
            Vector3 p = transform.position;
            bool front = SampleGround(p + forward * 0.6f, out float hFront, out _);
            bool back = SampleGround(p - forward * 0.6f, out float hBack, out _);
            SampleGround(p, out _, out float roughness);
            double grade = front && back ? Mathf.Clamp((hFront - hBack) / 1.2f, -0.6f, 0.6f) : 0;
            return new RideGround { Grade = grade, Roughness = roughness };
        }

        private bool SampleGround(Vector3 at, out float height, out float roughness)
        {
            height = 0f;
            roughness = SurfaceTag.Untagged;
            int n = Physics.RaycastNonAlloc(at + Vector3.up * 0.6f, Vector3.down, _hits, 1.6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Collider hit = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                best = _hits[i].distance;
                height = _hits[i].point.y;
                hit = _hits[i].collider;
            }
            if (hit == null) return false;
            var tag = hit.GetComponentInParent<SurfaceTag>();
            if (tag != null) roughness = tag.Roughness;
            return true;
        }

        private void Animate(float distance, float dt)
        {
            _visual.Animate(distance, _state.CrankAngle, _state.Lean);
            if (_state.Pushing) _pushPose = 0.45f;
            _pushPose -= dt;
            if (_vehicle.Kind == VehicleKind.Skateboard)
                _rider.Animate(_pushPose > 0f ? NpcPose.Push : NpcPose.Skate, Time.time);
            else
                _rider.Animate(NpcPose.Cycle, (float)_state.CrankAngle);
        }

        private void Look(float dt)
        {
            Vector2 look = _input.Look.ReadValue<Vector2>() * _fpc.LookSensitivity;
            _headYaw = Mathf.Clamp(_headYaw + look.x, -110f, 110f);
            _pitch = Mathf.Clamp(_pitch - look.y, -70f, 80f);
            if (Mathf.Abs(look.x) < 0.01f) _headYaw = Mathf.MoveTowards(_headYaw, 0f, 40f * dt); // eyes drift back to the road
            float eye = _vehicle.Kind == VehicleKind.Skateboard ? EyeHeight + _visual.SaddleHeight : 1.52f;
            float roll = _vehicle.Kind == VehicleKind.Skateboard ? 0f : (float)(-_state.Lean * Mathf.Rad2Deg * 0.4f);
            float wobble = (float)_state.Wobble * Mathf.Sin(Time.time * 38f) * 2.5f;
            _pivot.localPosition = new Vector3(0f, eye, 0f);
            _pivot.localRotation = Quaternion.Euler(_pitch, _headYaw, roll + wobble);

            if (!_chase) return;
            // Chase camera: behind and above, pulled in when a wall is in the way.
            Vector3 back = new Vector3(0f, 0.8f, -3.4f);
            Vector3 worldBack = _pivot.TransformDirection(back.normalized);
            float distance = back.magnitude;
            if (Physics.SphereCast(_pivot.position, 0.2f, worldBack, out RaycastHit wall, distance, ~0, QueryTriggerInteraction.Ignore) &&
                !wall.collider.transform.IsChildOf(transform))
                distance = Mathf.Max(0.4f, wall.distance - 0.1f);
            _camera.localPosition = back.normalized * distance;
            _camera.localRotation = Quaternion.Euler(8f, 0f, 0f);
        }

        private string Status()
        {
            var c = CultureInfo.InvariantCulture;
            string text = $"{(_state.Speed * 3.6).ToString("0", c)} km/h · {_vehicle.Name}";
            if (_vehicle.BatteryCapacityWh > 0)
                text += $" · Assist {RideDynamics.AssistNames[_state.Assist]} · Battery {_vehicle.BatteryFraction.ToString("P0", c)}";
            if (_state.Wobble > 0.25) text += " · SPEED WOBBLES";
            if (_vehicle.Condition < 0.5) text += " · needs a tune-up";
            return text;
        }

        private static float Signed(float angle) => angle > 180f ? angle - 360f : angle;
    }
}
