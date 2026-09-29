using System.Collections.Generic;
using OpeningBell.Gameplay;
using OpeningBell.Home;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Timberline Home (furniture) and Circuit Stop (tech) on the block north of Maple between Lumber Rd and Willow Ave
    /// (TOWN_SPEC B), with the collection yard behind them off Grove St: the pickup counter, the loaner desk, the
    /// RENTAL RETURN bay. Every catalog item stands in a showroom: aim for its card, [E] to buy (twice to confirm),
    /// [E] on the tag beside it for the next colour. Tech comes boxed straight into your hands; furniture waits at
    /// the pickup counter, or goes home on the delivery van.
    /// </summary>
    public static class HomeStores
    {
        public const string FurnitureName = "Timberline Home", TechName = "Circuit Stop";
        public static readonly Hours FurnitureHours = Hours.Of(9, 21), TechHours = Hours.Of(10, 20);
        private static readonly Vector2 FurnitureFront = new Vector2(-325f, -5.5f), TechFront = new Vector2(-283f, -5.5f);
        private const float FurnitureWidth = 46f, FurnitureDepth = 34f, TechWidth = 28f, TechDepth = 22f;

        /// <summary>The collection yard behind the stores (things may be set down here).</summary>
        public static readonly Rect Yard = Rect.MinMaxRect(-348f, 30.5f, -262f, 61f);
        /// <summary>Park the loaner in here to hand it back.</summary>
        public static readonly Rect ReturnBay = Rect.MinMaxRect(-282f, 40f, -268f, 56f);
        /// <summary>Where the loaner waits when you borrow it (facing north, the trailer behind).</summary>
        public static readonly Vector3 LoanerSpot = new Vector3(-300f, 0f, 44f);
        /// <summary>Bays where collected items are set out.</summary>
        public static Vector3 PickupSlot(int i) => new Vector3(-330f + (i % 6) * 3f, 0f, 36f + (i / 6) * 3.2f);

        public static void AddPads(CityContext c) =>
            c.Pads.Add(Pad.FromRect(Rect.MinMaxRect(-351f, -5.5f, -259f, 61.5f), 0f));

        public static void Build(CityContext c, HomeWorld world)
        {
            world.PickupYard = Yard;
            var furniture = Store(c, world, FurnitureName, FurnitureFront, FurnitureWidth, FurnitureDepth, FurnitureHours, HomeStore.Furniture,
                new Color(0.2f, 0.4f, 0.28f), -12f, 9101);
            Store(c, world, TechName, TechFront, TechWidth, TechDepth, TechHours, HomeStore.Tech, new Color(0.15f, 0.35f, 0.7f), -6f, 9102);
            YardAndCounters(c, world, furniture);
        }

        private static StoreDesk Store(CityContext c, HomeWorld world, string name, Vector2 front, float width, float depth, Hours hours,
            HomeStore which, Color brand, float doorX, int seed)
        {
            Kit k = c.Kit;
            Transform root = Kit.Group(c.Static, name, new Vector3(front.x, 0f, front.y));
            Transform dyn = Kit.Group(c.Dynamic, name, new Vector3(front.x, 0f, front.y));
            float hw = width / 2f, top = 5f;
            var f = new Rect(-hw, 0f, width, depth);
            Material outside = c.P.Lit(new Color(0.82f, 0.8f, 0.76f), 0.1f);
            Material inside = c.P.Lit(new Color(0.93f, 0.93f, 0.91f), 0.05f);
            Material floor = c.P.Lit(which == HomeStore.Furniture ? new Color(0.62f, 0.5f, 0.38f) : new Color(0.75f, 0.76f, 0.78f), 0.3f);
            Shops.ShopShell(c, root, f, top, Businesses.Wall, outside, inside, floor, doorX, 2.6f,
                new[] { ((-hw + doorX - 1.3f) / 2f, doorX - 1.3f + hw - 1.6f), ((doorX + 1.3f + hw) / 2f, hw - doorX - 2.9f) });
            k.Span(root, "Roof", new Vector3(-hw, top, 0f), new Vector3(hw, top + 0.3f, depth), c.P.Lit(new Color(0.24f, 0.24f, 0.25f)));
            k.Span(root, "Sign band", new Vector3(-hw + 0.5f, top - 0.9f, -0.16f), new Vector3(hw - 0.5f, top - 0.1f, 0f), c.P.Lit(brand, 0.3f), collider: false);
            k.Text(root, name.ToUpperInvariant(), new Vector3(0f, top - 0.5f, -0.18f), 0f, 0.5f, Color.white);
            k.Text(root, hours.Describe(), new Vector3(doorX + 2f, 2.4f, -0.03f), 0f, 0.07f, new Color(0.95f, 0.93f, 0.88f));
            c.SlidingDoor(dyn, name, new Vector3(doorX, 0f, Businesses.Wall / 2f), 2.6f, 2.4f);
            c.PointLight(root, new Vector3(0f, top - 0.6f, depth * 0.35f), width * 0.7f, 1.2f, new Color(1f, 0.96f, 0.9f));
            c.PointLight(root, new Vector3(0f, top - 0.6f, depth * 0.75f), width * 0.7f, 1.2f, new Color(1f, 0.96f, 0.9f));

            // The counter by the door, with someone behind it.
            Vector3 counter = new Vector3(hw - 5f, 0f, 3.5f);
            k.Box(root, "Counter", counter + new Vector3(0f, 0.5f, 0f), new Vector3(3.6f, 1f, 0.8f), c.P.Lit(new Color(0.3f, 0.27f, 0.24f), 0.3f));
            var desk = dyn.gameObject.AddComponent<StoreDesk>();
            var schedule = new WorkSchedule { Shift = hours };
            string greeting = which == HomeStore.Furniture ? "Welcome to Timberline. Everything on the floor is in stock." : "Hi! Need a hand with a setup?";
            string[] talk = which == HomeStore.Furniture
                ? new[] { "Pickup's round the back off Grove St. We lend a truck and trailer, too.", "Delivery's $79 and comes next morning." }
                : new[] { "Arms clamp to the back of the desk. Six screens a desk, tops.", "Everything comes boxed, unpack it at home." };
            int line = 0;
            StaffNpc staff = StaffNpc.Create(k, dyn, which == HomeStore.Furniture ? "Sales" : "Tech", seed, brand, schedule,
                new List<Vector3> { counter + new Vector3(0f, 0f, 1.2f), new Vector3(hw - 1f, 0f, depth - 1.5f) }, 180f,
                new[] { NpcPose.Stand, NpcPose.Typing }, () => greeting, () => talk[line++ % talk.Length], c.Game, c.Hud, c.Player);
            desk.Configure(world, name, hours, staff);
            if (which == HomeStore.Furniture)
            {
                GameObject deliveries = k.Box(dyn, "Delivery desk", counter + new Vector3(-2.4f, 0.5f, 0f), new Vector3(1.2f, 1f, 0.8f), c.P.Lit(brand, 0.3f));
                deliveries.AddComponent<DeliveryDesk>().Configure(world, desk);
            }

            // The showroom: everything in the catalog on a grid, wall pieces on the back wall.
            var items = new List<HomeItem>();
            foreach (HomeItem i in HomeCatalog.Items) if (i.Store == which && !i.IsBox) items.Add(i);
            int slot = 0, wall = 0;
            float cols = which == HomeStore.Furniture ? 9 : 6, spacing = (width - 6f) / (cols - 1);
            float rowGap = which == HomeStore.Furniture ? 6f : 4f;
            foreach (HomeItem item in items)
            {
                Vector3 at;
                float h = 0f;
                if (item.Support == Support.Wall)
                {
                    at = new Vector3(-hw + 4f + wall++ * 2.5f, 1.2f, depth - Businesses.Wall - item.Depth / 2f - 0.01f);
                }
                else
                {
                    int col = slot % (int)cols, row = slot / (int)cols;
                    slot++;
                    at = new Vector3(-hw + 3f + col * spacing, 0f, 8f + row * rowGap);
                    // Tech sits on display tables; ceiling lamps hang from a frame.
                    if (item.Store == HomeStore.Tech && item.Support != Support.Floor)
                    {
                        k.Box(dyn, "Display table", at + new Vector3(0f, 0.4f, 0f), new Vector3(Mathf.Max(1.2f, item.Width + 0.4f), 0.8f, 0.9f), c.P.Lit(new Color(0.85f, 0.86f, 0.88f), 0.3f));
                        h = 0.8f;
                    }
                    else if (item.Support == Support.Ceiling) h = top - 0.2f - item.Height;
                }
                Showroom(c, world, desk, dyn, item, at + Vector3.up * h);
            }
            c.Anchor(which == HomeStore.Furniture ? "timberline_door" : "circuit_door", dyn.TransformPoint(new Vector3(doorX, 0f, -1.5f)));
            c.Place(dyn.TransformPoint(new Vector3(doorX, 0f, -1.3f)), PlaceKind.Door, name);
            c.PlaceInfo[name] = (PlaceCategory.Shop, hours);
            return desk;
        }

        private static void Showroom(CityContext c, HomeWorld world, StoreDesk desk, Transform parent, HomeItem item, Vector3 at)
        {
            Transform g = Kit.Group(parent, "Display " + item.Id, at);
            GameObject model = HomeModels.Build(c, g, item, 0, false);
            var box = g.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, item.Height / 2f, 0f);
            box.size = new Vector3(item.Width, Mathf.Max(0.05f, item.Height), Mathf.Max(0.05f, item.Depth));
            var display = g.gameObject.AddComponent<ShowroomItem>();
            display.Configure(world, desk, item, model);
            // Price tag on a post in front, the colour picker.
            Vector3 tag = new Vector3(Mathf.Min(item.Width / 2f + 0.2f, 1.4f), 0f, -item.Depth / 2f - 0.35f);
            if (at.y > 1.5f) tag = new Vector3(0f, -at.y, 0f) + new Vector3(item.Width / 2f + 0.3f, 0f, 0f);
            GameObject post = c.Kit.Box(g, "Tag post", tag + new Vector3(0f, 0.45f, 0f), new Vector3(0.04f, 0.9f, 0.04f), c.P.Lit(new Color(0.3f, 0.3f, 0.3f)), collider: false);
            GameObject card = c.Kit.Box(g, "Tag", tag + new Vector3(0f, 0.95f, 0f), new Vector3(0.36f, 0.24f, 0.03f), c.P.Lit(new Color(0.97f, 0.96f, 0.92f)));
            TextMesh price = c.Kit.Text(g, HomeWorld.Dollars(item.Price), tag + new Vector3(0f, 0.97f, -0.02f), 0f, 0.05f, new Color(0.1f, 0.1f, 0.1f));
            card.AddComponent<ColourTag>().Configure(display);
            _ = post;
            _ = price;
        }

        private static void YardAndCounters(CityContext c, HomeWorld world, StoreDesk furniture)
        {
            Kit k = c.Kit;
            Transform root = Kit.Group(c.Static, "Timberline yard");
            Transform dyn = Kit.Group(c.Dynamic, "Timberline yard");
            Rect y = Yard;
            k.Span(root, "Yard", new Vector3(y.xMin, -0.05f, y.yMin), new Vector3(y.xMax, 0.01f, y.yMax), c.P.Lit(new Color(0.3f, 0.3f, 0.31f), 0.1f))
                .AddComponent<SurfaceTag>().Roughness = 0.3f;
            // Driveway out to Grove St.
            float grove = StreetMap.Plan.StreetGrade(new Vector2(-288f, 70f));
            GameObject drive = k.Span(root, "Driveway", new Vector3(-292f, -0.05f, y.yMax - 0.1f), new Vector3(-284f, 0.01f, 65.2f), c.P.Lit(new Color(0.42f, 0.42f, 0.41f), 0.1f));
            if (Mathf.Abs(grove) > 0.05f)
            {
                float run = 65.2f - y.yMax;
                drive.transform.position += Vector3.up * grove / 2f;
                drive.transform.rotation = Quaternion.Euler(-Mathf.Atan2(grove, run) * Mathf.Rad2Deg, 0f, 0f);
            }
            // Bays for collected things and the return bay.
            for (int i = 0; i < 12; i++)
            {
                Vector3 s = PickupSlot(i);
                k.Decal(root, "Pickup bay", s + new Vector3(0f, 0.015f, 0f), new Vector2(2.6f, 2.8f), 0f, c.P.Lit(new Color(0.42f, 0.4f, 0.3f), 0.1f));
            }
            k.Text(root, "PICKUP", new Vector3(-322.5f, 0.02f, 33.5f), 0f, 0.6f, new Color(0.9f, 0.88f, 0.8f)).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Rect r = ReturnBay;
            foreach (var (a, b) in new[] { (new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin)), (new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax)),
                         (new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax)), (new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax)) })
            {
                Vector2 m = (a + b) / 2f;
                k.Decal(root, "Return bay line", new Vector3(m.x, 0.016f, m.y), new Vector2(Mathf.Max(0.15f, Mathf.Abs(b.x - a.x)), Mathf.Max(0.15f, Mathf.Abs(b.y - a.y))), 0f, c.P.Lit(new Color(0.95f, 0.75f, 0.15f), 0.2f));
            }
            k.Text(root, "RENTAL RETURN", new Vector3(r.center.x, 0.02f, r.yMin + 1.5f), 0f, 0.55f, new Color(0.95f, 0.75f, 0.15f)).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // The collection booth: pickup window and the loaner desk.
            Vector3 booth = new Vector3(-340f, 0f, 36f);
            k.Span(root, "Booth", booth + new Vector3(-4f, 0f, -3f), booth + new Vector3(4f, 3f, 3f), c.P.Lit(new Color(0.35f, 0.45f, 0.38f), 0.2f));
            k.Span(root, "Booth roof", booth + new Vector3(-4.5f, 3f, -3.5f), booth + new Vector3(4.5f, 3.3f, 3.5f), c.P.Lit(new Color(0.24f, 0.24f, 0.25f)));
            k.Text(root, "PICKUP  ·  LOANER TRUCK", booth + new Vector3(4.02f, 2.4f, 0f), -90f, 0.25f, Color.white);
            GameObject pickup = k.Box(dyn, "Pickup window", booth + new Vector3(4.2f, 1f, -1.2f), new Vector3(0.4f, 1f, 1.6f), c.P.Lit(new Color(0.6f, 0.62f, 0.64f), 0.4f));
            pickup.AddComponent<PickupCounter>().Configure(world, furniture);
            GameObject loaner = k.Box(dyn, "Loaner desk", booth + new Vector3(4.2f, 1f, 1.4f), new Vector3(0.4f, 1f, 1.6f), c.P.Lit(new Color(0.85f, 0.6f, 0.15f), 0.4f));
            loaner.AddComponent<RentalDesk>().Configure(world, furniture);
            c.Anchor("timberline_pickup", booth + new Vector3(5.5f, 0f, -1.2f));
            c.Anchor("timberline_loaner_desk", booth + new Vector3(5.5f, 0f, 1.4f));
        }
    }

    /// <summary>A store's till: open hours, the person behind it, and buying with a second press to confirm.</summary>
    public sealed class StoreDesk : MonoBehaviour
    {
        private HomeWorld _w;
        private StaffNpc _staff;
        private string _pending;
        private float _pendingUntil;

        public string Name { get; private set; }
        public Hours Hours { get; private set; }
        public bool Open => Hours.Contains(_w.Game.Clock.Now);

        public void Configure(HomeWorld world, string name, Hours hours, StaffNpc staff)
        {
            _w = world;
            Name = name;
            Hours = hours;
            _staff = staff;
        }

        public void Say(string line)
        {
            if (_staff != null && _staff.isActiveAndEnabled) _staff.Say(line);
            else _w.Say(line);
        }

        /// <summary>True on the second press within a few seconds (the first one asks).</summary>
        public bool Confirm(string what, string ask)
        {
            if (_pending == what && Time.unscaledTime < _pendingUntil)
            {
                _pending = null;
                return true;
            }
            _pending = what;
            _pendingUntil = Time.unscaledTime + 6f;
            Say(ask);
            return false;
        }

        private HomeItem _pendingItem;
        private int _pendingVariant;

        /// <summary>A company to bill instead of the player's own card (the fund, once it holds its office), or null.</summary>
        private OpeningBell.Fund.HedgeFund Company => _w.OfficeHeld ? _w.Game.Fund : null;

        /// <summary>Buys one: tech (boxed) into your hands if they're free, everything else to the pickup counter.</summary>
        public OwnedItem Buy(HomeItem item, int variant)
        {
            if (!Open) { _w.Say($"{Name} is closed. {Hours.Describe()}"); return null; }
            string colour = item.Variants.Length > 1 ? item.Variants[variant] + " " : "";
            _pendingItem = item;
            _pendingVariant = variant;
            string company = Company != null ? $" · [F] bill {Company.Name}" : "";
            if (!Confirm(item.Id + variant, $"The {colour}{item.Name}, {HomeWorld.Dollars(item.Price)}? [E] again: your card{company}.")) return null;
            _pendingItem = null;
            return Purchase(item, variant, company: false);
        }

        private void Update()
        {
            // [F] while a purchase is waiting to be confirmed bills the company card instead.
            if (_pendingItem == null || Company == null || Time.unscaledTime > _pendingUntil) return;
            var keys = UnityEngine.InputSystem.Keyboard.current;
            if (keys == null || !keys.fKey.wasPressedThisFrame) return;
            HomeItem item = _pendingItem;
            _pendingItem = null;
            _pending = null;
            Purchase(item, _pendingVariant, company: true);
        }

        /// <summary>Pays (the player's bank, or the company) and hands it over or sends it to the pickup counter.</summary>
        public OwnedItem Purchase(HomeItem item, int variant, bool company)
        {
            string error = company
                ? Company?.BuyEquipment(item.Price, $"{Name}: {item.Name}") ?? "No company to bill."
                : _w.Game.Economy.Spend(item.Price, $"{Name}: {item.Name}", _w.Game.Clock.Now);
            if (error != null)
            {
                Say(company ? "The company card's been declined, sorry." : "Card's been declined, sorry.");
                _w.Say(error);
                return null;
            }
            bool hands = item.Boxed && !_w.Hands.Holding;
            OwnedItem owned = _w.Belongings.Add(item.Id, variant, hands ? ItemState.Carried : ItemState.AtPickup);
            if (company) owned.Owner = "fund";
            if (hands)
            {
                _w.Hands.TakeNew(owned);
                Say("Here you go. Unbox it at home.");
            }
            else
            {
                // A free moving box with the order (one waiting at a time): pack everything in it for the trip home.
                bool boxWaiting = false;
                foreach (OwnedItem i in _w.Belongings.Items) if (i.IsBox && i.State == ItemState.AtPickup) boxWaiting = true;
                if (!boxWaiting) _w.Belongings.Add("moving_box", 0, ItemState.AtPickup);
                Say(item.Boxed ? "Your hands are full, so it's at the pickup counter round the back. There's a moving box with it."
                    : "Paid. It's at the pickup counter round the back with a moving box, or we can deliver it.");
            }
            return owned;
        }
    }

    /// <summary>A showroom piece: aim for its card, [E] to buy it in the colour on its tag.</summary>
    public sealed class ShowroomItem : Interactable
    {
        private HomeWorld _w;
        private StoreDesk _desk;
        private GameObject _model;
        public HomeItem Item { get; private set; }
        public int Variant { get; private set; }

        public void Configure(HomeWorld world, StoreDesk desk, HomeItem item, GameObject model)
        {
            _w = world;
            _desk = desk;
            Item = item;
            _model = model;
        }

        public override string Prompt => $"Buy {Item.Name} · {HomeWorld.Dollars(Item.Price)}";

        public override string Details
        {
            get
            {
                string size = $"{Item.Width:0.0#} × {Item.Depth:0.0#} × {Item.Height:0.0#} m";
                string extra = Item.IsDesk ? $"\nTakes {Item.MonitorSlots} monitors standing (arms add more, 6 a desk)"
                    : Item.IsArm ? $"\nHolds {Item.Arms} monitor{(Item.Arms == 1 ? "" : "s")}; clamps to a desk"
                    : Item.IsMonitor ? $"\n{Item.Inches}\" · shows charts, watchlists, news" : "";
                string colour = Item.Variants.Length > 1 ? $"\nColour: {Item.Variants[Variant]} ([E] on the tag for another)" : "";
                string where = Item.Outdoor ? "indoor or outdoor" : "indoor";
                return $"{Item.Department} · {Item.Tier}{(Item.Brand != null ? " · " + Item.Brand : "")} · {size} · {where}{extra}{colour}";
            }
        }

        public override void Interact() => _desk.Buy(Item, Variant);

        public void NextColour()
        {
            Variant = (Variant + 1) % Item.Variants.Length;
            HomeModels.Tint(_model, HomeModels.ColourOf(Item, Variant));
        }
    }

    public sealed class ColourTag : Interactable
    {
        private ShowroomItem _item;
        public void Configure(ShowroomItem item) => _item = item;
        public override bool CanInteract => base.CanInteract && _item.Item.Variants.Length > 1;
        public override string Prompt => $"Colour: {_item.Item.Variants[_item.Variant]} (next)";
        public override void Interact() => _item.NextColour();
    }

    /// <summary>Timberline's delivery desk: everything waiting at pickup, home on the van.</summary>
    public sealed class DeliveryDesk : Interactable
    {
        private HomeWorld _w;
        private StoreDesk _desk;

        public void Configure(HomeWorld world, StoreDesk desk)
        {
            _w = world;
            _desk = desk;
        }

        private int Waiting => _w.Belongings.Count(ItemState.AtPickup);

        public override string Prompt => Waiting == 0 ? "Home delivery (nothing waiting)" : $"Deliver {Waiting} item{(Waiting == 1 ? "" : "s")} to {_w.DeliveryTarget} · {HomeWorld.Dollars(HomeWorld.DeliveryFee)}";

        public override void Interact()
        {
            if (Waiting == 0) { _desk.Say("Buy something first and I'll book the van."); return; }
            if (!_desk.Open) { _w.Say($"{_desk.Name} is closed."); return; }
            if (!_desk.Confirm("delivery", $"{Waiting} item{(Waiting == 1 ? "" : "s")} to {_w.DeliveryTarget} for {HomeWorld.Dollars(HomeWorld.DeliveryFee)}? [E] again to book.")) return;
            string error = _w.BookDelivery();
            if (error != null) _w.Say(error);
        }
    }

    /// <summary>The pickup window: what you've bought comes out into the bays to load up.</summary>
    public sealed class PickupCounter : Interactable
    {
        private HomeWorld _w;
        private StoreDesk _desk;

        public void Configure(HomeWorld world, StoreDesk desk)
        {
            _w = world;
            _desk = desk;
        }

        private int Waiting => _w.Belongings.Count(ItemState.AtPickup);
        public override string Prompt => Waiting == 0 ? "Pickup (nothing waiting)" : $"Collect {Waiting} item{(Waiting == 1 ? "" : "s")}";

        public override void Interact()
        {
            if (Waiting == 0) { _w.Say("Nothing waiting for you."); return; }
            if (!_desk.Open) { _w.Say("The pickup window's shut. " + _desk.Hours.Describe()); return; }
            int slot = 0;
            foreach (OwnedItem i in _w.Belongings.Items)
            {
                if (i.State != ItemState.AtPickup) continue;
                // The next empty bay.
                Vector3 at;
                do at = HomeStores.PickupSlot(slot++);
                while (slot < 60 && Physics.CheckBox(at + Vector3.up * 0.6f, new Vector3(1.1f, 0.5f, 1.2f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore));
                i.State = ItemState.Placed;
                i.Property = "yard";
                i.X = at.x; i.Y = at.y; i.Z = at.z; i.Yaw = 0f;
            }
            _w.Belongings.Touch();
            _w.Say("It's out in the pickup bays. Load up!");
        }
    }
}
