using System.Collections.Generic;
using System.Linq;
using OpeningBell.Casino;
using OpeningBell.Gameplay;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The Meridian Hotel (CASINO_SPEC §48–51, §67–71, §108–109): the hotel lobby off the casino floor (its own street
    /// door, the Tide Café, the front desk), the elevators (ground, the second floor, the guest floor, the penthouse and
    /// the roof; your key card decides where you can go), the tower above the casino with the three guest rooms and the
    /// penthouse (only the rooms you can enter are built), the rooftop helipad, and out front the valet and a weekend car
    /// display. The tower's structure is in MeridianTower.
    /// </summary>
    public static partial class MeridianHotel
    {
        /// <summary>Floor levels (local y) of the elevator's stops: ground, 2, the guest floor (5), penthouse, roof.</summary>
        public const float Guest = 18.02f, Penthouse = 32.02f, Roof = 36.82f;
        public static readonly Vector3 LiftAt = new Vector3(35.4f, 0f, 21f);

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        public static void Build(CityContext c, Transform m, Transform inner, Transform dyn, CasinoLife life)
        {
            Lobby(c, inner, dyn, life);
            Lift(c, dyn);
            Transform tower = Kit.Group(m, "Interior");
            tower.name = "Interior";
            Tower(c, m, tower, dyn);
            GuestFloor(c, tower, dyn);
            PenthouseFloor(c, tower, dyn);
            RoofTop(c, tower, dyn);
            CityLayers.Set(tower, CityLayers.Interior);
            Forecourt(c, m, dyn);
        }

        private static bool PenthouseGuest(CityContext c)
        {
            Hotel h = c.Game.Casino.Hotel;
            return h.Active(c.Game.Clock.Now) && h.Stay.Class == RoomClass.Penthouse;
        }

        // ---------------------------------------------------------------- lobby

        private static void Lobby(CityContext c, Transform inner, Transform dyn, CasinoLife life)
        {
            Kit k = c.Kit;
            Material marble = c.P.Surface(Finish.Tiles, new Color(0.9f, 0.88f, 0.84f), 0.65f);
            Material wood = c.P.Lit(new Color(0.24f, 0.12f, 0.07f), 0.45f);
            Material bronze = c.P.Metal(new Color(0.72f, 0.53f, 0.3f), 0.75f);
            k.Span(inner, "Hotel lobby floor", V(26.8f, 0.02f, Meridian.Wall / 2f), V(36.6f, 0.03f, 17f), marble, collider: false);
            // Planters between the casino and the hotel lobby: a line, not a wall.
            for (float z = 1.2f; z < 9f; z += 2.4f)
            {
                k.Box(inner, "Planter", V(26.6f, 0.35f, z), V(0.6f, 0.7f, 1.8f), wood);
                k.Real(inner, "potted_plant_04", V(26.6f, 0.7f, z), 0f, 0.9f);
            }

            // Reception: a long marble desk, the wall behind with the hotel's name.
            k.Span(inner, "Reception desk", V(31.8f, 0f, 12.6f), V(36.4f, 1.08f, 13.4f), wood);
            k.Span(inner, "Reception top", V(31.7f, 1.08f, 12.5f), V(36.5f, 1.13f, 13.5f), c.P.Lit(new Color(0.85f, 0.82f, 0.76f), 0.75f), collider: false);
            Material wall = c.P.Surface(Finish.PaintedPlaster, new Color(0.3f, 0.22f, 0.16f), 0.05f);
            k.WallX(inner, "Reception wall", 31.5f, 36.8f, 15.2f, 0f, Meridian.Ceiling, 0.2f, wall);
            TextMesh name = k.Text(inner, "MERIDIAN HOTEL", V(34.1f, 3.1f, 15.08f), 0f, 0.3f, Color.white);
            name.GetComponent<MeshRenderer>().sharedMaterial = c.P.Sign(new Color(1f, 0.9f, 0.7f));
            k.Text(inner, "RECEPTION · ROOMS · SUITES · PENTHOUSE", V(34.1f, 2.7f, 15.08f), 0f, 0.08f, new Color(0.95f, 0.9f, 0.8f));
            k.Real(inner, "brass_vase_02", V(35.9f, 1.13f, 13f), 0f, 0.8f);
            var always = new WorkSchedule { Shift = Hours.Of(0, 24) };
            StaffNpc clerk = StaffNpc.Create(k, dyn, "Receptionist", 9300, new Color(0.1f, 0.12f, 0.2f), always,
                new[] { V(34f, 0f, 14.3f), V(36f, 0f, 14.3f) }, 180f, new[] { NpcPose.Stand, NpcPose.Typing }, () => "Good evening. Checking in?",
                Meridian.Cycle("Rooms are on the fifth floor; the elevators are behind the desk.", "Check-out's at eleven.", "Members get a better rate."),
                c.Game, c.Hud, c.Player, look: "Suit");
            var desk = new GameObject("Front desk");
            desk.transform.SetParent(dyn, false);
            desk.transform.localPosition = V(34.1f, 1.3f, 12.35f);
            var box = desk.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = V(4.2f, 0.6f, 0.6f);
            desk.AddComponent<HotelDesk>().Configure(c, clerk);
            c.Anchor("casino_hotel_desk", inner.TransformPoint(V(34f, 0f, 11.2f)));

            // The elevator hall behind the desk.
            k.WallZ(inner, "Elevator hall wall", 15.2f, 19.3f, 31.5f, 0f, Meridian.Ceiling, 0.2f, wall);
            Meridian.HangingSign(c, inner, "ELEVATORS · HOTEL GUESTS", V(30.5f, 3.9f, 18.5f), new Color(1f, 0.9f, 0.7f), 3.2f);
            c.Anchor("casino_elevator", inner.TransformPoint(V(32.6f, 0f, 21f)));

            // The Tide Café in the front corner: coffee and pastries, 6 AM to 3 PM.
            k.Span(inner, "Café counter", V(34.6f, 0f, 2.5f), V(35.4f, 1.05f, 7f), wood);
            k.Span(inner, "Café top", V(34.5f, 1.05f, 2.4f), V(35.5f, 1.1f, 7.1f), c.P.Lit(new Color(0.2f, 0.2f, 0.22f), 0.8f), collider: false);
            k.Real(inner, "croissant", V(34.9f, 1.1f, 3.4f), 0f, 1f);
            TextMesh cafe = k.Text(inner, "TIDE CAFÉ", V(36.55f, 2.9f, 4.8f), 90f, 0.3f, Color.white);
            cafe.GetComponent<MeshRenderer>().sharedMaterial = c.P.Sign(new Color(0.5f, 0.95f, 0.85f));
            k.Text(inner, "COFFEE · PASTRIES · 6 AM – 3 PM", V(36.55f, 2.55f, 4.8f), 90f, 0.08f, new Color(0.95f, 0.9f, 0.8f));
            StaffNpc barista = StaffNpc.Create(k, dyn, "Barista", 9301, new Color(0.15f, 0.35f, 0.3f), new WorkSchedule { Shift = Hours.Of(6, 15) },
                new[] { V(36.1f, 0f, 4.8f), V(36.1f, 0f, 8.5f) }, 270f, new[] { NpcPose.Stand, NpcPose.Typing }, () => "Morning! What can I get you?",
                Meridian.Cycle("Oat latte's the favourite.", "Croissants come in at six.", "Long night at the tables?"), c.Game, c.Hud, c.Player, look: "Casual");
            var venue = new MenuVenue { Name = "Tide Café", Where = "Order", Items = CasinoMenus.Cafe, Staff = barista, Open = Hours.Of(6, 15) };
            Meridian.Counter(dyn, V(34.3f, 1.25f, 4.8f), V(0.6f, 0.3f, 4f), venue);
            foreach (float z in new[] { 4f, 7.5f })
            {
                k.Real(inner, "round_wooden_table_01", V(29.8f, 0.03f, z), 0f, 0.8f);
                k.Real(inner, "dining_chair_02", V(29f, 0.03f, z), 90f);
                Meridian.Chair(c, dyn, V(29f, 0f, z), 90f, "Sit down", 1.15f, venue);
            }
            c.Anchor("casino_cafe", inner.TransformPoint(V(33.6f, 0f, 4.8f)));
            life.AddSpot(k, dyn, V(30.6f, 0.03f, 7.5f), -90f, NpcPose.Sit, 9302, 0.3f);
            life.AddSpot(k, dyn, V(30f, 0f, 11.5f), 90f, NpcPose.Phone, 9303, 0.5f);
        }

        // ---------------------------------------------------------------- the elevator

        private static readonly string[] Labels = { "G", "2", "5", "PH", "R" };
        private static readonly float[] Stops = { 0.02f, MeridianUpstairs.Floor + 0.02f, Guest, Penthouse, Roof };

        /// <summary>
        /// The hotel elevator, facing west into the elevator hall on every floor. Anyone can ride between the ground and
        /// the second floor; the guest floor takes a room key, the penthouse and roof the penthouse key. Built like
        /// Harborview's: a car at each stop and the ride between them.
        /// </summary>
        private static void Lift(CityContext c, Transform dyn)
        {
            Kit k = c.Kit;
            Material walnut = c.P.Lit(new Color(0.3f, 0.19f, 0.12f), 0.4f);
            Material brass = c.P.Lit(new Color(0.75f, 0.6f, 0.35f), 0.75f);
            Material steel = c.P.Lit(new Color(0.55f, 0.55f, 0.57f), 0.75f);
            Material mirror = c.P.Lit(new Color(0.9f, 0.92f, 0.94f), 0.95f);
            Material lampOff = c.P.Unlit(new Color(0.25f, 0.25f, 0.24f));
            Material lampOn = c.P.Unlit(new Color(1f, 0.75f, 0.35f));
            // Local −z (the doors) turned to face west (−x).
            Transform shaft = Kit.Group(dyn, "Meridian elevator", LiftAt, 90f);
            var elevator = shaft.gameObject.AddComponent<Elevator>();
            var stops = new Elevator.FloorStop[Labels.Length];
            const float doorZ = -1.2f;
            for (int f = 0; f < Labels.Length; f++)
            {
                float y = Stops[f];
                k.Span(shaft, "Car floor", V(-1.4f, y - 0.1f, -1.2f), V(1.4f, y, 1.2f), c.P.Lit(new Color(0.2f, 0.19f, 0.18f), 0.5f));
                k.Span(shaft, "Car ceiling", V(-1.4f, y + 2.6f, -1.2f), V(1.4f, y + 2.7f, 1.2f), steel);
                k.Span(shaft, "Car back", V(-1.4f, y, 1.17f), V(1.4f, y + 2.6f, 1.23f), walnut);
                k.Span(shaft, "Car mirror", V(-1f, y + 0.9f, 1.15f), V(1f, y + 2.2f, 1.17f), mirror, collider: false);
                k.Span(shaft, "Car side", V(-1.45f, y, -1.2f), V(-1.4f, y + 2.6f, 1.2f), walnut);
                k.Span(shaft, "Car side", V(1.4f, y, -1.2f), V(1.45f, y + 2.6f, 1.2f), walnut);
                k.Span(shaft, "Car rail", V(-1.38f, y + 0.9f, -0.9f), V(-1.33f, y + 0.95f, 0.9f), brass, collider: false);
                k.Span(shaft, "Car front", V(-1.4f, y, doorZ - 0.03f), V(-0.66f, y + 2.6f, doorZ + 0.03f), steel);
                k.Span(shaft, "Car front", V(0.66f, y, doorZ - 0.03f), V(1.4f, y + 2.6f, doorZ + 0.03f), steel);
                k.Span(shaft, "Car front", V(-0.66f, y + 2.3f, doorZ - 0.03f), V(0.66f, y + 2.6f, doorZ + 0.03f), steel);
                c.PointLight(shaft, V(0f, y + 2.4f, 0f), 3.5f, 0.7f, new Color(1f, 0.93f, 0.82f));
                var stop = new Elevator.FloorStop { Label = Labels[f], Y = y };
                stop.DoorLeft = k.Box(shaft, "Door L", V(-0.33f, y + 1.15f, doorZ), V(0.66f, 2.3f, 0.05f), steel).transform;
                stop.DoorRight = k.Box(shaft, "Door R", V(0.33f, y + 1.15f, doorZ), V(0.66f, 2.3f, 0.05f), steel).transform;
                stop.HallIndicator = k.Text(shaft, Labels[f], V(0f, y + 2.55f, doorZ - 0.13f), 0f, 0.14f, new Color(1f, 0.7f, 0.35f));
                stop.CarIndicator = k.Text(shaft, Labels[f], V(0f, y + 2.42f, doorZ + 0.08f), 180f, 0.12f, new Color(1f, 0.7f, 0.35f));
                GameObject call = k.Box(shaft, "Call", V(1.05f, y + 1.1f, doorZ - 0.15f), V(0.14f, 0.2f, 0.04f), brass);
                stop.HallLamp = k.Box(shaft, "Call lamp", V(1.05f, y + 1.1f, doorZ - 0.18f), V(0.06f, 0.06f, 0.02f), lampOff, collider: false).GetComponent<Renderer>();
                call.AddComponent<ElevatorButton>().Configure(elevator, f, inCar: false);
                Transform board = Kit.Group(shaft, "Car panel", V(1.37f, y, 0f), 90f);
                var lamps = new List<Renderer>();
                var lampFloors = new List<int>();
                for (int b = 0; b < Labels.Length; b++)
                {
                    float by = 0.95f + b * 0.2f;
                    GameObject button = k.Box(board, "Button " + Labels[b], V(0f, by, -0.02f), V(0.12f, 0.12f, 0.03f), brass);
                    var eb = button.AddComponent<ElevatorButton>();
                    eb.Configure(elevator, b, inCar: true);
                    if (b == 2) eb.LockReason = () => c.Game.Casino.Hotel.Active(c.Game.Clock.Now) ? null : "hotel guests (room key)";
                    if (b >= 3) eb.LockReason = () => PenthouseGuest(c) ? null : "penthouse key only";
                    k.Text(board, Labels[b], V(-0.13f, by, -0.03f), 0f, 0.05f, new Color(0.1f, 0.1f, 0.1f));
                    if (b == f) continue;
                    lamps.Add(k.Box(board, "Lamp", V(0.09f, by, -0.03f), V(0.03f, 0.03f, 0.02f), lampOff, collider: false).GetComponent<Renderer>());
                    lampFloors.Add(b);
                }
                stop.CarLamps = lamps.ToArray();
                stop.CarLampFloors = lampFloors.ToArray();
                stops[f] = stop;
            }
            elevator.Configure(stops, new Vector2(1.4f, 1.2f), 1.3f, c.Player, lampOn, lampOff);
            elevator.Logic.StopHeights = Stops;
            elevator.Logic.SecondsPerMetre = 7f / 37f;
        }

        // ---------------------------------------------------------------- out front

        private static void Forecourt(CityContext c, Transform m, Transform dyn)
        {
            Kit k = c.Kit;
            Transform front = Kit.Group(m, "Forecourt");
            Material bronze = c.P.Metal(new Color(0.72f, 0.53f, 0.3f), 0.75f);
            // The valet podium at the kerb, left of the doors; cars come back to the pickup lane.
            k.Box(front, "Valet podium", V(-12f, 0.55f, -4.5f), V(0.8f, 1.1f, 0.5f), c.P.Lit(new Color(0.1f, 0.1f, 0.12f), 0.6f));
            k.Box(front, "Valet top", V(-12f, 1.12f, -4.5f), V(0.9f, 0.04f, 0.6f), bronze, collider: false);
            k.Text(front, "VALET", V(-12f, 0.8f, -4.76f), 0f, 0.1f, new Color(1f, 0.85f, 0.5f));
            k.Decal(front, "Pickup lane", V(-16f, 0.012f, -8f), new Vector2(6f, 2.6f), 0f, c.P.Lit(new Color(0.75f, 0.6f, 0.3f), 0.2f));
            k.Text(front, "VALET PICKUP", V(-16f, 0.02f, -6.6f), 0f, 0.2f, new Color(0.95f, 0.85f, 0.55f)).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            StaffNpc valet = StaffNpc.Create(k, dyn, "Valet", 9310, new Color(0.3f, 0.05f, 0.08f), new WorkSchedule { Shift = Hours.Of(0, 24) },
                new[] { V(-12f, 0f, -3.9f), V(-12f, 0f, -2.5f) }, 180f, new[] { NpcPose.Stand, NpcPose.Phone }, () => "Valet, sir or madam?",
                Meridian.Cycle("Leave it with me and ask for it back any time.", "Members with a Silver card park free.", "Had a supercar in earlier. Didn't let me drive it."),
                c.Game, c.Hud, c.Player, look: "Suit");
            Transform pickup = Kit.Group(dyn, "Valet pickup", V(-16f, 0f, -8f), 90f);
            var stand = new GameObject("Valet stand");
            stand.transform.SetParent(dyn, false);
            stand.transform.localPosition = V(-12f, 1.2f, -4.9f);
            var box = stand.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = V(1f, 0.6f, 0.5f);
            stand.AddComponent<ValetStand>().Configure(c, valet, pickup, Meridian.ValetBay, 270f);
            c.Anchor("casino_valet", m.TransformPoint(V(-12f, 0f, -6.5f)));
            c.Anchor("casino_valet_pickup", pickup.position);

            // Weekend display (§69): two of the most expensive cars on turntable plinths.
            var display = new GameObject("Car display");
            display.transform.SetParent(dyn, false);
            var show = display.AddComponent<CarDisplay>();
            foreach (float x in new[] { 21f, 28f })
            {
                k.Cylinder(front, "Plinth", V(x, 0.07f, -8f), 5.2f, 0.14f, c.P.Lit(new Color(0.08f, 0.08f, 0.09f), 0.8f), collider: true);
                k.Cylinder(front, "Plinth ring", V(x, 0.15f, -8f), 5.3f, 0.02f, bronze);
            }
            k.Text(front, "ON DISPLAY THIS WEEKEND", V(24.5f, 1.3f, -4.6f), 0f, 0.14f, new Color(1f, 0.85f, 0.5f));
            show.Configure(c.Game, m.TransformPoint(V(21f, 0.16f, -8f)), m.TransformPoint(V(28f, 0.16f, -8f)), m.eulerAngles.y + 60f);
        }
    }

    /// <summary>The weekend supercars out front: built once, shown Friday to Sunday (§69). Not drivable.</summary>
    public sealed class CarDisplay : MonoBehaviour
    {
        private GameBootstrap _game;
        private readonly List<GameObject> _cars = new List<GameObject>();
        private Vector3 _a, _b;
        private float _yaw, _next;
        private bool _built;

        public bool Showing => _cars.Count > 0 && _cars[0].activeSelf;

        public void Configure(GameBootstrap game, Vector3 a, Vector3 b, float yaw)
        {
            _game = game;
            _a = a;
            _b = b;
            _yaw = yaw;
        }

        private void Build()
        {
            _built = true;
            if (_game.VehicleLibrary == null) return;
            IEnumerable<VehicleModel> best = _game.Vehicles.Catalog.Models.Where(v => v.Kind == VehicleKind.Car).OrderByDescending(v => v.Price).Take(2);
            int i = 0;
            foreach (VehicleModel model in best)
            {
                GameObject mesh = _game.VehicleLibrary.CarMesh(model.Mesh);
                if (mesh == null) continue;
                CarController car = CarFactory.BuildDrivable(transform, mesh, model.Car.Clone(), "On display: " + model.Name);
                car.transform.SetPositionAndRotation(i == 0 ? _a : _b, Quaternion.Euler(0f, _yaw + i * 60f, 0f));
                car.SetParked(true);
                _cars.Add(car.gameObject);
                i++;
            }
        }

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 5f;
            if (!_built) Build();
            bool on = CasinoEvents.AutoDisplay(_game.Clock.Now);
            foreach (GameObject car in _cars)
                if (car.activeSelf != on) car.SetActive(on);
        }
    }
}
