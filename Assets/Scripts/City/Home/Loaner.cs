using System;
using OpeningBell.Gameplay;
using OpeningBell.Home;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Timberline's loaner (TOWN_SPEC B): a pickup with a trailer hitched behind, lent for a $150 deposit and two
    /// hours of game time. Both have a load bed; the trailer's gate folds down. Hand it back parked in the RENTAL
    /// RETURN bay: the deposit comes back less any late fee and damage. It's kept in the save, cargo and all.
    /// </summary>
    public sealed class Loaner : MonoBehaviour
    {
        public const string TruckBed = "loaner_bed", TrailerBed = "loaner_trailer";
        public const string Model = "car_pickup";
        public const float TruckCapacity = 2.2f, TrailerCapacity = 4.8f;

        private HomeWorld _w;
        private FleetView _fleet;
        private DriveController _driver;
        private OwnedVehicle _vehicle;
        private GameObject _trailer;
        private float _nextPose;

        public bool Out => _vehicle != null;
        public OwnedVehicle Vehicle => _vehicle;
        public CarController Truck => _vehicle != null ? _fleet.Shown(_vehicle)?.GetComponent<CarController>() : null;
        public GameObject Trailer => _trailer;

        public void Configure(HomeWorld world, FleetView fleet, DriveController driver)
        {
            _w = world;
            _fleet = fleet;
            _driver = driver;
        }

        private void Start()
        {
            // A loan still running in the save: the truck and trailer come back where they were left.
            if (_w != null && _w.Game.Rental.Active && !Out)
            {
                var (x, y, z, yaw) = _w.Game.Rental.Pose;
                Spawn(new Vector3((float)x, (float)y, (float)z), (float)yaw);
            }
        }

        /// <summary>Lends the truck and trailer (the deposit is taken). An error, or null.</summary>
        public string Borrow()
        {
            if (Out || _w.Game.Rental.Active) return "You've already got the loaner.";
            string error = _w.Game.Economy.Spend(Rental.DepositAmount, "Timberline loaner deposit", _w.Game.Clock.Now);
            if (error != null) return error;
            Vector3 at = HomeStores.LoanerSpot;
            if (!Spawn(at, 0f)) return "The truck's not available.";
            _w.Game.Rental.Start(_w.Game.Clock.Now, _vehicle.Condition);
            _w.Game.Rental.SetPose(at.x, at.y, at.z, 0);
            return null;
        }

        private bool Spawn(Vector3 at, float yaw)
        {
            _vehicle = _w.Game.Vehicles.Lend(Model, null, _w.Game.Clock.Now, at.x, at.y, at.z, yaw);
            _fleet.Refresh();
            GameObject truck = _fleet.Shown(_vehicle);
            if (truck == null)
            {
                _w.Game.Vehicles.Remove(_vehicle);
                _vehicle = null;
                return false;
            }
            SplitForBed(truck);
            _trailer = BuildTrailer(truck);
            return true;
        }

        /// <summary>
        /// The kit pickup has one body box; cut it into the cab and a chassis whose top is the bed floor, so things
        /// can be set down in the back.
        /// </summary>
        private void SplitForBed(GameObject truck)
        {
            var body = truck.GetComponent<BoxCollider>();
            Vector3 c = body.center, s = body.size;
            float rear = c.z - s.z / 2f, front = c.z + s.z / 2f, split = rear + s.z * 0.45f;
            float bottom = c.y - s.y / 2f, floor = bottom + s.y * 0.42f;
            body.center = new Vector3(c.x, c.y, (split + front) / 2f);
            body.size = new Vector3(s.x, s.y, front - split);
            var bed = new GameObject("Bed");
            bed.transform.SetParent(truck.transform, false);
            var box = bed.AddComponent<BoxCollider>();
            box.center = new Vector3(c.x, (bottom + floor) / 2f, (rear + split) / 2f);
            box.size = new Vector3(s.x * 0.9f, floor - bottom, split - rear);
            box.sharedMaterial = body.sharedMaterial;
            bed.AddComponent<CargoBed>().Configure(TruckBed, TruckCapacity, truck.transform);
            _w.AddBed(bed.GetComponent<CargoBed>());
        }

        /// <summary>A flatbed trailer with low sides and a fold-down gate, on two wheels, hitched with a ball joint.</summary>
        private GameObject BuildTrailer(GameObject truck)
        {
            Kit k = _w.City.Kit;
            // The hitch hangs off the back of the bed.
            var bedBox = truck.transform.Find("Bed").GetComponent<BoxCollider>();
            float rearOfTruck = bedBox.center.z - bedBox.size.z / 2f;
            var hitch = new Vector3(0f, 0.5f, rearOfTruck - 0.25f);
            const float tongue = 2.6f, length = 3f, width = 1.7f, deck = 0.62f;

            var go = new GameObject("Loaner trailer") { layer = CityLayers.Vehicle };
            go.transform.SetPositionAndRotation(truck.transform.TransformPoint(hitch - new Vector3(0f, 0.5f, tongue)), truck.transform.rotation);
            Material paint = _w.City.P.Lit(new Color(0.2f, 0.4f, 0.28f), 0.3f);
            Material steel = _w.City.P.Lit(new Color(0.25f, 0.25f, 0.26f), 0.5f);
            Transform t = go.transform;
            GameObject floor = k.Box(t, "Deck", new Vector3(0f, deck - 0.06f, 0f), new Vector3(width, 0.12f, length), steel);
            floor.AddComponent<CargoBed>().Configure(TrailerBed, TrailerCapacity, t);
            k.Box(t, "Side L", new Vector3(-width / 2f, deck + 0.22f, 0f), new Vector3(0.05f, 0.45f, length), paint);
            k.Box(t, "Side R", new Vector3(width / 2f, deck + 0.22f, 0f), new Vector3(0.05f, 0.45f, length), paint);
            k.Box(t, "Front", new Vector3(0f, deck + 0.22f, length / 2f), new Vector3(width, 0.45f, 0.05f), paint);
            k.Box(t, "Tongue", new Vector3(0f, 0.5f, length / 2f + (tongue - length / 2f) / 2f), new Vector3(0.12f, 0.1f, tongue - length / 2f), steel, collider: false);
            k.Text(t, "TIMBERLINE HOME", new Vector3(-width / 2f - 0.03f, deck + 0.25f, 0f), 90f, 0.12f, Color.white);
            // The gate: hinged at the deck's back edge, folds down into a ramp.
            Transform hinge = Kit.Group(t, "Gate hinge", new Vector3(0f, deck, -length / 2f));
            GameObject gate = k.Box(hinge, "Gate", new Vector3(0f, 0.22f, 0f), new Vector3(width, 0.45f, 0.05f), paint);
            gate.AddComponent<TrailerGate>().Configure(hinge, floor.GetComponent<CargoBed>());
            var wheels = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -1f : 1f) * (width / 2f + 0.14f);
                GameObject w = k.Cylinder(t, "Wheel", new Vector3(x, 0.33f, 0f), 0.66f, 0.2f, _w.City.P.Lit(new Color(0.08f, 0.08f, 0.08f)));
                w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                wheels[i] = w.transform;
                k.Box(t, "Fender", new Vector3(x, 0.72f, 0f), new Vector3(0.24f, 0.04f, 0.8f), paint, collider: false);
            }
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 450f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.8f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.centerOfMass = new Vector3(0f, 0.5f, 0.2f);
            go.AddComponent<TrailerPhysics>().Configure(truck.GetComponent<Rigidbody>(), wheels, 0.33f, hitch, new Vector3(0f, 0.5f, tongue));
            SetLayer(t, CityLayers.Vehicle);
            _w.AddBed(floor.GetComponent<CargoBed>());
            return go;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        /// <summary>The truck parked inside the RENTAL RETURN bay (and nobody in it).</summary>
        public bool InReturnBay
        {
            get
            {
                CarController truck = Truck;
                if (truck == null || (_driver != null && _driver.IsDriving)) return false;
                Vector3 p = truck.transform.position;
                return HomeStores.ReturnBay.Contains(new Vector2(p.x, p.z));
            }
        }

        public int CargoAboard
        {
            get
            {
                int n = 0;
                foreach (OwnedItem i in _w.Belongings.Items)
                    if (i.State == ItemState.Loaded && (i.Vehicle == TruckBed || i.Vehicle == TrailerBed)) n++;
                return n;
            }
        }

        /// <summary>Hands the loaner back: settles the deposit; anything still aboard goes to the pickup counter.</summary>
        public (decimal Refund, decimal Extra, decimal Late, decimal Damage) Return()
        {
            DateTime now = _w.Game.Clock.Now;
            var result = _w.Game.Rental.Return(now, _vehicle.Condition);
            if (result.Refund > 0m) _w.Game.Economy.Receive(result.Refund, "Timberline loaner deposit back", now);
            if (result.Extra > 0m) _w.Game.Economy.ChargeFee(result.Extra, "Timberline loaner fees", now);
            foreach (OwnedItem i in _w.Belongings.Items)
                if (i.State == ItemState.Loaded && (i.Vehicle == TruckBed || i.Vehicle == TrailerBed))
                {
                    i.State = ItemState.AtPickup;
                    i.Vehicle = "";
                }
            _w.Belongings.Touch();
            foreach (CargoBed bed in GetBeds()) _w.RemoveBed(bed);
            if (_trailer != null) Destroy(_trailer);
            _trailer = null;
            _w.Game.Vehicles.Remove(_vehicle);
            _vehicle = null;
            _fleet.Refresh();
            return result;
        }

        private CargoBed[] GetBeds() => new[] { _w.Bed(TruckBed), _w.Bed(TrailerBed) };

        private void Update()
        {
            if (!Out || Time.unscaledTime < _nextPose) return;
            _nextPose = Time.unscaledTime + 1f;
            CarController truck = Truck;
            if (truck == null) return;
            Vector3 p = truck.transform.position;
            _w.Game.Rental.SetPose(p.x, p.y, p.z, truck.transform.eulerAngles.y);
        }
    }

    /// <summary>
    /// Two raycast wheels (spring, damper, sideways grip) and a ball-joint hitch. Asleep while the truck is parked,
    /// so a trailer left in a car park stays put.
    /// </summary>
    public sealed class TrailerPhysics : MonoBehaviour
    {
        private const float Rest = 0.3f, Spring = 36000f, Damper = 3500f, Grip = 1.1f;
        private Rigidbody _rb, _truck;
        private Transform[] _wheels;
        private Vector3[] _mounts;
        private float _radius;
        private ConfigurableJoint _joint;

        public ConfigurableJoint Joint => _joint;

        public void Configure(Rigidbody truck, Transform[] wheels, float radius, Vector3 truckHitch, Vector3 trailerHitch)
        {
            _rb = GetComponent<Rigidbody>();
            _truck = truck;
            _wheels = wheels;
            _radius = radius;
            _mounts = new Vector3[wheels.Length];
            for (int i = 0; i < wheels.Length; i++) _mounts[i] = wheels[i].localPosition + Vector3.up * Rest;
            _joint = gameObject.AddComponent<ConfigurableJoint>();
            _joint.connectedBody = truck;
            _joint.autoConfigureConnectedAnchor = false;
            _joint.anchor = trailerHitch;
            _joint.connectedAnchor = truckHitch;
            _joint.xMotion = _joint.yMotion = _joint.zMotion = ConfigurableJointMotion.Locked;
            _joint.angularXMotion = ConfigurableJointMotion.Limited;
            _joint.angularYMotion = ConfigurableJointMotion.Limited;
            _joint.angularZMotion = ConfigurableJointMotion.Limited;
            _joint.lowAngularXLimit = new SoftJointLimit { limit = -20f };
            _joint.highAngularXLimit = new SoftJointLimit { limit = 20f };
            _joint.angularYLimit = new SoftJointLimit { limit = 75f };
            _joint.angularZLimit = new SoftJointLimit { limit = 15f };
            _joint.enableCollision = false;
        }

        private void FixedUpdate()
        {
            if (_rb == null) return;
            bool parked = _truck == null || _truck.isKinematic;
            if (parked && _rb.linearVelocity.sqrMagnitude < 0.01f) { if (!_rb.isKinematic) _rb.isKinematic = true; return; }
            if (!parked && _rb.isKinematic) _rb.isKinematic = false;
            if (_rb.isKinematic) return;
            float perWheel = _rb.mass * -Physics.gravity.y / _wheels.Length;
            for (int i = 0; i < _wheels.Length; i++)
            {
                Vector3 mount = transform.TransformPoint(_mounts[i]);
                Vector3 up = transform.up;
                if (!Physics.Raycast(mount, -up, out RaycastHit hit, Rest + _radius + 0.2f, ~(1 << CityLayers.Vehicle), QueryTriggerInteraction.Ignore))
                {
                    _wheels[i].position = mount - up * Rest;
                    continue;
                }
                float compression = Rest + _radius - hit.distance;
                Vector3 v = _rb.GetPointVelocity(mount);
                float load = Mathf.Max(0f, Spring * compression - Damper * Vector3.Dot(v, up));
                _rb.AddForceAtPosition(up * load, mount);
                // Sideways grip: cancel slide across the wheel, limited by what the tyre can hold.
                Vector3 side = transform.right;
                float slide = Vector3.Dot(v, side);
                float grip = Mathf.Clamp(-slide * perWheel * 0.5f, -load * Grip, load * Grip);
                _rb.AddForceAtPosition(side * grip, hit.point);
                // A little rolling drag.
                _rb.AddForceAtPosition(-transform.forward * Vector3.Dot(v, transform.forward) * 12f, hit.point);
                _wheels[i].position = hit.point + up * _radius;
            }
        }
    }

    /// <summary>The trailer's tailgate: down it's a ramp and the bed can be loaded; up, things stay in.</summary>
    public sealed class TrailerGate : Interactable
    {
        private Transform _hinge;
        private CargoBed _bed;

        public void Configure(Transform hinge, CargoBed bed)
        {
            _hinge = hinge;
            _bed = bed;
            Set(false);
        }

        public bool Down => _bed.Open;
        public override string Prompt => Down ? "Close the gate" : "Open the gate";
        public override void Interact() => Set(!Down);

        public void Set(bool down)
        {
            _bed.Open = down;
            _hinge.localRotation = Quaternion.Euler(down ? -95f : 0f, 0f, 0f);
        }
    }

    /// <summary>Timberline's loaner desk: borrow the truck and trailer, or hand them back from the return bay.</summary>
    public sealed class RentalDesk : Interactable
    {
        private HomeWorld _w;
        private StoreDesk _desk;

        public void Configure(HomeWorld world, StoreDesk desk)
        {
            _w = world;
            _desk = desk;
        }

        private Loaner Loaner => _w.Loaner;

        public override string Prompt
        {
            get
            {
                if (Loaner == null) return "Loaner desk";
                if (!Loaner.Out) return $"Borrow the pickup and trailer · {HomeWorld.Dollars(Rental.DepositAmount)} deposit, back in 2 h";
                return Loaner.InReturnBay ? "Return the loaner" : "Loaner out: park it in RENTAL RETURN";
            }
        }

        public override string Details => Loaner != null && Loaner.Out ? $"Due back {_w.Game.Rental.Due:ddd h:mm tt}. Late: $20 a half hour after 10 min grace; damage is charged." : null;

        public override void Interact()
        {
            if (Loaner == null) return;
            if (!_desk.Open) { _w.Say("The loaner desk's shut. " + _desk.Hours.Describe()); return; }
            if (!Loaner.Out)
            {
                if (!_desk.Confirm("borrow", $"Pickup and trailer for two hours, {HomeWorld.Dollars(Rental.DepositAmount)} deposit? [E] again.")) return;
                string error = Loaner.Borrow();
                if (error != null) _w.Say(error);
                else _desk.Say($"Keys are in it. It's the green trailer in the yard. Back by {_w.Game.Rental.Due:h:mm tt}, please.");
                return;
            }
            if (!Loaner.InReturnBay)
            {
                _desk.Say("Park it in the RENTAL RETURN bay and hop out, then I'll check it in.");
                return;
            }
            int cargo = Loaner.CargoAboard;
            if (cargo > 0 && !_desk.Confirm("return-cargo", $"You've still got {cargo} thing{(cargo == 1 ? "" : "s")} aboard. [E] again and I'll put them back at pickup.")) return;
            var r = Loaner.Return();
            string fees = r.Late + r.Damage > 0m ? $" Late {HomeWorld.Dollars(r.Late)}, damage {HomeWorld.Dollars(r.Damage)}." : "";
            _desk.Say($"All checked in. {HomeWorld.Dollars(r.Refund)} back on your card.{fees}");
        }
    }
}
