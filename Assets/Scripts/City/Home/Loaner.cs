using System;
using OpeningBell.Gameplay;
using OpeningBell.Home;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Timberline's loaner (TOWN_SPEC B): a box truck (a chassis-cab with a cargo box on its flatbed), lent for a
    /// $150 deposit and two hours of game time. The box's floor is the load bed; its roll-up back door has to be up
    /// to load or unload. Hand it back parked in the RENTAL RETURN bay: the deposit comes back less any late fee and
    /// damage. It's kept in the save, cargo and all.
    /// </summary>
    public sealed class Loaner : MonoBehaviour
    {
        public const string TruckBed = "loaner_bed";
        /// <summary>Where the old pickup's trailer kept its cargo: saves from then load it into the truck's box.</summary>
        private const string OldTrailerBed = "loaner_trailer";
        public const string Model = "car_moving_truck";
        public const float Capacity = 9f;

        // The cargo box in the truck's frame (moving_truck.fbx at real size, facing +z, ground at 0): it sits on the
        // flatbed, whose top is 0.74 m up, from the back of the platform to just behind the cab.
        private const float Floor = 0.74f, BoxLength = 3.4f, BoxHalfWidth = 1f, BoxTop = 2.75f, Wall = 0.04f;
        // The driver's eye: in the cab, over the left seat (the body's middle is back on the flatbed).
        private static readonly Vector3 CabSeat = new Vector3(-0.4f, 1.4f, 0.8f);

        private HomeWorld _w;
        private FleetView _fleet;
        private DriveController _driver;
        private OwnedVehicle _vehicle;
        private float _nextPose;

        public bool Out => _vehicle != null;
        public OwnedVehicle Vehicle => _vehicle;
        public CarController Truck => _vehicle != null ? _fleet.Shown(_vehicle)?.GetComponent<CarController>() : null;
        public CargoDoor Door { get; private set; }

        public void Configure(HomeWorld world, FleetView fleet, DriveController driver)
        {
            _w = world;
            _fleet = fleet;
            _driver = driver;
        }

        private void Start()
        {
            // A loan still running in the save: the truck comes back where it was left.
            if (_w != null && _w.Game.Rental.Active && !Out)
            {
                var (x, y, z, yaw) = _w.Game.Rental.Pose;
                Spawn(new Vector3((float)x, (float)y, (float)z), (float)yaw);
            }
        }

        /// <summary>Lends the truck (the deposit is taken). An error, or null.</summary>
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
            Transform seat = truck.transform.Find("Seat");
            if (seat != null) seat.localPosition = CabSeat;
            CargoBox(truck);
            MoveOldTrailerCargo();
            return true;
        }

        /// <summary>
        /// The cargo box on the flatbed: floor (the load bed), sides, front and roof in white with the store's name and a
        /// green band, and the roll-up door at the back. The drive body's collider is cut back to the cab, so the
        /// inside of the box is empty space things can be set down in.
        /// </summary>
        private void CargoBox(GameObject truck)
        {
            Kit k = _w.City.Kit;
            Transform t = truck.transform;
            var body = truck.GetComponent<BoxCollider>();
            float rear = body.center.z - body.size.z / 2f, front = rear + BoxLength;
            float cabFront = body.center.z + body.size.z / 2f;
            body.center = new Vector3(body.center.x, body.center.y, (front + cabFront) / 2f);
            body.size = new Vector3(body.size.x, body.size.y, cabFront - front);

            Material white = _w.City.P.Lit(new Color(0.93f, 0.93f, 0.9f), 0.25f);
            Material band = _w.City.P.Lit(new Color(0.2f, 0.4f, 0.28f), 0.3f);
            Material ply = _w.City.P.Surface(Finish.WoodFloor, new Color(0.62f, 0.5f, 0.36f), 0.1f);
            float mid = (rear + front) / 2f, height = BoxTop - Floor;
            // The floor reaches down to the chassis (a steel base, plywood on top): it's also what the back bumps into.
            GameObject floor = k.Box(t, "Cargo floor", new Vector3(0f, (Floor + 0.3f) / 2f, mid), new Vector3(BoxHalfWidth * 2f - 0.1f, Floor - 0.3f, BoxLength),
                _w.City.P.Lit(new Color(0.2f, 0.2f, 0.21f), 0.35f));
            k.Box(t, "Plywood", new Vector3(0f, Floor + 0.005f, mid), new Vector3(BoxHalfWidth * 2f - 0.1f, 0.01f, BoxLength - 0.02f), ply, collider: false);
            var bed = floor.AddComponent<CargoBed>();
            bed.Configure(TruckBed, Capacity, t);
            _w.AddBed(bed);
            foreach (float side in new[] { -1f, 1f })
            {
                float face = side > 0f ? -90f : 90f;
                k.Box(t, "Box side", new Vector3(side * (BoxHalfWidth - Wall / 2f), Floor + height / 2f, mid), new Vector3(Wall, height, BoxLength), white);
                k.Box(t, "Band", new Vector3(side * (BoxHalfWidth + 0.002f), Floor + 0.35f, mid), new Vector3(0.004f, 0.3f, BoxLength - 0.05f), band, collider: false);
                k.Text(t, "TIMBERLINE HOME", new Vector3(side * (BoxHalfWidth + 0.01f), Floor + height * 0.62f, mid), face, 0.22f, new Color(0.2f, 0.4f, 0.28f));
                k.Text(t, "MOVING? BORROW THE TRUCK.", new Vector3(side * (BoxHalfWidth + 0.01f), Floor + height * 0.36f, mid), face, 0.09f, new Color(0.25f, 0.25f, 0.27f));
            }
            k.Box(t, "Box front", new Vector3(0f, Floor + height / 2f, front - Wall / 2f), new Vector3(BoxHalfWidth * 2f, height, Wall), white);
            k.Box(t, "Box roof", new Vector3(0f, BoxTop - Wall / 2f, mid), new Vector3(BoxHalfWidth * 2f, Wall, BoxLength), white);
            // The back: a frame round the opening, and the door, which rolls up under the roof.
            k.Box(t, "Door frame top", new Vector3(0f, BoxTop - 0.12f, rear + Wall / 2f), new Vector3(BoxHalfWidth * 2f, 0.24f, Wall), white);
            foreach (float side in new[] { -1f, 1f })
                k.Box(t, "Door post", new Vector3(side * (BoxHalfWidth - 0.05f), Floor + height / 2f, rear + Wall / 2f), new Vector3(0.1f, height, Wall), white);
            float opening = height - 0.24f;
            Transform roller = Kit.Group(t, "Roll-up door", new Vector3(0f, Floor, rear + Wall));
            GameObject door = k.Box(roller, "Door", new Vector3(0f, opening / 2f, 0f), new Vector3(BoxHalfWidth * 2f - 0.2f, opening, 0.03f), _w.City.P.Lit(new Color(0.82f, 0.82f, 0.8f), 0.3f));
            Material slat = _w.City.P.Lit(new Color(0.68f, 0.68f, 0.66f), 0.3f);
            for (float y = 0.25f; y < opening; y += 0.25f)
                k.Box(roller, "Slat", new Vector3(0f, y, -0.018f), new Vector3(BoxHalfWidth * 2f - 0.22f, 0.02f, 0.006f), slat, collider: false);
            Door = door.AddComponent<CargoDoor>();
            Door.Configure(roller, BoxTop - Wall - 0.03f, bed);
            SetLayer(t, CityLayers.Vehicle);
        }

        /// <summary>Cargo from a save made when the loaner was a pickup and trailer: into the box, in a row from the back.</summary>
        private void MoveOldTrailerCargo()
        {
            BoxCollider floor = _w.Bed(TruckBed).GetComponent<BoxCollider>();
            float z = floor.center.z - floor.size.z / 2f + 0.5f, last = floor.center.z + floor.size.z / 2f - 0.4f;
            bool moved = false;
            foreach (OwnedItem i in _w.Belongings.Items)
            {
                if (i.State != ItemState.Loaded || i.Vehicle != OldTrailerBed) continue;
                i.Vehicle = TruckBed;
                i.X = 0f; i.Y = Floor; i.Z = Mathf.Min(z, last); i.Yaw = 0f;
                z += Mathf.Max(0.5f, i.Item.Depth) + 0.1f;
                moved = true;
            }
            if (moved) _w.Belongings.Touch();
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
                    if (i.State == ItemState.Loaded && i.Vehicle == TruckBed) n++;
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
                if (i.State == ItemState.Loaded && i.Vehicle == TruckBed)
                {
                    i.State = ItemState.AtPickup;
                    i.Vehicle = "";
                }
            _w.Belongings.Touch();
            _w.RemoveBed(_w.Bed(TruckBed));
            Door = null;
            _w.Game.Vehicles.Remove(_vehicle);
            _vehicle = null;
            _fleet.Refresh();
            return result;
        }

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

    /// <summary>The truck's roll-up back door: up, the box can be loaded; down, things stay in.</summary>
    public sealed class CargoDoor : Interactable
    {
        private Transform _roller;
        private Vector3 _closed;
        private float _roof;
        private CargoBed _bed;

        /// <param name="roof">Height of the underside of the box's roof, where the rolled-up door lies.</param>
        public void Configure(Transform roller, float roof, CargoBed bed)
        {
            _roller = roller;
            _closed = roller.localPosition;
            _roof = roof;
            _bed = bed;
            Set(false);
        }

        public bool Up => _bed.Open;
        public override string Prompt => Up ? "Pull the door down" : "Roll the door up";
        public override void Interact() => Set(!Up);

        /// <summary>Up, the door runs up its rails and lies flat under the roof, its bottom edge at the opening to pull it down by.</summary>
        public void Set(bool up)
        {
            _bed.Open = up;
            _roller.localPosition = up ? new Vector3(_closed.x, _roof, _closed.z) : _closed;
            _roller.localRotation = up ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity; // its height turned to run into the box
        }
    }

    /// <summary>Timberline's loaner desk: borrow the moving truck, or hand it back from the return bay.</summary>
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
                if (!Loaner.Out) return $"Borrow the moving truck · {HomeWorld.Dollars(Rental.DepositAmount)} deposit, back in 2 h";
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
                if (!_desk.Confirm("borrow", $"The moving truck for two hours, {HomeWorld.Dollars(Rental.DepositAmount)} deposit? [E] again.")) return;
                string error = Loaner.Borrow();
                if (error != null) _w.Say(error);
                else _desk.Say($"Keys are in it. It's the white box truck in the yard; the back door rolls up. Back by {_w.Game.Rental.Due:h:mm tt}, please.");
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
