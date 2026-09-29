using System;
using System.Collections.Generic;

namespace OpeningBell.Home
{
    /// <summary>What an item stands on (TOWN_SPEC B placement rules).</summary>
    public enum Support
    {
        /// <summary>Sofas, beds, desks, chairs, lamps, plants.</summary>
        Floor,
        /// <summary>On a desk, table, nightstand or TV stand: monitors, keyboards, lamps, speakers.</summary>
        Surface,
        /// <summary>Hung on a wall: art, clocks, mirrors, shelves.</summary>
        Wall,
        /// <summary>Hung from the ceiling.</summary>
        Ceiling,
        /// <summary>Clamped to a desk's back edge: monitor arms.</summary>
        DeskMount,
    }

    public enum HomeStore { Furniture, Tech }
    public enum Tier { Budget, Mid, Premium }

    /// <summary>One thing the stores sell. Sizes are metres (width along x, depth along z, height).</summary>
    public sealed class HomeItem
    {
        public string Id, Name, Department, Model, Brand;
        public HomeStore Store;
        public Tier Tier;
        public decimal Price;
        public float Width, Depth, Height;
        public Support Support;
        /// <summary>Height of a top you can stand things on (desks, tables, nightstands), or 0.</summary>
        public float Surface;
        /// <summary>Desks: monitors it takes (2, 3, 4 or 6).</summary>
        public int MonitorSlots;
        /// <summary>Monitor arms: monitors it holds (1–6).</summary>
        public int Arms;
        /// <summary>Monitors: diagonal in inches.</summary>
        public int Inches;
        /// <summary>Sold in a box: carried home boxed, unboxed where it goes.</summary>
        public bool Boxed;
        /// <summary>Fine outdoors (plants, benches, grills); everything else is indoor only.</summary>
        public bool Outdoor;
        /// <summary>Something you sit on: never on a bed, a table or another seat.</summary>
        public bool Seat;
        /// <summary>An Office Ready Kit: a whole trading desk with this many screens, sold as one and unpacked assembled.</summary>
        public int KitScreens;
        /// <summary>Floor-standing, but fine on a desk or table top too (a tower PC).</summary>
        public bool OnTops;
        public string[] Variants = { "Default" };
        /// <summary>The moving box: free with pickup orders, not on sale; holds any amount (Variant 0 open, 1 closed).</summary>
        public bool IsBox;

        public bool IsDesk => MonitorSlots > 0;
        public bool IsMonitor => Inches > 0;
        /// <summary>A laptop: a computer with its own screen, which shows what a monitor does (never on an arm).</summary>
        public bool IsLaptop => Id == "laptop";
        /// <summary>Anything with a live screen: monitors and laptops.</summary>
        public bool HasScreen => IsMonitor || IsLaptop;
        public bool IsArm => Arms > 0;
        public bool IsKit => KitScreens > 0;

        /// <summary>
        /// What an Office Ready Kit unpacks into: the desk (the trading desk from three screens up), a monitor arm for
        /// the screens, 27" monitors, a tower, keyboard, mouse and speakers. The first entry is the desk.
        /// </summary>
        public static string[] KitParts(int screens)
        {
            var parts = new System.Collections.Generic.List<string> { screens <= 2 ? "desk_standard" : "desk_trading", "arm_" + screens };
            for (int i = 0; i < screens; i++) parts.Add("mon_27");
            parts.AddRange(new[] { "pc_tower", "keyboard", "mouse", "speakers" });
            return parts.ToArray();
        }
        /// <summary>Anything big or heavy: carried slowly, and too bulky for a car's back seat.</summary>
        public bool Large => Math.Max(Width, Depth) > 1.2f || Width * Depth * Height > 0.4f;
        /// <summary>Loading space it takes, in cubic metres.</summary>
        public float Volume => Width * Depth * Height;
    }

    /// <summary>
    /// Everything Timberline Home (furniture) and Circuit Stop (tech) sell. Budget, mid and premium tiers per
    /// department; colours by material. Brands are made up.
    /// </summary>
    public static class HomeCatalog
    {
        private static readonly string[] Fabric = { "Charcoal", "Oatmeal", "Navy", "Forest" };
        private static readonly string[] Wood = { "Oak", "Walnut", "White" };
        private static readonly string[] Metal = { "Black", "Silver" };

