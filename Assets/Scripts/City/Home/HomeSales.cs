using System.Collections.Generic;
using OpeningBell.Gameplay;
using OpeningBell.Home;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Homes on the market (TOWN_SPEC B): a sign out front with the price (buy with a second press; the money comes
    /// from the bank), open-house viewings by day, and once it's yours: the front door locks from a keypad, a switch
    /// by the door for the lights, a garage with a roll-up door behind the family houses and mansions. The Harborview
    /// penthouse is a glass storey on the tower's roof, reached by a freight hoist up the back.
    /// </summary>
    public static class HomeSales
    {
        public static readonly Hours OpenHouse = Hours.Of(10, 18);
        public const string PenthouseId = "penthouse";

        public static void Build(CityContext c, HomeWorld world)
        {
            Transform root = Kit.Group(c.Static, "Homes for sale");
            Transform dyn = Kit.Group(c.Dynamic, "Homes for sale");
            Penthouse(c, world, root, dyn);
            var doors = new List<Door>(Object.FindObjectsByType<Door>(FindObjectsSortMode.None));
            foreach (HomeSpec h in c.Homes)
            {
                if (h.Id == HomeSpec.ApartmentId) continue;
                Door door = Nearest(doors, h.Root.TransformPoint(h.DoorLocal));
                if (door != null)
                {
                    HomeSpec spec = h;
                    door.LockReason = () => world.Owns(spec)
                        ? (world.Estate.Locked(spec.Id) ? "locked (keypad)" : null)
                        : OpenHouse.Contains(world.Game.Clock.Now) ? null : "for sale · open house 10 AM–6 PM";
                }
                if (h.Kind != HomeKind.Penthouse) Sign(c, world, dyn, h);
                // Houses face the street (-z); the penthouse's door is on its north side (+z).
                float s = h.Kind == HomeKind.Penthouse ? -1f : 1f, yaw = h.Root.eulerAngles.y + (s < 0f ? 180f : 0f);
                Keypad(c, world, dyn, h, h.Root.TransformPoint(h.DoorLocal + new Vector3(1.1f, 1.3f, -0.12f * s)), yaw);
                Switch(c, world, dyn, h, h.Root.TransformPoint(h.DoorLocal + new Vector3(-0.9f, 1.3f, 0.2f * s)), yaw + 180f);
                Transform cupboard = Kit.Group(dyn, "Storage " + h.Id, h.Root.TransformPoint(h.DoorLocal + new Vector3(-1.5f, 0f, 0.35f * s)), yaw + 180f);
                c.Kit.Box(cupboard, "Cupboard", new Vector3(0f, 1f, 0f), new Vector3(0.6f, 2f, 0.3f), c.P.Lit(new Color(0.8f, 0.78f, 0.72f), 0.2f))
                    .AddComponent<StorageCupboard>().Configure(world, h);
                if (h.Kind == HomeKind.Family || h.Kind == HomeKind.Mansion) Garage(c, world, root, dyn, h);
            }
        }

        private static Door Nearest(List<Door> doors, Vector3 p)
        {
            Door best = null;
            float bestD = 2.5f;
            foreach (Door d in doors)
            {
                float dist = Vector2.Distance(new Vector2(d.transform.position.x, d.transform.position.z), new Vector2(p.x, p.z));
                if (dist < bestD && Mathf.Abs(d.transform.position.y - p.y) < 1.5f) { best = d; bestD = dist; }
            }
            return best;
        }

        private static void Sign(CityContext c, HomeWorld world, Transform dyn, HomeSpec h)
        {
            Kit k = c.Kit;
            Vector3 local = new Vector3(h.LotWidth / 2f - 1.3f, 0f, 1.3f);
            Transform g = Kit.Group(dyn, "For sale " + h.Id, h.Plot.TransformPoint(local), h.Plot.eulerAngles.y);
            Material post = c.P.Lit(new Color(0.95f, 0.95f, 0.93f), 0.2f);
            k.Box(g, "Post", new Vector3(0f, 0.7f, 0f), new Vector3(0.08f, 1.4f, 0.08f), post, collider: false);
            k.Box(g, "Arm", new Vector3(-0.35f, 1.35f, 0f), new Vector3(0.7f, 0.06f, 0.06f), post, collider: false);
            GameObject board = k.Box(g, "Board", new Vector3(-0.35f, 1f, 0f), new Vector3(0.62f, 0.45f, 0.03f), c.P.Lit(new Color(0.75f, 0.12f, 0.1f), 0.3f));
            TextMesh text = k.Text(g, "FOR SALE", new Vector3(-0.35f, 1.05f, -0.02f), 0f, 0.07f, Color.white);
            k.Text(g, "KELL VALLEY REALTY", new Vector3(-0.35f, 0.88f, -0.02f), 0f, 0.035f, Color.white);
            board.AddComponent<ForSaleSign>().Configure(world, h, text);
        }

        private static void Keypad(CityContext c, HomeWorld world, Transform dyn, HomeSpec h, Vector3 at, float yaw)
        {
            Transform g = Kit.Group(dyn, "Keypad " + h.Id, at, yaw);
            GameObject pad = c.Kit.Box(g, "Keypad", Vector3.zero, new Vector3(0.12f, 0.18f, 0.03f), c.P.Lit(new Color(0.15f, 0.15f, 0.16f), 0.5f));
            pad.AddComponent<HomeKeypad>().Configure(world, h);
        }

        private static void Switch(CityContext c, HomeWorld world, Transform dyn, HomeSpec h, Vector3 at, float yaw)
        {
            Transform g = Kit.Group(dyn, "Light switch " + h.Id, at, yaw);
            GameObject plate = c.Kit.Box(g, "Switch", Vector3.zero, new Vector3(0.08f, 0.12f, 0.02f), c.P.Lit(new Color(0.95f, 0.95f, 0.93f), 0.2f));
            plate.AddComponent<LightSwitch>().Configure(world, h);
        }

        /// <summary>A single garage at the back of the lot, its roll-up door facing the street.</summary>
        private static void Garage(CityContext c, HomeWorld world, Transform root, Transform dyn, HomeSpec h)
        {
            const float w = 4.4f, d = 6.8f, height = 2.9f;
            if (h.LotDepth < 34f) return; // not enough garden behind the house
            Kit k = c.Kit;
            Vector3 origin = new Vector3(0f, 0f, h.LotDepth - d - 1f);
            Transform g = Kit.Group(root, "Garage " + h.Id, h.Plot.TransformPoint(origin), h.Plot.eulerAngles.y);
            Material wall = c.P.Lit(new Color(0.85f, 0.83f, 0.78f), 0.08f);
            k.Span(g, "Slab", new Vector3(-w / 2f, -0.05f, 0f), new Vector3(w / 2f, 0.05f, d), c.P.Lit(new Color(0.6f, 0.6f, 0.58f), 0.1f));
            k.Span(g, "Wall L", new Vector3(-w / 2f, 0f, 0f), new Vector3(-w / 2f + 0.15f, height, d), wall);
            k.Span(g, "Wall R", new Vector3(w / 2f - 0.15f, 0f, 0f), new Vector3(w / 2f, height, d), wall);
            k.Span(g, "Back", new Vector3(-w / 2f, 0f, d - 0.15f), new Vector3(w / 2f, height, d), wall);
            k.Span(g, "Roof", new Vector3(-w / 2f - 0.2f, height, -0.2f), new Vector3(w / 2f + 0.2f, height + 0.2f, d + 0.2f), c.P.Lit(new Color(0.3f, 0.3f, 0.32f)));
            Transform door = Kit.Group(dyn, "Garage door " + h.Id, h.Plot.TransformPoint(origin), h.Plot.eulerAngles.y);
            GameObject panel = k.Box(door, "Roll-up door", new Vector3(0f, (height - 0.1f) / 2f, 0.05f), new Vector3(w - 0.3f, height - 0.1f, 0.06f), c.P.Lit(new Color(0.8f, 0.8f, 0.8f), 0.3f));
            GameObject button = k.Box(g, "Opener", new Vector3(w / 2f + 0.05f, 1.2f, 0.2f), new Vector3(0.05f, 0.14f, 0.1f), c.P.Lit(new Color(0.2f, 0.2f, 0.22f), 0.4f));
            _ = panel;
            button.AddComponent<GarageButton>().Configure(world, h, door, height);
        }

        /// <summary>
        /// A glass loft on Harborview's roof with a terrace round it; a freight hoist up the back of the tower (a car at
        /// street level and one at the roof, the player riding between them) and a glass bridge across to the loft.
        /// </summary>
        private static void Penthouse(CityContext c, HomeWorld world, Transform root, Transform dyn)
        {
            Kit k = c.Kit;
            Rect lot = Landmarks.HarborviewLot;
            float ground = StreetMap.Plan.StreetGrade(lot.center), roof = ground + 62f;
            Transform p = Kit.Group(root, "Penthouse", new Vector3(252f, roof, 112f));
            Material glass = c.P.Glass(new Color(0.65f, 0.78f, 0.85f, 0.3f));
            Material frame = c.P.Lit(new Color(0.18f, 0.19f, 0.2f), 0.5f);
            Material floor = c.P.Lit(new Color(0.55f, 0.45f, 0.35f), 0.35f);
            const float hw = 11f, hd = 9f, h = 3.6f;
            k.Span(p, "Floor", new Vector3(-hw, 0f, -hd), new Vector3(hw, 0.05f, hd), floor);
            k.Span(p, "Roof", new Vector3(-hw - 0.3f, h, -hd - 0.3f), new Vector3(hw + 0.3f, h + 0.3f, hd + 0.3f), frame);
            k.WallX(p, "Glass S", -hw, hw, -hd, 0f, h, 0.06f, glass);
            k.WallZ(p, "Glass W", -hd, hd, -hw, 0f, h, 0.06f, glass);
            k.WallZ(p, "Glass E", -hd, hd, hw, 0f, h, 0.06f, glass);
            k.WallX(p, "Glass N", -hw, hw, hd, 0f, h, 0.06f, glass, Opening.Door(-0.5f, 1.6f, 0f, 2.3f));
            k.WallX(p, "Bedroom wall", -hw, -3f, 2f, 0f, h, 0.12f, c.P.Lit(new Color(0.9f, 0.88f, 0.84f)), Opening.Door(-6f, 1f, 0f));
            foreach (float x in new[] { -hw, hw }) foreach (float z in new[] { -hd, hd })
                k.Box(p, "Mullion", new Vector3(x, h / 2f, z), new Vector3(0.12f, h, 0.12f), frame, collider: false);
            // Terrace rail round the roof's edge (the lit crown is only a band).
            foreach (var (a, b) in new[] { (new Vector2(-14f, -12f), new Vector2(14f, -12f)), (new Vector2(-14f, 12f), new Vector2(-1f, 12f)),
                         (new Vector2(2f, 12f), new Vector2(14f, 12f)), (new Vector2(-14f, -12f), new Vector2(-14f, 12f)), (new Vector2(14f, -12f), new Vector2(14f, 12f)) })
                k.Span(p, "Terrace rail", new Vector3(Mathf.Min(a.x, b.x) - 0.03f, 0f, Mathf.Min(a.y, b.y) - 0.03f), new Vector3(Mathf.Max(a.x, b.x) + 0.03f, 1.1f, Mathf.Max(a.y, b.y) + 0.03f), glass);
            c.PointLight(p, new Vector3(0f, h - 0.4f, 0f), 14f, 1f, new Color(1f, 0.94f, 0.85f));
            c.PointLight(p, new Vector3(-7f, h - 0.4f, 5f), 8f, 0.8f, new Color(1f, 0.94f, 0.85f));
            Transform front = Kit.Group(dyn, "Penthouse door", Vector3.zero);
            c.SlidingDoor(front, "penthouse", p.TransformPoint(new Vector3(-0.5f, 0f, hd)), 1.6f, 2.3f);

            // The glass bridge from the roof's back edge to the hoist, and the hoist's frame up the back of the podium.
            const float hoistX = 251.5f, hoistZ = 129.8f;
            k.Span(root, "Bridge floor", new Vector3(hoistX - 1.2f, roof - 0.15f, 124f), new Vector3(hoistX + 1.2f, roof + 0.02f, hoistZ - 1.3f), frame);
            k.Span(root, "Bridge glass W", new Vector3(hoistX - 1.25f, roof, 124f), new Vector3(hoistX - 1.2f, roof + 2.6f, hoistZ - 1.3f), glass);
            k.Span(root, "Bridge glass E", new Vector3(hoistX + 1.2f, roof, 124f), new Vector3(hoistX + 1.25f, roof + 2.6f, hoistZ - 1.3f), glass);
            k.Span(root, "Bridge roof", new Vector3(hoistX - 1.3f, roof + 2.6f, 124f), new Vector3(hoistX + 1.3f, roof + 2.7f, hoistZ - 1.3f), frame, collider: false);
            foreach (float x in new[] { hoistX - 1.6f, hoistX + 1.6f }) foreach (float z in new[] { hoistZ - 1.4f, hoistZ + 1.4f })
                k.Box(root, "Hoist post", new Vector3(x, (roof + 3f + ground) / 2f, z), new Vector3(0.18f, roof + 3f - ground, 0.18f), frame, collider: false);
            Elevator hoist = Hoist(c, dyn, new Vector3(hoistX, ground + 0.02f, hoistZ), roof - ground);
            // Only residents ride up.
            foreach (ElevatorButton button in hoist.GetComponentsInChildren<ElevatorButton>())
                if (button.Floor == 1 || !button.name.StartsWith("Button"))
                    button.LockReason = () => world.Estate.Owns(PenthouseId) ? null : OpenHouse.Contains(world.Game.Clock.Now) ? null : "residents only";
            Transform plot = Kit.Group(dyn, "Penthouse roof", new Vector3(252f, roof, 100f));
            c.Homes.Add(new HomeSpec
            {
                Id = PenthouseId, Name = "Harborview Penthouse", Kind = HomeKind.Penthouse, Price = HomeSpec.PriceOf(HomeKind.Penthouse),
                Root = p, Size = new Vector2(hw * 2f, hd * 2f), Floors = 1, Plot = plot, LotWidth = 28f, LotDepth = 24f, DoorLocal = new Vector3(-0.5f, 0f, hd),
            });
            // The sign's in the lobby yard, by the hoist.
            HomeSpec spec = c.Homes[c.Homes.Count - 1];
            Transform g = Kit.Group(dyn, "For sale penthouse", new Vector3(hoistX + 3f, ground, hoistZ + 2.5f), 180f);
            GameObject board = k.Box(g, "Board", new Vector3(0f, 1.3f, 0f), new Vector3(0.9f, 0.6f, 0.04f), c.P.Lit(new Color(0.1f, 0.2f, 0.35f), 0.3f));
            TextMesh text = k.Text(g, "PENTHOUSE", new Vector3(0f, 1.4f, -0.03f), 0f, 0.09f, Color.white);
            k.Box(g, "Post", new Vector3(0f, 0.5f, 0f), new Vector3(0.08f, 1f, 0.08f), frame, collider: false);
            board.AddComponent<ForSaleSign>().Configure(world, spec, text);
            c.Anchor("penthouse_hoist", new Vector3(hoistX, ground, hoistZ + 2.6f));
            c.Anchor("penthouse_inside", p.TransformPoint(new Vector3(0f, 0.05f, 0f)));
        }

        /// <summary>Two stops (street and roof), each with its own car room; doors north at the street, south at the roof.</summary>
        private static Elevator Hoist(CityContext c, Transform dyn, Vector3 at, float rise)
        {
            Kit k = c.Kit;
            Material metal = c.P.Lit(new Color(0.45f, 0.46f, 0.48f), 0.6f);
            Material mesh = c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.4f);
            Material lampOff = c.P.Unlit(new Color(0.25f, 0.25f, 0.24f));
            Material lampOn = c.P.Unlit(new Color(1f, 0.75f, 0.35f));
            Transform shaft = Kit.Group(dyn, "Freight hoist", at);
            var elevator = shaft.gameObject.AddComponent<Elevator>();
            var stops = new Elevator.FloorStop[2];
            string[] labels = { "G", "PH" };
            for (int f = 0; f < 2; f++)
            {
                float y = f == 0 ? 0f : rise;
                float doorZ = f == 0 ? 1.3f : -1.3f, backZ = -doorZ;
                k.Span(shaft, "Car floor", new Vector3(-1.4f, y - 0.1f, -1.2f), new Vector3(1.4f, y, 1.2f), metal);
                k.Span(shaft, "Car ceiling", new Vector3(-1.4f, y + 2.6f, -1.2f), new Vector3(1.4f, y + 2.7f, 1.2f), mesh);
                k.Span(shaft, "Car back", new Vector3(-1.4f, y, backZ - 0.03f), new Vector3(1.4f, y + 2.6f, backZ + 0.03f), mesh);
                k.Span(shaft, "Car side W", new Vector3(-1.45f, y, -1.2f), new Vector3(-1.4f, y + 2.6f, 1.2f), mesh);
                k.Span(shaft, "Car side E", new Vector3(1.4f, y, -1.2f), new Vector3(1.45f, y + 2.6f, 1.2f), mesh);
                c.PointLight(shaft, new Vector3(0f, y + 2.4f, 0f), 3.5f, 0.7f, new Color(1f, 0.95f, 0.85f));
                var stop = new Elevator.FloorStop { Label = labels[f], Y = y };
                stop.DoorLeft = k.Box(shaft, "Door L", new Vector3(-0.33f, y + 1.15f, doorZ), new Vector3(0.66f, 2.3f, 0.05f), metal).transform;
                stop.DoorRight = k.Box(shaft, "Door R", new Vector3(0.33f, y + 1.15f, doorZ), new Vector3(0.66f, 2.3f, 0.05f), metal).transform;
                float face = f == 0 ? 180f : 0f; // indicators read from outside the doors
                stop.HallIndicator = k.Text(shaft, labels[f], new Vector3(0f, y + 2.55f, doorZ + Mathf.Sign(doorZ) * 0.12f), face, 0.14f, new Color(1f, 0.55f, 0.2f));
                stop.CarIndicator = k.Text(shaft, labels[f], new Vector3(0f, y + 2.42f, doorZ - Mathf.Sign(doorZ) * 0.16f), face + 180f, 0.12f, new Color(1f, 0.55f, 0.2f));
                GameObject call = k.Box(shaft, "Call", new Vector3(1.05f, y + 1.1f, doorZ + Mathf.Sign(doorZ) * 0.13f), new Vector3(0.14f, 0.2f, 0.04f), metal);
                stop.HallLamp = k.Box(shaft, "Call lamp", new Vector3(1.05f, y + 1.1f, doorZ + Mathf.Sign(doorZ) * 0.16f), new Vector3(0.06f, 0.06f, 0.02f), lampOff, collider: false).GetComponent<Renderer>();
                call.AddComponent<ElevatorButton>().Configure(elevator, f, inCar: false);
                Transform board = Kit.Group(shaft, "Car panel", new Vector3(1.37f, y, 0f), 90f);
                for (int b = 0; b < 2; b++)
                {
                    GameObject button = k.Box(board, "Button " + labels[b], new Vector3(0f, 1.05f + b * 0.25f, -0.02f), new Vector3(0.12f, 0.12f, 0.03f), metal);
                    button.AddComponent<ElevatorButton>().Configure(elevator, b, inCar: true);
                    k.Text(board, labels[b], new Vector3(-0.11f, 1.05f + b * 0.25f, -0.03f), 0f, 0.05f, new Color(0.1f, 0.1f, 0.1f));
                    if (b != f)
                    {
                        stop.CarLamp = k.Box(board, "Lamp", new Vector3(0.09f, 1.05f + b * 0.25f, -0.03f), new Vector3(0.03f, 0.03f, 0.02f), lampOff, collider: false).GetComponent<Renderer>();
                        stop.CarLampFloor = b;
                    }
                }
                stops[f] = stop;
            }
            elevator.Configure(stops, new Vector2(1.4f, 1.2f), 1.3f, c.Player, lampOn, lampOff);
            return elevator;
        }
    }

    /// <summary>The realtor's sign: price and rooms; [E] twice buys it (from the bank).</summary>
    public sealed class ForSaleSign : Interactable
    {
        private HomeWorld _w;
        private HomeSpec _home;
        private TextMesh _text;
        private float _armedUntil;

        public void Configure(HomeWorld world, HomeSpec home, TextMesh text)
        {
            _w = world;
            _home = home;
            _text = text;
        }

        private bool Owned => _w.Owns(_home);

        public override string Prompt => Owned ? $"{_home.Name} · yours" : $"Buy {_home.Name} · {HomeWorld.Dollars(_home.Price)}";

        public override string Details => _home.Kind switch
        {
            HomeKind.Starter => "One storey: living and kitchen, a bedroom, a bathroom. Sold empty.",
            HomeKind.Family => "Two storeys: living, kitchen, bath; two bedrooms upstairs; garage out back. Sold empty.",
            HomeKind.Mansion => "Modern villa: foyer, living and dining wings, three bedrooms, a pool and a garage. Sold empty.",
            HomeKind.Penthouse => "Glass loft on top of Harborview Tower, wraparound terrace, private freight hoist. Sold empty.",
            _ => null,
        } + (Owned ? "" : "\nOpen house 10 AM–6 PM: go and have a look first.");

        private void Update()
        {
            if (_text != null) _text.text = Owned ? "SOLD" : _home.Kind == HomeKind.Penthouse ? "PENTHOUSE" : "FOR SALE";
        }

        public override void Interact()
        {
            if (Owned) { _w.Say("It's yours. Keys work, the keypad's by the front door."); return; }
            if (Time.unscaledTime > _armedUntil)
            {
                _armedUntil = Time.unscaledTime + 6f;
                _w.Say($"Buy {_home.Name} for {HomeWorld.Dollars(_home.Price)}? It comes out of the bank. [E] again to sign.");
                return;
            }
            _armedUntil = 0f;
            string error = _w.Buy(_home);
            _w.Say(error ?? $"Congratulations: {_home.Name} is yours.");
        }
    }

    /// <summary>Front-door keypad: lock or unlock (yours only).</summary>
    public sealed class HomeKeypad : Interactable
    {
        private HomeWorld _w;
        private HomeSpec _home;

        public void Configure(HomeWorld world, HomeSpec home)
        {
            _w = world;
            _home = home;
        }

        public override bool CanInteract => base.CanInteract && _w.Owns(_home);
        public override string Prompt => _w.Estate.Locked(_home.Id) ? "Unlock the door" : "Lock the door";
        public override void Interact() => _w.Estate.SetLocked(_home.Id, !_w.Estate.Locked(_home.Id));
    }

    /// <summary>The hall cupboard: what you've put away at this home, one thing at a time back into your hands.</summary>
    public sealed class StorageCupboard : Interactable
    {
        private HomeWorld _w;
        private HomeSpec _home;

        public void Configure(HomeWorld world, HomeSpec home)
        {
            _w = world;
            _home = home;
        }

        private OwnedItem Next => _w.Belongings.In(ItemState.Stored).Find(i => i.Property == _home.Id);
        public override bool CanInteract => base.CanInteract && _w.Owns(_home) && !_w.Hands.Holding;
        public override string Prompt => Next is OwnedItem i ? $"Take out the {i.Item.Name}" : "Storage (empty · [B] while carrying to put away)";

        public override void Interact()
        {
            if (Next is OwnedItem i) _w.Hands.TakeNew(i);
        }
    }

    /// <summary>The lights in a home you own: all on or off from the switch by the door.</summary>
    public sealed class LightSwitch : Interactable
    {
        private HomeWorld _w;
        private HomeSpec _home;
        private readonly Dictionary<Light, float> _full = new Dictionary<Light, float>();
        private int _applied = -1;

        public void Configure(HomeWorld world, HomeSpec home)
        {
            _w = world;
            _home = home;
        }

        public override bool CanInteract => base.CanInteract && _w.Owns(_home);
        public override string Prompt => _w.Estate.LightsOn(_home.Id) ? "Lights off" : "Lights on";
        public override void Interact() => _w.Estate.SetLights(_home.Id, !_w.Estate.LightsOn(_home.Id));

        private void Update()
        {
            if (_w == null || _applied == _w.Estate.Version) return;
            _applied = _w.Estate.Version;
            bool on = !_w.Owns(_home) || _w.Estate.LightsOn(_home.Id);
            // Intensity, not enabled: the distance gating owns that switch.
            foreach (Light l in _home.Root.GetComponentsInChildren<Light>(true))
            {
                if (!_full.ContainsKey(l)) _full[l] = l.intensity;
                l.intensity = on ? _full[l] : 0f;
            }
        }
    }

    /// <summary>The garage door opener: rolls the door up into the roof and back down.</summary>
    public sealed class GarageButton : Interactable
    {
        private HomeWorld _w;
        private HomeSpec _home;
        private Transform _door;
        private Vector3 _closedAt;
        private float _height, _open;

        public void Configure(HomeWorld world, HomeSpec home, Transform door, float height)
        {
            _w = world;
            _home = home;
            _door = door;
            _closedAt = door.localPosition;
            _height = height;
            _open = world.Estate.GarageOpen(home.Id) ? 1f : 0f;
            Apply();
        }

        public override bool CanInteract => base.CanInteract && _w.Owns(_home);
        public override string Prompt => _w.Estate.GarageOpen(_home.Id) ? "Close the garage" : "Open the garage";
        public override void Interact() => _w.Estate.SetGarage(_home.Id, !_w.Estate.GarageOpen(_home.Id));
        public bool IsOpen => _open > 0.95f;

        private void Update()
        {
            float want = _w.Estate.GarageOpen(_home.Id) ? 1f : 0f;
            if (Mathf.Approximately(_open, want)) return;
            _open = Mathf.MoveTowards(_open, want, Time.deltaTime / 3f);
            Apply();
        }

        private void Apply()
        {
            // Rolls up: the panel shortens from the bottom into the drum under the roof.
            _door.localScale = new Vector3(1f, Mathf.Max(0.02f, 1f - _open), 1f);
            _door.localPosition = _closedAt + Vector3.up * ((_height - 0.1f) * _open);
        }
    }
}
