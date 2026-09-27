using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Gameplay;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Kell Auto &amp; Body (WORLD_SPEC Phase 12, TOWN_SPEC A6), on Pine Rd in the Foundry, 8 AM–6 PM. Drive into one
    /// of the three bays and park; the service board in the office lists what they'll do to the car in the bay
    /// (repairs priced by damage, tyres, performance upgrades, paint, tint). Pay by card (a second press confirms)
    /// and your car goes up on the lift while they work, then comes down changed.
    /// </summary>
    public sealed class MechanicShop : MonoBehaviour
    {
        public static readonly Hours ShopHours = Hours.Of(8, 18);
        public const string Name = "Kell Auto & Body";
        /// <summary>Front centre on Pine Rd's west sidewalk edge; the shop runs west from there.</summary>
        public static readonly Vector2 Front = new Vector2(-258.5f, -112f);
        public const float Width = 44f, Depth = 30f;

        private CityContext _c;
        private StaffNpc _staff;
        private readonly List<Transform> _lifts = new List<Transform>();
        private readonly List<Rect> _bays = new List<Rect>(); // local frame
        private readonly List<GameObject> _doors = new List<GameObject>();
        private object _pending;
        private float _pendingUntil;
        private bool _working;

        public bool Working => _working;
        public bool Open => _staff != null && _staff.AtStation;
        private Fleet Fleet => _c.Game.Vehicles;

        public static void AddPads(CityContext c) =>
            c.Pads.Add(new Pad(Front + new Vector2(-Depth / 2f, 0f), new Vector2(Width / 2f + 1f, Depth / 2f + 1f), -90f, 0f));

        public static MechanicShop Build(CityContext c)
        {
            Kit k = c.Kit;
            // Local frame: x along Pine (world +z), +z into the shop (world -x).
            Transform root = Kit.Group(c.Static, Name, new Vector3(Front.x, 0f, Front.y), -90f);
            Transform dyn = Kit.Group(c.Dynamic, Name, new Vector3(Front.x, 0f, Front.y), -90f);
            var shop = dyn.gameObject.AddComponent<MechanicShop>();
            shop._c = c;
            Material block = c.P.Lit(new Color(0.62f, 0.62f, 0.6f), 0.05f);
            Material inside = c.P.Lit(new Color(0.8f, 0.8f, 0.78f), 0.05f);
            Material floor = c.P.Lit(new Color(0.46f, 0.46f, 0.45f), 0.25f);
            Material brand = c.P.Lit(new Color(0.75f, 0.15f, 0.1f), 0.3f);
            Material red = c.P.Lit(new Color(0.75f, 0.12f, 0.1f), 0.4f);
            Material steel = c.P.Lit(new Color(0.55f, 0.57f, 0.6f), 0.5f);
            const float h = 6.5f, hw = Width / 2f;

            k.Span(root, "Floor", new Vector3(-hw, 0f, 0f), new Vector3(hw, 0.03f, Depth), floor).AddComponent<SurfaceTag>().Roughness = 0.2f;
            k.Span(root, "Back wall", new Vector3(-hw, 0f, Depth - 0.3f), new Vector3(hw, h, Depth), block);
            k.Span(root, "South wall", new Vector3(-hw, 0f, 0f), new Vector3(-hw + 0.3f, h, Depth), block);
            k.Span(root, "North wall", new Vector3(hw - 0.3f, 0f, 0f), new Vector3(hw, h, Depth), block);
            k.Span(root, "Roof", new Vector3(-hw - 0.3f, h, -0.5f), new Vector3(hw + 0.3f, h + 0.4f, Depth + 0.3f), c.P.Lit(new Color(0.3f, 0.3f, 0.31f)));
            k.Span(root, "Sign band", new Vector3(-hw, 5.2f, -0.2f), new Vector3(hw, 6.4f, 0f), brand, collider: false);
            k.Text(root, "KELL AUTO & BODY", new Vector3(0f, 5.85f, -0.22f), 0f, 0.6f, Color.white);
            k.Text(root, "REPAIRS · TYRES · PERFORMANCE · PAINT   8 AM – 6 PM", new Vector3(-12f, 4.8f, -0.22f), 0f, 0.13f, Color.white);

            // Office on the south end: front wall with a door and window, an inner wall to the bays.
            const float officeEnd = -10f;
            k.WallX(root, "Office front", -hw, officeEnd, 0.15f, 0f, 4.6f, 0.3f, block, Opening.Door(-16f, 1.2f), new Opening(-19.5f, 3f, 0.9f, 2.6f), new Opening(-12.6f, 2.6f, 0.9f, 2.6f));
            k.Pane(root, "Office window", new Vector3(-21f, 0.9f, 0.12f), new Vector3(-18f, 2.6f, 0.18f), new Color(0.6f, 0.7f, 0.75f, 0.35f));
            k.Pane(root, "Office window", new Vector3(-13.9f, 0.9f, 0.12f), new Vector3(-11.3f, 2.6f, 0.18f), new Color(0.6f, 0.7f, 0.75f, 0.35f));
            k.Span(root, "Office lintel", new Vector3(-hw, 4.6f, 0f), new Vector3(officeEnd, h, 0.3f), block);
            k.WallZ(root, "Office side", 0f, 14f, officeEnd, 0f, 4.6f, 0.2f, inside, Opening.Door(10f, 1f));
            k.Span(root, "Office ceiling", new Vector3(-hw, 4.6f, 0f), new Vector3(officeEnd, 4.7f, 14f), inside, collider: false);
            k.Span(root, "Office back", new Vector3(-hw, 0f, 13.9f), new Vector3(officeEnd, 4.6f, 14.1f), inside);
            Door door = c.SwingDoor(dyn, "door", new Vector3(-16.6f, 0f, 0.15f), 1.2f, 2.25f, c.P.Glass(new Color(0.6f, 0.7f, 0.75f, 0.35f)), glass: true);
            door.LockReason = () => ShopHours.Contains(c.Game.Clock.Now) ? null : "closed (opens 8 AM)";
            k.Span(root, "Service desk", new Vector3(-21f, 0f, 9f), new Vector3(-13f, 1f, 9.8f), c.P.Lit(new Color(0.25f, 0.25f, 0.27f), 0.4f));
            for (int i = 0; i < 4; i++) k.Fit(root, "chair", new Vector3(-20.5f + i * 1f, 0f, 2.2f), new Vector3(0.55f, 0f, 0f));
            k.Fit(root, "kitchenCoffeeMachine", new Vector3(-11f, 0f, 3f), new Vector3(0.5f, 0f, 0f), 270f);
            k.Fit(root, "pottedPlant", new Vector3(-21.4f, 0f, 5f), new Vector3(0.6f, 0f, 0f));
            c.PointLight(root, new Vector3(-16f, 4.2f, 6f), 9f, 1f, new Color(1f, 0.95f, 0.88f));

            // Bays: three, each with a lift, a tool chest and a roll-up door.
            for (int i = 0; i < 3; i++)
            {
                float x0 = -9f + i * 10f, x1 = x0 + 9f, cx = (x0 + x1) / 2f;
                shop._bays.Add(Rect.MinMaxRect(x0 + 0.5f, 1f, x1 - 0.5f, 22f));
                if (i > 0) k.Span(root, "Bay wall", new Vector3(x0 - 0.5f, 0f, 0f), new Vector3(x0 + 0.1f, 1.2f, 20f), block);
                k.Span(root, "Door head", new Vector3(x0, 4.6f, 0f), new Vector3(x1, h, 0.3f), block);
                if (i < 2) k.Span(root, "Pier", new Vector3(x1, 0f, 0f), new Vector3(x1 + 1f, h, 0.3f), block);
                // Lift posts either side; the arms and platform move with the car.
                foreach (float side in new[] { -1f, 1f })
                    k.Box(root, "Lift post", new Vector3(cx + side * 1.8f, 1.6f, 11f), new Vector3(0.35f, 3.2f, 0.35f), red);
                Transform lift = Kit.Group(dyn, "Lift", new Vector3(cx, 0f, 11f));
                foreach (float side in new[] { -1f, 1f })
                    k.Box(lift, "Arm", new Vector3(side * 1.1f, 0.12f, 0f), new Vector3(0.25f, 0.12f, 3.6f), steel, collider: false);
                shop._lifts.Add(lift);
                // Each bay: a roller tool chest against the back, a tool cart by the lift, tyres waiting to go on.
                if (k.Fit(root, "metal_tool_chest", new Vector3(x1 - 1f, 0f, 20.5f), new Vector3(1.2f, 0f, 0f), 180f) == null)
                {
                    k.Box(root, "Tool chest", new Vector3(x1 - 1f, 0.6f, 20.5f), new Vector3(1.2f, 1.2f, 0.6f), red);
                    k.Box(root, "Tool chest top", new Vector3(x1 - 1f, 1.5f, 20.5f), new Vector3(1.2f, 0.6f, 0.5f), red);
                }
                k.Real(root, "tool_cart", new Vector3(cx + 2.6f, 0f, 13.5f), 200f);
                k.Real(root, "metal_toolbox", new Vector3(cx - 2.5f, 0f, 16f), 30f);
                for (int t = 0; t < 3; t++)
                {
                    // Placed upright (centre 0.3 m up), then laid flat about its centre: a stack of three.
                    GameObject tyre = k.Real(root, "old_tyre", new Vector3(x0 + 0.8f, -0.215f + t * 0.17f, 18.5f), 0f);
                    if (tyre != null) tyre.transform.localRotation *= Quaternion.Euler(90f, 0f, 0f);
                }
                // Roll-up door: up while the shop's open.
                GameObject rollup = k.Span(dyn, "Roll-up door", new Vector3(x0, 0f, 0.05f), new Vector3(x1, 4.6f, 0.2f), c.P.Lit(new Color(0.7f, 0.72f, 0.74f), 0.3f));
                shop._doors.Add(rollup);
                // Kerb cut from Pine into the bay.
                const float run = 1.4f, rise = -CityPlan.RoadY;
                GameObject ramp = k.Box(root, "Kerb ramp", new Vector3(cx, CityPlan.RoadY + rise / 2f - 0.03f, -CityPlan.SidewalkWidth - run / 2f),
                    new Vector3(8f, 0.06f, Mathf.Sqrt(run * run + rise * rise)), c.P.Lit(new Color(0.58f, 0.57f, 0.54f), 0.08f));
                ramp.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(rise, run) * Mathf.Rad2Deg, 0f, 0f);
                c.PointLight(root, new Vector3(cx, 6f, 11f), 12f, 1.1f, new Color(0.95f, 0.97f, 1f));
                k.Text(root, "BAY " + (i + 1), new Vector3(cx, 5.05f, 0.35f), 0f, 0.25f, new Color(0.95f, 0.95f, 0.9f));
            }
            // Parts room at the back: steel racking with boxed parts, oil and cans, tyres along the wall.
            var parts = new System.Random(9301);
            string[] onRack = { "cardboard_box_01", "cardboard_box_01", "oil_tin", "multi_cleaner_5_litre", "plastic_jerrycan", "cardboard_box_01" };
            for (float x = -8f; x < 20f; x += 1.2f)
            {
                if (k.Real(root, "steel_frame_shelves_01", new Vector3(x + 0.55f, 0f, 29f), 180f) == null)
                {
                    k.Span(root, "Parts shelf", new Vector3(x, 0f, 26.5f), new Vector3(x + 1.1f, 2.4f, 29.5f), steel);
                    continue;
                }
                foreach (float yy in new[] { 0.05f, 0.73f, 1.41f })
                    ShopStock.Spread(k, root, onRack, new Vector3(x + 0.55f, yy, 29f), 1f, 180f, parts, 2);
            }
            for (float x = -8f; x < 20f; x += 0.7f)
                k.Real(root, "old_tyre", new Vector3(x, 0.3f, 27.2f), 0f);
            k.Real(root, "hand_truck", new Vector3(19f, 0f, 25.5f), 250f);

            Vector3 station = new Vector3(-17f, 0f, 10.8f);
            shop._staff = StaffNpc.Create(k, dyn, "Mechanic", 9301, new Color(0.2f, 0.3f, 0.45f), new WorkSchedule { Shift = ShopHours },
                new List<Vector3> { station, new Vector3(-11.5f, 0f, 10.8f), new Vector3(-9f, 0f, 16f) }, 180f,
                new[] { NpcPose.Typing, NpcPose.Stand, NpcPose.Phone },
                () => "Pull it into a bay and I'll take a look. Board's on the wall.",
                () => "Turbo's the fun one. Brakes first, though, if you're asking me.",
                c.Game, c.Hud, c.Player, look: "Worker");

            // The service board on the office's inner wall, facing the desk.
            Transform board = Kit.Group(root, "Service board", new Vector3(officeEnd - 0.12f, 0f, 7f), 90f);
            k.Box(board, "Board", new Vector3(0f, 2.4f, 0.04f), new Vector3(6.4f, 3.6f, 0.06f), c.P.Lit(new Color(0.12f, 0.12f, 0.13f)), collider: false);
            k.Text(board, "SERVICES — CAR IN THE BAY", new Vector3(0f, 4.0f, -0.01f), 0f, 0.13f, new Color(1f, 0.85f, 0.35f));
            var items = new List<(string Id, string Label)> { ("repair", "Engine & body repair"), ("tyres", "New tyres") };
            foreach (CarUpgrade u in CarTuning.Upgrades) items.Add((u.Id, u.Name));
            for (int i = 0; i < CarTuning.Paints.Length; i++) items.Add(("paint:" + i, "Paint: " + CarTuning.Paints[i].Name));
            for (int i = 0; i < items.Count; i++)
            {
                int col = i < 10 ? 0 : 1, row = i < 10 ? i : i - 10;
                var at = new Vector3(-1.55f + col * 3.1f, 3.6f - row * 0.3f, -0.02f);
                k.Text(board, items[i].Label.ToUpperInvariant(), at, 0f, 0.075f, Color.white);
                var aim = new GameObject("Service " + items[i].Id);
                aim.transform.SetParent(board, false);
                aim.transform.localPosition = at;
                var box = aim.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(2.9f, 0.26f, 0.3f);
                aim.AddComponent<MechanicService>().Configure(shop, items[i].Id, items[i].Label);
            }

            c.Place(root.TransformPoint(new Vector3(-16f, 0f, -0.8f)), PlaceKind.Door, Name);
            c.Anchor("mechanic_door", root.TransformPoint(new Vector3(-16f, 0f, -2f)));
            c.Anchor("mechanic_board", root.TransformPoint(new Vector3(-13f, 0f, 7f)));
            c.Anchor("mechanic_bay1", root.TransformPoint(new Vector3(-4.5f, 0f, 11f)));
            return shop;
        }

        private void Update()
        {
            if (_c == null) return;
            // Roll-up doors: rolled away (gone, collider and all) while the shop's open.
            bool open = ShopHours.Contains(_c.Game.Clock.Now) || _working;
            foreach (GameObject door in _doors)
                if (door.activeSelf == open) door.SetActive(!open);
        }

        /// <summary>Your car parked in a bay (the one you drove last, if several are), and which bay.</summary>
        public OwnedVehicle CarInBay(out int bay)
        {
            bay = -1;
            OwnedVehicle best = null;
            Transform frame = transform;
            foreach (OwnedVehicle v in Fleet.Vehicles)
            {
                if (v.Kind != VehicleKind.Car || v.State != VehicleState.Parked || v.TestDrive) continue;
                Vector3 local = frame.InverseTransformPoint(new Vector3((float)v.X, 0f, (float)v.Z));
                for (int i = 0; i < _bays.Count; i++)
                    if (_bays[i].Contains(new Vector2(local.x, local.z)) && (best == null || v == Fleet.LastRidden))
                    {
                        best = v;
                        bay = i;
                    }
            }
            return best;
        }

        private double ModelPrice(OwnedVehicle v) => Fleet.Catalog.TryGetModel(v.ModelId, out VehicleModel m) ? m.Price : 20000;

        public decimal Price(string id, OwnedVehicle v)
        {
            if (id == "repair") return CarTuning.RepairPrice(v, ModelPrice(v));
            if (id == "tyres") return CarTuning.TyresPrice(ModelPrice(v));
            if (id.StartsWith("paint:")) return CarTuning.PaintPrice;
            return CarTuning.TryGet(id, out CarUpgrade u) ? u.Price : 0m;
        }

        private static string Dollars(decimal d) => "$" + d.ToString("N0", CultureInfo.InvariantCulture);

        public string PromptFor(string id, string label)
        {
            OwnedVehicle v = CarInBay(out _);
            if (v == null) return label + " · drive your car into a bay";
            if (id == "repair" && v.Condition >= 0.995) return $"{label} · your {v.Name} is in perfect shape";
            if (id == "tyres" && v.TireCondition >= 0.98) return $"{label} · tyres are new";
            if (v.Parts.Contains(id)) return $"{label} · already fitted";
            return $"{label} · {Dollars(Price(id, v))} for your {v.Name}";
        }

        public string DetailsFor(string id)
        {
            OwnedVehicle v = CarInBay(out _);
            string what = id == "repair" ? "Engine, drivetrain and bodywork back to 100%."
                : id == "tyres" ? "A fresh set of all-season tyres."
                : id.StartsWith("paint:") ? "A full respray."
                : CarTuning.TryGet(id, out CarUpgrade u) ? u.Description : "";
            return v == null ? what : what + $"\n{v.Name}: condition {v.Condition:P0} · tyres {v.TireCondition:P0}";
        }

        public void Buy(string id, string label)
        {
            if (!Open || _working) return;
            OwnedVehicle v = CarInBay(out int bay);
            if (v == null)
            {
                _staff.Say("I need the car in a bay first. Pull it in, any of the three.");
                return;
            }
            if (v.Parts.Contains(id) || (id == "repair" && v.Condition >= 0.995) || (id == "tyres" && v.TireCondition >= 0.98))
            {
                _staff.Say("Nothing to do there, it's already sorted.");
                return;
            }
            decimal price = Price(id, v);
            if (!Confirm(id + v.Id, $"{label} on the {v.Name}: {Dollars(price)}. [E] again and I'll get started.")) return;
            System.DateTime now = _c.Game.Clock.Now;
            string error = _c.Game.Economy.Spend(price, $"{Name}: {label}", now);
            if (error != null)
            {
                _staff.Say("Card got declined. Come back when it clears.");
                _c.Hud.ShowToast(error);
                return;
            }
            if (id == "repair") Fleet.Service(v, ServiceKind.TuneUp, now, price, "Engine & body repair");
            else if (id == "tyres") Fleet.Service(v, ServiceKind.NewTires, now, price);
            else if (id.StartsWith("paint:"))
            {
                var p = CarTuning.Paints[int.Parse(id.Substring(6), CultureInfo.InvariantCulture)];
                Fleet.Paint(v, p.R, p.G, p.B, p.Name, now, price);
            }
            else CarTuning.Install(Fleet, v, id, now, price);
            _staff.Say("On it. Give me a few minutes.");
            StartCoroutine(Service(v, bay, label));
        }

        private bool Confirm(string what, string ask)
        {
            if (_pending is string p && p == what && Time.unscaledTime < _pendingUntil)
            {
                _pending = null;
                return true;
            }
            _pending = what;
            _pendingUntil = Time.unscaledTime + 6f;
            _staff.Say(ask);
            return false;
        }

        /// <summary>Up on the lift, a little work, back down; then the car is rebuilt with its new parts and paint.</summary>
        private IEnumerator Service(OwnedVehicle v, int bay, string label)
        {
            _working = true;
            GameObject car = _c.FleetView != null ? _c.FleetView.Shown(v) : null;
            Transform lift = _lifts[bay];
            if (car != null && car.TryGetComponent(out ParkedVehicle handle)) handle.enabled = false;
            Vector3 carStart = car != null ? car.transform.position : Vector3.zero;
            Vector3 liftStart = lift.localPosition;
            const float up = 1.7f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 2.5f)
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                lift.localPosition = liftStart + Vector3.up * up * k;
                if (car != null) car.transform.position = carStart + Vector3.up * up * k;
                yield return null;
            }
            yield return new WaitForSeconds(3f);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 2.5f)
            {
                float k = 1f - Mathf.SmoothStep(0f, 1f, t);
                lift.localPosition = liftStart + Vector3.up * up * k;
                if (car != null) car.transform.position = carStart + Vector3.up * up * k;
                yield return null;
            }
            lift.localPosition = liftStart;
            if (car != null) car.transform.position = carStart;
            _c.FleetView?.Rebuild(v);
            _working = false;
            _staff.Say("All done. Take it easy on it for the first few miles.");
            _c.Hud.ShowToast($"{label}: done. Your {v.Name} is ready in bay {bay + 1}.");
        }
    }

    /// <summary>One line on the mechanic's service board: aim at it for the price on your car, [E] to buy.</summary>
    public sealed class MechanicService : Interactable
    {
        private MechanicShop _shop;
        private string _id, _label;

        public void Configure(MechanicShop shop, string id, string label)
        {
            _shop = shop;
            _id = id;
            _label = label;
        }

        public override bool CanInteract => base.CanInteract && _shop.Open && !_shop.Working;
        public override string Prompt => _shop.PromptFor(_id, _label);
        public override string Details => _shop.DetailsFor(_id);
        public override void Interact() => _shop.Buy(_id, _label);
    }
}