        public static readonly IReadOnlyList<HomeItem> Items = Build();
        private static Dictionary<string, HomeItem> _byId;

        public static HomeItem Find(string id)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<string, HomeItem>(StringComparer.Ordinal);
                foreach (HomeItem i in Items) _byId[i.Id] = i;
            }
            return id != null && _byId.TryGetValue(id, out HomeItem item) ? item : null;
        }

        private static List<HomeItem> Build()
        {
            var all = new List<HomeItem>();
            HomeItem F(string id, string name, string dept, Tier tier, decimal price, string model, float w, float d, float h,
                Support support = Support.Floor, string[] variants = null, float surface = 0f)
            {
                var i = new HomeItem
                {
                    Id = id, Name = name, Department = dept, Store = HomeStore.Furniture, Tier = tier, Price = price, Model = model,
                    Width = w, Depth = d, Height = h, Support = support, Surface = surface, Variants = variants ?? new[] { "Default" },
                };
                all.Add(i);
                return i;
            }
            HomeItem T(string id, string name, string dept, Tier tier, decimal price, string model, float w, float d, float h,
                Support support = Support.Surface, string brand = null)
            {
                var i = new HomeItem
                {
                    Id = id, Name = name, Department = dept, Store = HomeStore.Tech, Tier = tier, Price = price, Model = model,
                    Width = w, Depth = d, Height = h, Support = support, Boxed = true, Brand = brand, Variants = Metal,
                };
                all.Add(i);
                return i;
            }

            // The moving box the furniture store hands out with pickup orders (not on sale; Kenney's cardboard box).
            HomeItem box = F("moving_box", "Moving Box", "Moving", Tier.Budget, 0m, "cardboardBoxOpen", 0.7f, 0.5f, 0.5f, variants: new[] { "Open", "Closed" });
            box.IsBox = true;
            box.Outdoor = true;

            // Living.
            F("sofa_budget", "Nook 2-Seat Sofa", "Living", Tier.Budget, 349m, "loungeDesignSofa", 1.7f, 0.85f, 0.8f, variants: Fabric).Seat = true;
            F("sofa_mid", "Harbor 3-Seat Sofa", "Living", Tier.Mid, 899m, "loungeSofa", 2.1f, 0.9f, 0.85f, variants: Fabric).Seat = true;
            F("sofa_premium", "Summit Corner Sofa", "Living", Tier.Premium, 2490m, "loungeSofaCorner", 2.4f, 2.4f, 0.85f, variants: Fabric).Seat = true;
            F("armchair", "Reading Armchair", "Living", Tier.Mid, 420m, "loungeChair", 0.85f, 0.85f, 0.85f, variants: Fabric).Seat = true;
            F("coffee_table", "Slab Coffee Table", "Living", Tier.Budget, 129m, "tableCoffee", 1.1f, 0.6f, 0.42f, variants: Wood, surface: 0.42f);
            F("tv_stand", "Low TV Stand", "Living", Tier.Mid, 249m, "cabinetTelevision", 1.6f, 0.45f, 0.5f, variants: Wood, surface: 0.5f);
            F("bookcase", "Tall Bookcase", "Living", Tier.Mid, 189m, "bookcaseOpen", 0.9f, 0.35f, 1.9f, variants: Wood);
            F("rug", "Wool Rug", "Living", Tier.Mid, 210m, "rugRectangle", 2f, 1.4f, 0.02f, variants: Fabric);
            F("floor_lamp", "Arc Floor Lamp", "Living", Tier.Mid, 119m, "lampRoundFloor", 0.4f, 0.4f, 1.6f, variants: Metal);
            // Bedroom.
            F("bed_single", "Pine Single Bed", "Bedroom", Tier.Budget, 229m, "bedSingle", 1f, 2.05f, 0.55f, variants: Wood);
            F("bed_double", "Harbor Double Bed", "Bedroom", Tier.Mid, 649m, "bedDouble", 1.6f, 2.1f, 0.6f, variants: Wood);
            F("bed_king", "Summit King Bed", "Bedroom", Tier.Premium, 1790m, "bedDouble", 1.95f, 2.2f, 0.65f, variants: Wood);
            F("nightstand", "Nightstand", "Bedroom", Tier.Budget, 79m, "cabinetBedDrawer", 0.5f, 0.4f, 0.55f, variants: Wood, surface: 0.55f);
            F("dresser", "Six-Drawer Dresser", "Bedroom", Tier.Mid, 329m, "cabinetBedDrawerTable", 1.2f, 0.5f, 0.85f, variants: Wood, surface: 0.85f);
            // Office: desks by how many monitors they take.
            F("desk_compact", "Compact Desk", "Office", Tier.Budget, 139m, "desk", 1.2f, 0.6f, 0.75f, variants: Wood, surface: 0.75f).MonitorSlots = 2;
            F("desk_standard", "Work Desk", "Office", Tier.Mid, 289m, "desk", 1.6f, 0.75f, 0.75f, variants: Wood, surface: 0.75f).MonitorSlots = 3;
            F("desk_corner", "L-Shaped Desk", "Office", Tier.Mid, 499m, "deskCorner", 1.8f, 1.6f, 0.75f, variants: Wood, surface: 0.75f).MonitorSlots = 4;
            F("desk_trading", "Trading Desk", "Office", Tier.Premium, 1290m, "desk", 2.4f, 0.9f, 0.75f, variants: Wood, surface: 0.75f).MonitorSlots = 6;
            F("chair_office", "Task Chair", "Office", Tier.Budget, 99m, "chairDesk", 0.6f, 0.6f, 1f, variants: Metal).Seat = true;
            F("chair_ergo", "Ergo Mesh Chair", "Office", Tier.Premium, 690m, "chairDesk", 0.68f, 0.68f, 1.15f, variants: Metal).Seat = true;
            F("filing", "Filing Cabinet", "Office", Tier.Budget, 119m, "sideTableDrawers", 0.45f, 0.55f, 0.7f, variants: Metal, surface: 0.7f);
            // For a company floor (FUND_SPEC §7, §20): the coffee point, reception seating, a meeting table.
            F("coffee_station", "Espresso Station", "Office", Tier.Mid, 1480m, "", 1.1f, 0.6f, 1.05f, variants: Wood);
            F("water_cooler", "Water Cooler", "Office", Tier.Budget, 189m, "office_watercooler_cube_246_cube", 0.35f, 0.35f, 1.25f);
            F("waiting_bench", "Reception Bench", "Office", Tier.Mid, 540m, "bench", 1.6f, 0.55f, 0.46f, variants: Fabric).Seat = true;
            F("meeting_table", "Meeting Table", "Office", Tier.Premium, 1690m, "table", 2.6f, 1.1f, 0.75f, variants: Wood, surface: 0.75f);
            F("whiteboard", "Whiteboard", "Office", Tier.Budget, 149m, "", 1.8f, 0.04f, 1.1f, Support.Wall);
            // Dining and entry.
            F("dining_table", "Four-Seat Table", "Dining", Tier.Mid, 399m, "table", 1.4f, 0.85f, 0.75f, variants: Wood, surface: 0.75f);
            F("dining_chair", "Dining Chair", "Dining", Tier.Budget, 59m, "chair", 0.45f, 0.5f, 0.9f, variants: Wood).Seat = true;
            F("coat_rack", "Coat Rack", "Entry", Tier.Budget, 49m, "coatRackStanding", 0.45f, 0.45f, 1.8f, variants: Wood);
            F("shoe_bench", "Shoe Bench", "Entry", Tier.Budget, 89m, "bench", 1f, 0.4f, 0.45f, variants: Wood).Seat = true;
            // Decor.
            F("plant", "Potted Fiddle Leaf", "Decor", Tier.Budget, 45m, "pottedPlant", 0.45f, 0.45f, 1.1f).Outdoor = true;
            F("wall_art", "Framed Print", "Decor", Tier.Mid, 95m, "", 0.8f, 0.04f, 0.6f, Support.Wall);
            F("wall_clock", "Wall Clock", "Decor", Tier.Budget, 35m, "", 0.35f, 0.05f, 0.35f, Support.Wall);
            F("mirror", "Tall Mirror", "Decor", Tier.Mid, 149m, "", 0.6f, 0.04f, 1.4f, Support.Wall);
            F("ceiling_lamp", "Pendant Lamp", "Decor", Tier.Mid, 89m, "lampSquareCeiling", 0.4f, 0.4f, 0.5f, Support.Ceiling);
            F("desk_lamp", "Desk Lamp", "Decor", Tier.Budget, 39m, "lampSquareTable", 0.25f, 0.25f, 0.45f, Support.Surface);

            // Tech: monitors by size and tier.
            T("mon_24", "Voxel 24\" Monitor", "Monitors", Tier.Budget, 149m, "computerScreen", 0.55f, 0.18f, 0.42f, brand: "Voxel").Inches = 24;
            T("mon_27", "Lumen 27\" QHD", "Monitors", Tier.Mid, 329m, "computerScreen", 0.62f, 0.2f, 0.47f, brand: "Lumen").Inches = 27;
            T("mon_32", "Lumen 32\" 4K", "Monitors", Tier.Premium, 649m, "computerScreen", 0.72f, 0.22f, 0.52f, brand: "Lumen").Inches = 32;
            T("mon_34", "Arcwave 34\" Ultrawide", "Monitors", Tier.Premium, 799m, "computerScreen", 0.82f, 0.24f, 0.45f, brand: "Arcwave").Inches = 34;
            // Mounts: arms clamp to a desk and hold 1–6 monitors.
            for (int n = 1; n <= 6; n++)
            {
                string[] names = { "", "Single", "Dual", "Triple", "Quad", "Five-Screen", "Six-Screen" };
                HomeItem arm = T("arm_" + n, $"{names[n]} Monitor Arm", "Mounts", n <= 2 ? Tier.Budget : n <= 4 ? Tier.Mid : Tier.Premium,
                    49m + 45m * (n - 1), "", 0.2f + 0.3f * Math.Min(n, 3), 0.2f, 0.5f, Support.DeskMount, "Flexmount");
                arm.Arms = n;
            }
            // Peripherals and computers.
            T("keyboard", "Keyboard", "Peripherals", Tier.Budget, 39m, "computerKeyboard", 0.44f, 0.15f, 0.03f, brand: "Keyfort");
            T("keyboard_mech", "Mechanical Keyboard", "Peripherals", Tier.Premium, 169m, "computerKeyboard", 0.44f, 0.15f, 0.04f, brand: "Keyfort");
            T("mouse", "Mouse", "Peripherals", Tier.Budget, 25m, "computerMouse", 0.07f, 0.12f, 0.04f, brand: "Keyfort");
            T("pc_tower", "Tower PC", "Computers", Tier.Mid, 1299m, "", 0.22f, 0.45f, 0.48f, Support.Floor, "Northbyte").OnTops = true;
            T("laptop", "Laptop", "Computers", Tier.Mid, 999m, "laptop", 0.34f, 0.24f, 0.02f, brand: "Northbyte");
            T("speakers", "Desk Speakers", "Audio", Tier.Mid, 119m, "speakerSmall", 0.14f, 0.16f, 0.24f, brand: "Tonebox");
            // Office Ready Kits: a complete desk, set up, in one crate. Sized as the desk (what it stands on and needs room for).
            for (int n = 1; n <= 6; n++)
            {
                bool big = n > 2;
                HomeItem kit = T("kit_" + n, $"Office Ready Kit · {n} Screen{(n == 1 ? "" : "s")}", "Office Kits", Tier.Premium, 50000m, "",
                    big ? 2.4f : 1.6f, big ? 0.9f : 0.75f, 0.75f, Support.Floor, "Circuit Stop");
                kit.KitScreens = n;
                kit.Variants = new[] { "Default" };
            }
            return all;
        }
    }
}
