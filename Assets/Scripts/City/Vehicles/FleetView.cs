using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Gameplay;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>How rough a surface rides (0 polished floor … 1 grass). Untagged ground counts as smooth floor.</summary>
    public sealed class SurfaceTag : MonoBehaviour
    {
        public float Roughness = 0.3f;

        public const float Untagged = 0.1f;
    }

    /// <summary>A parked bike in the world: [E] to ride it. Shows its condition and charge.</summary>
    public sealed class ParkedVehicle : Interactable
    {
        private GameBootstrap _game;
        private string _verb;
        private System.Func<bool> _canUse;
        private System.Action<OwnedVehicle> _use;

        public OwnedVehicle Vehicle { get; private set; }

        public void Configure(OwnedVehicle vehicle, GameBootstrap game, string verb, System.Func<bool> canUse, System.Action<OwnedVehicle> use)
        {
            Vehicle = vehicle;
            _game = game;
            _verb = verb;
            _canUse = canUse;
            _use = use;
        }

        public override string Prompt => _verb + " " + Vehicle.Name;
        public override bool CanInteract => base.CanInteract && _canUse();
        public override string Details => Describe(Vehicle, _game);

        public override void Interact() => _use(Vehicle);

        public static string Describe(OwnedVehicle v, GameBootstrap game)
        {
            var c = CultureInfo.InvariantCulture;
            string text = $"Condition {v.Condition:P0} · {(v.Kind == VehicleKind.Skateboard ? "wheels" : "tyres")} {v.TireCondition:P0} · {(v.Odometer / 1000).ToString("0.0", c)} km";
            if (v.BatteryCapacityWh > 0) text += $" · battery {v.BatteryFraction:P0}";
            if (v.FuelCapacity > 0) text += $" · fuel {v.FuelFraction:P0}";
            return text + $"\nResale about ${v.ResaleValue(game.Clock.Now).ToString("N0", c)}";
        }
    }

    /// <summary>
    /// Keeps a world object for every parked vehicle in the fleet (positions come from the save, never from a
    /// prefab spawn). Rebuilds on fleet changes; cheap, as there are only a few.
    /// </summary>
    public sealed class FleetView : MonoBehaviour
    {
        private GameBootstrap _game;
        private Kit _kit;
        private RideController _rider;
        private DriveController _driver;
        private readonly Dictionary<OwnedVehicle, GameObject> _shown = new Dictionary<OwnedVehicle, GameObject>();
        private bool _dirty = true;

        public void Configure(GameBootstrap game, Kit kit, RideController rider, DriveController driver)
        {
            _game = game;
            _kit = kit;
            _rider = rider;
            _driver = driver;
            _game.Vehicles.Changed += _ => _dirty = true;
        }

        public GameObject Shown(OwnedVehicle v) => _shown.TryGetValue(v, out GameObject go) ? go : null;

        /// <summary>Throws away a parked vehicle's object and builds it again (after upgrades, paint or tint).</summary>
        public void Rebuild(OwnedVehicle v)
        {
            if (!_shown.TryGetValue(v, out GameObject go) || v.State != VehicleState.Parked) return;
            go.SetActive(false); // gone this frame, so the new one doesn't collide with it
            Destroy(go);
            _shown.Remove(v);
            Refresh();
        }

        /// <summary>Parked cars, for traffic to steer around or queue behind.</summary>
        public void ParkedCarPositions(List<Vector2> into)
        {
            foreach (var pair in _shown)
                if (pair.Key.Kind == VehicleKind.Car) into.Add(new Vector2(pair.Value.transform.position.x, pair.Value.transform.position.z));
        }

        private bool Busy => _rider.IsRiding || _driver.IsDriving;

        private void LateUpdate()
        {
            if (_dirty) Refresh();
        }

        /// <summary>Brings the world objects up to date now (a dealer's test car must exist before you get in).</summary>
        public void Refresh()
        {
            _dirty = false;
            var keep = new HashSet<OwnedVehicle>();
            foreach (OwnedVehicle v in _game.Vehicles.Vehicles)
            {
                // A car being driven keeps its object (it *is* the car); everything else parked gets one.
                if (v.State != VehicleState.Parked && !(v.Kind == VehicleKind.Car && v.State == VehicleState.Riding)) continue;
                keep.Add(v);
                if (v.Kind == VehicleKind.Car && _shown.ContainsKey(v))
                {
                    if (v.State == VehicleState.Parked) _shown[v].GetComponent<CarController>().SetParked(true);
                    continue; // physics owns its pose
                }
                if (!_shown.TryGetValue(v, out GameObject go)) _shown[v] = go = Build(v);
                go.transform.SetPositionAndRotation(new Vector3((float)v.X, (float)v.Y, (float)v.Z), Quaternion.Euler(0f, (float)v.Yaw, 0f));
            }
            var gone = new List<OwnedVehicle>();
            foreach (OwnedVehicle v in _shown.Keys)
                if (!keep.Contains(v)) gone.Add(v);
            foreach (OwnedVehicle v in gone)
            {
                Destroy(_shown[v]);
                _shown.Remove(v);
            }
        }

        private GameObject Build(OwnedVehicle v)
        {
            _game.Vehicles.Catalog.TryGetModel(v.ModelId, out VehicleModel model);
            if (model.Kind == VehicleKind.Car)
            {
                GameObject mesh = _game.VehicleLibrary != null ? _game.VehicleLibrary.CarMesh(model.Mesh) : null;
                if (mesh != null)
                {
                    CarController car = CarFactory.BuildDrivable(transform, mesh, CarTuning.Apply(model.Car, v.Parts), "Car " + v.Name + " " + v.Id);
                    car.transform.SetPositionAndRotation(new Vector3((float)v.X, (float)v.Y, (float)v.Z), Quaternion.Euler(0f, (float)v.Yaw, 0f));
                    if (v.Painted) CarFactory.Paint(car.gameObject, new Color(v.PaintR, v.PaintG, v.PaintB));
                    if (v.Tinted) CarFactory.Tint(car.gameObject, _kit.P.Glass(new Color(0.02f, 0.02f, 0.03f, 0.85f)));
                    car.gameObject.AddComponent<ParkedVehicle>().Configure(v, _game, "Drive", () => !Busy, x => _driver.Enter(x));
                    return car.gameObject;
                }
            }
            var root = new GameObject("Parked " + v.Name + " " + v.Id);
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3((float)v.X, -100f, (float)v.Z); // never flash at the origin
            VehicleVisual visual = VehicleVisual.Build(_kit, root.transform, model);
            visual.Animate(0, 0, v.Kind == VehicleKind.Skateboard ? 0 : 0.12); // on its kickstand
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.5f, 0f);
            box.size = new Vector3(0.5f, 1f, Mathf.Max(0.6f, visual.Length));
            root.AddComponent<ParkedVehicle>().Configure(v, _game, "Ride", () => !Busy, x => _rider.Mount(x));
            return root;
        }
    }

    /// <summary>
    /// Charges e-bikes parked next to it, by game time (a night's sleep charges fully). Home, public or fast.
    /// </summary>
    public sealed class ChargingPoint : Interactable
    {
        private GameBootstrap _game;
        private string _label;
        private float _watts;
        private Renderer _lamp;
        private Material _on, _off;
        private System.DateTime _last;

        public const float Reach = 2.5f;

        public float Watts => _watts;

        public void Configure(GameBootstrap game, string label, float watts, Renderer lamp, Material on, Material off)
        {
            _game = game;
            _label = label;
            _watts = watts;
            _lamp = lamp;
            _on = on;
            _off = off;
            _last = game.Clock.Now;
        }

        public override string Prompt => $"{_label} ({_watts:0} W)";
        public override string Details
        {
            get
            {
                OwnedVehicle v = Nearest();
                return v != null ? $"Charging {v.Name}: {v.BatteryFraction:P0}" : "Park an e-bike here to charge it.";
            }
        }
        public override void Interact() { }

        private OwnedVehicle Nearest()
        {
            foreach (OwnedVehicle v in _game.Vehicles.Vehicles)
                if (v.State == VehicleState.Parked && v.BatteryCapacityWh > 0 &&
                    new Vector2((float)v.X - transform.position.x, (float)v.Z - transform.position.z).sqrMagnitude < Reach * Reach) return v;
            return null;
        }

        private void Update()
        {
            System.DateTime now = _game.Clock.Now;
            double gameSeconds = (now - _last).TotalSeconds;
            _last = now;
            bool charging = false;
            foreach (OwnedVehicle v in _game.Vehicles.Vehicles)
            {
                if (v.State != VehicleState.Parked || v.BatteryCapacityWh <= 0) continue;
                if (new Vector2((float)v.X - transform.position.x, (float)v.Z - transform.position.z).sqrMagnitude > Reach * Reach) continue;
                if (v.BatteryWh < v.BatteryCapacityWh) charging = true;
                if (gameSeconds > 0) _game.Vehicles.Charge(v, _watts, gameSeconds);
            }
            Material m = charging ? _on : _off;
            if (_lamp.sharedMaterial != m) _lamp.sharedMaterial = m;
        }
    }
}
