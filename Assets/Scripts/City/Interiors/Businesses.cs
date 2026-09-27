using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    public enum Trade
    {
        Laundromat, Pawn, Liquor, Barber, Thrift, Diner, Pharmacy, Hardware, Tattoo, Bakery, AutoParts, SmokeShop,
        Bank, Restaurant, Books, Repair, UsedFurniture, Bar, Discount, Florist, Gym, Vacant, Arcade, Nightclub,
        FastFood, Pizza, MotelOffice, Supermarket, CarWash, Moto, FishMarket, Police, Jeweler,
    }

    /// <summary>One storefront: where it stands (front centre on the sidewalk edge, facing out of <see cref="Inward"/>), its size and trade.</summary>
    public sealed class Business
    {
        public string Name;
        public Trade Trade;
        public Vector2 Front;
        public Vector2 Inward;
        public float Width, Depth;
        /// <summary>Storeys of flats or offices above the shop.</summary>
        public int Floors;
        public Color Brand;
        public FacadeStyle Style;
        public string Tagline;

        public float Yaw => Mathf.Atan2(Inward.x, Inward.y) * Mathf.Rad2Deg;
        public Hours Hours => Businesses.HoursFor(Trade);
    }

    /// <summary>
    /// Builds a <see cref="Business"/> (TOWN_SPEC A5): a shop shell with real windows and an opening-hours door, a
    /// sign band (neon for bars and clubs), flats above, and an interior themed by trade: counter and register,
    /// shelves or machines or booths, a member of staff on shift, something to buy, and a light that goes out
    /// when it closes. Laid out in a local frame (x along the frontage, +z into the building) so a shop can face
    /// any street.
    /// </summary>
    public static class Businesses
    {
        public const float GroundTop = 4.2f, Wall = 0.25f, Storey = 3.2f;

        public static Hours HoursFor(Trade t) => t switch
        {
            Trade.Laundromat => Hours.Of(6, 23),
            Trade.Pawn => Hours.Of(10, 20),
            Trade.Liquor => Hours.Of(10, 23),
            Trade.Barber => Hours.Of(9, 18),
            Trade.Diner => Hours.Of(6, 22),
            Trade.Pharmacy => Hours.Of(8, 21),
            Trade.Hardware => Hours.Of(8, 19),
            Trade.Tattoo => Hours.Of(12, 22),
            Trade.Bakery => Hours.Of(6, 15),
            Trade.AutoParts => Hours.Of(8, 19),
            Trade.SmokeShop => Hours.Of(9, 22),
            Trade.Bank => Hours.Of(9, 17),
            Trade.Police => Hours.Of(0, 24),
            Trade.Restaurant => Hours.Of(11, 22),
            Trade.Bar => Hours.Of(16, 2),
            Trade.Nightclub => Hours.Of(21, 3),
            Trade.Arcade => Hours.Of(12, 24),
            Trade.FastFood => Hours.Of(6, 24),
            Trade.Pizza => Hours.Of(11, 24),
            Trade.MotelOffice => Hours.Of(0, 24),
            Trade.Supermarket => Hours.Of(6, 23),
            Trade.Gym => Hours.Of(5, 23),
            Trade.FishMarket => Hours.Of(6, 16),
            Trade.Discount => Hours.Of(8, 21),
            _ => Hours.Of(10, 19),
        };

        private static readonly Dictionary<Trade, (string Look, string Greeting, string[] Talk, (string Item, decimal Price, string Thanks)[] Sells)> Staff =
            new Dictionary<Trade, (string, string, string[], (string, decimal, string)[])>
        {
            [Trade.Laundromat] = ("Casual", "Change machine's busted, I've got quarters.", new[] { "Dryer six eats socks. Everybody knows.", "Busiest on Sundays." }, new[] { ("Wash & dry", 4.50m, "Machine four's free.") }),
            [Trade.Pawn] = ("Worker", "Buying or selling?", new[] { "Everything's got a story. Most of them aren't true.", "Guitars on the wall, jewellery in the case." }, new[] { ("Old wristwatch", 45m, "Still keeps time. Mostly."), ("Used guitar", 120m, "Needs new strings.") }),
            [Trade.Liquor] = ("Casual", "ID if you look under thirty.", new[] { "Cooler's in the back.", "We close at eleven, no exceptions." }, new[] { ("Six-pack", 12m, "Enjoy."), ("Bottle of red", 16m, "Good choice.") }),
            [Trade.Barber] = ("Worker", "Take a seat, I'll be right with you.", new[] { "Short back and sides? That's most of it.", "Walk-ins till five." }, new[] { ("Haircut", 22m, "Looking sharp.") }),
            [Trade.Thrift] = ("Casual2", "Everything on the red rack is half off.", new[] { "Donations round the back.", "We got a whole estate in last week." }, new[] { ("Old jacket", 14m, "Somebody loved that coat."), ("Paperback", 2m, "Enjoy the read.") }),
            [Trade.Diner] = ("Chef", "Sit anywhere, hon.", new[] { "Pie's fresh. Well. Fresh enough.", "Breakfast all day." }, new[] { ("Coffee", 2.50m, "Refills are free."), ("Burger & fries", 11.50m, "Order up!") }),
            [Trade.Pharmacy] = ("Formal", "Pickup or prescription drop-off?", new[] { "The pharmacist's in till nine.", "Cold medicine's aisle two." }, new[] { ("Painkillers", 8m, "Take with food."), ("Vitamins", 12m, "Can't hurt.") }),
            [Trade.Hardware] = ("Worker", "Need a hand finding something?", new[] { "Keys cut while you wait.", "Paint's mixed to order." }, new[] { ("Toolbox", 35m, "That'll last you."), ("Duct tape", 5m, "Fixes everything.") }),
            [Trade.Tattoo] = ("Punk", "Got an idea, or want to look at flash?", new[] { "Walk-ins after four.", "Aftercare sheet's on the counter." }, new[] { ("Small tattoo", 80m, "Keep it out of the sun.") }),
            [Trade.Bakery] = ("Chef", "Morning! It's all fresh.", new[] { "Sourdough sells out by ten.", "Day-old's half price." }, new[] { ("Loaf of bread", 5m, "Still warm."), ("Donut", 2m, "Enjoy.") }),
            [Trade.AutoParts] = ("Worker", "What are you driving?", new[] { "We can test your battery for free.", "Wiper blades are on sale." }, new[] { ("Motor oil", 12m, "Five quarts should do it."), ("Car battery", 140m, "Bring the old one back for the core.") }),
            [Trade.SmokeShop] = ("Punk", "What can I get you?", new[] { "No sampling in the store.", "Lighters are by the register." }, new[] { ("Lighter", 3m, "There you go.") }),
            [Trade.Police] = ("Formal", "Kell Valley Police. What's the problem?", new[] { "For an emergency, call 911.", "Lost property is Tuesdays and Thursdays.", "Parking tickets get paid at City Hall." }, new (string, decimal, string)[0]),
            [Trade.Bank] = ("Suit", "Welcome to Kell Valley. How can I help?", new[] { "The ATM outside is open all night.", "Rates are on the board." }, new (string, decimal, string)[0]),
            [Trade.Restaurant] = ("Chef", "Table for one?", new[] { "The special's on the board.", "Kitchen closes at ten." }, new[] { ("Dinner", 16m, "Enjoy your meal."), ("Soup", 7m, "Careful, it's hot.") }),
            [Trade.Books] = ("Formal", "Browse as long as you like.", new[] { "Used books half the cover price.", "The shop cat's asleep in history." }, new[] { ("Used book", 6m, "Great pick.") }),
            [Trade.Repair] = ("Worker", "Phone, laptop, whatever: I fix it.", new[] { "Screens take a day.", "No data recovery on water damage, sorry." }, new[] { ("Phone charger", 15m, "That'll do.") }),
            [Trade.UsedFurniture] = ("Worker", "All of it's solid wood. Mostly.", new[] { "We deliver for twenty bucks.", "The recliner's older than me." }, new[] { ("Lamp", 25m, "Bulb included.") }),
            [Trade.Bar] = ("Casual", "What'll it be?", new[] { "Happy hour's four to six.", "Pool table's a quarter." }, new[] { ("Beer", 6m, "Cheers."), ("Whiskey", 9m, "Neat.") }),
            [Trade.Discount] = ("Casual", "Everything's under five bucks. Almost.", new[] { "New stock Tuesdays.", "Cleaning stuff's in the back." }, new[] { ("Snacks", 3m, "Bargain.") }),
            [Trade.Florist] = ("Casual2", "Something for someone?", new[] { "Roses are marked up. Try the tulips.", "We do deliveries." }, new[] { ("Bouquet", 24m, "They'll love it.") }),
            [Trade.Gym] = ("Worker", "Day pass or membership?", new[] { "Wipe the benches down after.", "Squat rack's by the mirrors." }, new[] { ("Day pass", 12m, "Have a good one.") }),
            [Trade.Arcade] = ("Punk", "Tokens at the counter.", new[] { "High score on the racer's been up for months.", "Claw machine's rigged. Kidding." }, new[] { ("Tokens", 5m, "Go get 'em.") }),
            [Trade.Nightclub] = ("Punk", "Cover's ten.", new[] { "DJ goes on at eleven.", "No hats, no hoods." }, new[] { ("Cover charge", 10m, "Have fun."), ("Drink", 11m, "Enjoy.") }),
            [Trade.FastFood] = ("Casual", "Welcome to Kell Burger, what can I get you?", new[] { "Combo comes with a drink.", "Drive-thru's open all night." }, new[] { ("Burger combo", 9m, "Number forty-two!") }),
            [Trade.Pizza] = ("Casual", "Slice or a whole pie?", new[] { "Pepperoni's right out of the oven.", "We deliver till midnight." }, new[] { ("Pizza slice", 4m, "Hot, careful."), ("Whole pizza", 18m, "Enjoy.") }),
            [Trade.MotelOffice] = ("Casual", "Looking for a room?", new[] { "Ice machine's by room nine.", "Checkout's at eleven." }, new[] { ("Vending snack", 2m, "Enjoy.") }),
            [Trade.Supermarket] = ("Casual", "Hi, find everything okay?", new[] { "Produce is fresh on Mondays.", "The deli closes at eight." }, new[] { ("Groceries", 38m, "Paper or plastic?"), ("Sandwich", 6.5m, "Enjoy.") }),
            [Trade.CarWash] = ("Worker", "Basic or deluxe?", new[] { "Pull in when the light's green.", "Deluxe includes the wax." }, new[] { ("Car wash", 12m, "Pull on through.") }),
            [Trade.Moto] = ("Punk", "Looking at bikes?", new[] { "Helmets are on the back wall.", "New models in the spring." }, new[] { ("Riding gloves", 40m, "Stay safe out there.") }),
            [Trade.Jeweler] = ("Suit", "Welcome to Lustre. Anything catching your eye?", new[] { "Everything in the cases is real, I promise.", "Try it on, the mirror's by the window.", "Watches on the left, diamonds on the right." }, new (string, decimal, string)[0]),
            [Trade.FishMarket] = ("Worker", "Came in on the boats this morning.", new[] { "Salmon's running. Get it while it's cheap.", "We close when it's gone." }, new[] { ("Fresh salmon", 14m, "Keep it cold."), ("Crab", 18m, "Watch the claws.") }),
        };

        public static void Build(CityContext c, Business b, int seed)
        {
            Kit k = c.Kit;
            float y = StreetMap.Plan.StreetGrade(b.Front);
            Transform root = Kit.Group(c.Static, b.Name, new Vector3(b.Front.x, y, b.Front.y), b.Yaw);
            Transform dyn = Kit.Group(c.Dynamic, b.Name, new Vector3(b.Front.x, y, b.Front.y), b.Yaw);
            float w = b.Width, d = b.Depth, hw = w / 2f;
            var f = new Rect(-hw, 0f, w, d);
            var rng = new System.Random(seed);
            float top = GroundTop;
            Material outside = WallMaterial(c, b, rng);
            Material roof = c.P.Lit(new Color(0.24f, 0.24f, 0.25f));
            Material brand = c.P.Lit(b.Brand, 0.3f);

            if (b.Trade == Trade.Vacant)
            {
                // Empty storefront: papered windows, a leasing sign. Not enterable.
                ModularFacade.Build(c, root, b.Name, new Vector3(-hw, 0f, 0f), new Vector3(hw, Mathf.Max(top + b.Floors * Storey, ModularFacade.ShopStorey + 2.5f), d), b.Style, seed, storefront: true);
                k.Span(root, "Papered window", new Vector3(-hw + 0.8f, 0.6f, -0.03f), new Vector3(hw - 0.8f, 3f, 0f), c.P.Lit(new Color(0.72f, 0.66f, 0.52f)), collider: false);
                k.Text(root, b.Tagline ?? "FOR LEASE", new Vector3(0f, 2f, -0.05f), 0f, 0.3f, new Color(0.7f, 0.12f, 0.1f));
                return;
            }

            // Door slightly off-centre, windows either side.
            float doorX = hw > 6f ? -hw * 0.35f : 0f;
            var windows = new List<(float, float)>();
            float leftW = doorX - 0.8f - (-hw + 0.8f), rightW = hw - 0.8f - (doorX + 0.8f);
            if (leftW > 1.5f) windows.Add(((-hw + 0.8f + doorX - 0.8f) / 2f, leftW - 0.4f));
            if (rightW > 1.5f) windows.Add(((doorX + 0.8f + hw - 0.8f) / 2f, rightW - 0.4f));
            Material inside = c.P.Surface(Finish.Plaster, InsideColor(b.Trade), 0.05f);
            Material floor = c.P.Surface(FloorFinish(b.Trade), FloorColor(b.Trade), 0.3f);
            Shops.ShopShell(c, root, f, top, Wall, outside, inside, floor, doorX, 1.2f, windows.ToArray());
            if (b.Floors > 0) ModularFacade.Build(c, root, "Flats above", new Vector3(-hw, top, 0f), new Vector3(hw, top + b.Floors * Storey, d), b.Style, seed);
            else k.Span(root, "Roof", new Vector3(-hw, top, 0f), new Vector3(hw, top + 0.3f, d), roof);

            // Sign band and name; neon for the night trades.
            bool neon = b.Trade == Trade.Bar || b.Trade == Trade.Nightclub || b.Trade == Trade.Arcade || b.Trade == Trade.Tattoo || b.Trade == Trade.Pizza;
            k.Span(root, "Sign band", new Vector3(-hw + 0.3f, 3.35f, -0.14f), new Vector3(hw - 0.3f, 4.1f, 0f), neon ? c.P.Lit(new Color(0.08f, 0.08f, 0.1f)) : brand, collider: false);
            float nameSize = Mathf.Min(0.36f, (w - 1f) / (b.Name.Length * 0.62f));
            Color nameColor = neon ? Color.Lerp(b.Brand, Color.white, 0.35f) : (b.Brand.grayscale > 0.55f ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.97f, 0.95f, 0.9f));
            TextMesh name = k.Text(root, b.Name.ToUpperInvariant(), new Vector3(0f, 3.72f, -0.16f), 0f, nameSize, nameColor);
            if (neon) name.GetComponent<MeshRenderer>().sharedMaterial = c.P.Sign(Color.Lerp(b.Brand, Color.white, 0.2f));
            Hours hours = b.Hours;
            k.Text(root, (b.Tagline != null ? b.Tagline + "   " : "") + hours.Describe(), new Vector3(doorX + 1.6f, 2.5f, -0.03f), 0f, 0.07f, new Color(0.95f, 0.93f, 0.88f));
            if (rng.NextDouble() < 0.5 && !neon)
                k.Box(root, "Awning", new Vector3(0f, 3.2f, -0.7f), new Vector3(w - 0.6f, 0.08f, 1.4f), c.P.Lit(Color.Lerp(b.Brand, Color.black, 0.2f)), collider: false);

            Door door = c.SwingDoor(dyn, "door", new Vector3(doorX - 0.6f, 0f, Wall / 2f), 1.2f, 2.25f, c.P.Glass(new Color(0.6f, 0.7f, 0.75f, 0.35f)), glass: true);
            door.LockReason = () => hours.Contains(c.Game.Clock.Now) ? null : $"closed (opens {Hours.Clock(hours.Open)})";

            // Inside (same frame as the shell; its own group so it can be culled at range).
            Transform inner = Kit.Group(root, "Interior");
            Interior(c, inner, b, rng);
            CityLayers.Set(inner, CityLayers.Interior);
            Light lamp = c.PointLight(root, new Vector3(0f, top - 0.8f, d * 0.45f), Mathf.Max(8f, w * 0.8f), 1.1f, new Color(1f, 0.93f, 0.82f));
            // OPEN / CLOSED sign in the door glass; it says BACK SOON while the only member of staff is on a break.
            Transform doorSign = Kit.Group(dyn, "Door sign", new Vector3(doorX + 1.3f, 1.55f, Wall / 2f - 0.08f)); // in the window by the door
            Renderer signPanel = k.Box(doorSign, "Panel", Vector3.zero, new Vector3(0.42f, 0.16f, 0.02f), c.P.Unlit(new Color(0.1f, 0.1f, 0.1f)), collider: false).GetComponent<Renderer>();
            TextMesh signText = k.Text(doorSign, "CLOSED", new Vector3(0f, 0f, -0.015f), 0f, 0.06f, Color.white);

            // Staff behind the counter; they come and go by the back corner. Long days get a lunch break.
            var (look, greeting, talk, sells) = Staff[b.Trade];
            var station = new Vector3(hw > 4f ? hw * 0.4f : 0f, 0f, d - 1.7f);
            var route = new List<Vector3> { station, new Vector3(hw - 0.9f, 0f, d - 1.7f), new Vector3(hw - 0.9f, 0f, d - 0.6f) };
            var lines = new Queue<string>(talk);
            int openHours = ((hours.Close - hours.Open) + 24 * 60) % (24 * 60);
            var schedule = new WorkSchedule { Shift = hours };
            if (openHours >= 10 * 60 && openHours < 20 * 60 && seed % 3 == 0)
            {
                schedule.Break = Hours.Of(13 + (seed % 2) * 0.5, 13.5 + (seed % 2) * 0.5);
                schedule.HasBreak = true;
            }
            StaffNpc staff = StaffNpc.Create(k, dyn, StaffTitle(b.Trade), seed, b.Brand, schedule, route, 180f,
                new[] { NpcPose.Stand, NpcPose.Phone, NpcPose.Typing }, () => greeting,
                () => { string l = lines.Dequeue(); lines.Enqueue(l); return l; },
                c.Game, c.Hud, c.Player, look: look);
            for (int i = 0; i < sells.Length; i++)
                Counter(c, root, new Vector3(station.x - 0.6f + i * 1.2f, 1.02f, d - 2.45f), sells[i].Item, sells[i].Price, staff, sells[i].Thanks);
            root.gameObject.AddComponent<OpenLights>().Configure(c, hours, lamp, staff, signPanel, signText, neon ? name : null,
                c.P.Glow(new Color(0.2f, 0.9f, 0.35f), 1.4f), c.P.Glow(new Color(1f, 0.7f, 0.15f), 1.2f), c.P.Unlit(new Color(0.1f, 0.1f, 0.1f)),
                c.P.Sign(new Color(0.16f, 0.16f, 0.18f)));

            Vector3 outdoor = root.TransformPoint(new Vector3(doorX, 0f, -0.8f));
            c.Place(outdoor, PlaceKind.Door, b.Name);
            c.PlaceInfo[b.Name] = (CategoryOf(b.Trade), hours);
            if (CategoryOf(b.Trade) == PlaceCategory.Night || b.Trade == Trade.Diner || b.Trade == Trade.Pizza)
            {
                // A spot out front where people stand around (smoking, talking, waiting for a ride).
                string standTag = "outside " + b.Name;
                c.Place(root.TransformPoint(new Vector3(doorX + 2.6f, 0f, -1.3f)), PlaceKind.Stand, standTag);
                c.PlaceInfo[standTag] = (PlaceCategory.Stand, hours);
            }
            c.Anchor("biz_" + b.Name.ToLowerInvariant().Replace(' ', '_').Replace("'", ""), root.TransformPoint(new Vector3(doorX, 0f, -2f)));
        }

        public static PlaceCategory CategoryOf(Trade t) => t switch
        {
            Trade.Diner or Trade.Bakery or Trade.Restaurant or Trade.FastFood or Trade.Pizza => PlaceCategory.Food,
            Trade.Bar or Trade.Nightclub or Trade.Arcade => PlaceCategory.Night,
            Trade.MotelOffice => PlaceCategory.Home,
            Trade.Gym => PlaceCategory.Leisure,
            Trade.Police => PlaceCategory.Work,
            _ => PlaceCategory.Shop,
        };

        private static string StaffTitle(Trade t) => t switch
        {
            Trade.Bar => "Bartender",
            Trade.Barber => "Barber",
            Trade.Diner or Trade.Restaurant => "Server",
            Trade.Pharmacy => "Pharmacist",
            Trade.Tattoo => "Tattoo artist",
            Trade.Bank => "Teller",
            Trade.Police => "Desk officer",
            Trade.Nightclub => "Bouncer",
            Trade.Jeweler => "Jeweller",
            _ => "Clerk",
        };

        private static Material WallMaterial(CityContext c, Business b, System.Random rng)
        {
            Color[] walls = { new Color(0.55f, 0.3f, 0.24f), new Color(0.78f, 0.74f, 0.66f), new Color(0.4f, 0.42f, 0.44f), new Color(0.62f, 0.5f, 0.38f),
                new Color(0.3f, 0.36f, 0.34f), new Color(0.7f, 0.66f, 0.56f) };
            // Red and tan ones are brick; the pale and dark ones rendered or painted, the grey one bare concrete.
            Finish[] finishes = { Finish.Brick, Finish.PaintedPlaster, Finish.Concrete, Finish.Brick, Finish.PaintedPlaster, Finish.Plaster };
            int i = rng.Next(walls.Length);
            return c.P.Surface(finishes[i], walls[i], 0.05f);
        }

        private static Color InsideColor(Trade t) => t switch
        {
            Trade.Bar or Trade.Nightclub or Trade.Arcade => new Color(0.18f, 0.16f, 0.18f),
            Trade.Diner => new Color(0.86f, 0.82f, 0.72f),
            Trade.Tattoo or Trade.SmokeShop => new Color(0.25f, 0.23f, 0.26f),
            Trade.Jeweler => new Color(0.17f, 0.2f, 0.28f),
            _ => new Color(0.88f, 0.87f, 0.83f),
        };

        private static Finish FloorFinish(Trade t) => t switch
        {
            Trade.Diner or Trade.Laundromat or Trade.Pharmacy or Trade.Jeweler => Finish.Tiles,
            Trade.Gym => Finish.Concrete,
            _ => Finish.WoodFloor,
        };

        private static Color FloorColor(Trade t) => t switch
        {
            Trade.Diner => new Color(0.75f, 0.72f, 0.7f),
            Trade.Bar or Trade.Nightclub => new Color(0.25f, 0.18f, 0.12f),
            Trade.Gym => new Color(0.15f, 0.15f, 0.16f),
            Trade.Laundromat or Trade.Pharmacy => new Color(0.8f, 0.8f, 0.78f),
            Trade.Jeweler => new Color(0.9f, 0.88f, 0.84f),
            _ => new Color(0.5f, 0.42f, 0.32f),
        };

        private static void Counter(CityContext c, Transform root, Vector3 at, string item, decimal price, StaffNpc staff, string thanks)
        {
            GameObject display = c.Kit.Box(root, item, at + new Vector3(0f, 0.06f, 0f), new Vector3(0.28f, 0.12f, 0.2f), c.P.Lit(new Color(0.8f, 0.62f, 0.35f)), collider: false);
            var aim = new GameObject(item + " (buy)");
            aim.transform.SetParent(c.Dynamic, false);
            aim.transform.position = display.transform.position + Vector3.up * 0.1f;
            var box = aim.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.7f, 0.45f, 0.7f);
            aim.AddComponent<ShopCounter>().Configure(c.Game, c.Hud, staff, item, price, thanks);
        }

        // ---- interiors by trade (local frame: x across, +z to the back wall) ----

        private static void Interior(CityContext c, Transform r, Business b, System.Random rng)
        {
            Kit k = c.Kit;
            float hw = b.Width / 2f - Wall, d = b.Depth - Wall;
            Material counter = c.P.Lit(new Color(0.3f, 0.26f, 0.22f), 0.3f);
            Material top = c.P.Lit(new Color(0.15f, 0.15f, 0.16f), 0.5f);
            Material metal = c.P.Lit(new Color(0.6f, 0.62f, 0.64f), 0.5f);
            Material shelf = c.P.Lit(new Color(0.55f, 0.57f, 0.6f), 0.4f);
            // Every shop has a counter across the back with a register.
            float cz = d - 2.3f;
            k.Span(r, "Counter", new Vector3(-hw + 1.2f, 0f, cz - 0.35f), new Vector3(hw - 1.6f, 1f, cz + 0.35f), counter);
            k.Span(r, "Counter top", new Vector3(-hw + 1.1f, 1f, cz - 0.4f), new Vector3(hw - 1.5f, 1.04f, cz + 0.4f), top, collider: false);
            k.Box(r, "Register", new Vector3(hw * 0.4f + 0.5f, 1.17f, cz), new Vector3(0.4f, 0.25f, 0.35f), c.P.Lit(new Color(0.12f, 0.12f, 0.13f), 0.4f), collider: false);
            float front = 2.2f, back = cz - 1.3f; // floor space between the windows and the counter

            switch (b.Trade)
            {
                case Trade.Laundromat:
                    for (float z = front; z < back; z += 0.9f)
                    {
                        k.Fit(r, "washer", new Vector3(-hw + 0.45f, 0f, z), new Vector3(0.85f, 0f, 0.85f), 90f);
                        k.Fit(r, "dryer", new Vector3(hw - 0.45f, 0f, z), new Vector3(0.85f, 0f, 0.85f), 270f);
                    }
                    k.Span(r, "Folding table", new Vector3(-1.2f, 0f, (front + back) / 2f - 1f), new Vector3(1.2f, 0.9f, (front + back) / 2f + 1f), metal);
                    k.Fit(r, "bench", new Vector3(0f, 0f, front - 0.8f), new Vector3(2f, 0f, 0f));
                    break;
                case Trade.Pawn:
                case Trade.SmokeShop:
                    // Glass cases in a U, guitars and TVs on the walls.
                    for (float z = front; z < back; z += 2.2f)
                    {
                        GlassCase(c, r, new Vector3(-hw + 1.4f, 0f, z + 1f), 2f, 0.8f);
                        GlassCase(c, r, new Vector3(hw - 1.4f, 0f, z + 1f), 2f, 0.8f);
                    }
                    for (float x = -hw + 1f; x < hw - 1f; x += 1.4f)
                    {
                        k.Box(r, "Guitar", new Vector3(x, 2.2f, d - 0.15f), new Vector3(0.35f, 1f, 0.08f), c.P.Lit(new Color(0.55f + (float)rng.NextDouble() * 0.3f, 0.3f, 0.15f), 0.5f), collider: false);
                    }
                    k.Fit(r, "televisionVintage", new Vector3(-hw + 0.6f, 1.2f, back), new Vector3(0.8f, 0f, 0f), 90f);
                    k.Box(r, "Safe", new Vector3(hw - 0.6f, 0.5f, d - 0.6f), new Vector3(0.8f, 1f, 0.8f), c.P.Lit(new Color(0.2f, 0.22f, 0.2f), 0.4f));
                    break;
                case Trade.Liquor:
                case Trade.Discount:
                case Trade.Pharmacy:
                case Trade.AutoParts:
                case Trade.Hardware:
                case Trade.Supermarket:
                case Trade.FishMarket:
                    Aisles(c, r, b.Trade, hw, front, back, rng);
                    if (b.Trade == Trade.Discount) Convenience(c, r, hw, d, front, back);
                    if (b.Trade == Trade.Hardware || b.Trade == Trade.AutoParts)
                    {
                        // Stock room behind the till: a steel rack against the back wall and a pile of boxes.
                        k.Solid(k.Fit(r, "gas_shelf_metal", new Vector3(-hw * 0.3f, 0f, d - 0.45f), new Vector3(2.4f, 0f, 0.7f), 180f));
                        k.Solid(k.Fit(r, "gas_boxs_3", new Vector3(-hw + 1.1f, 0f, d - 0.6f), new Vector3(1.4f, 0f, 0.8f)));
                    }
                    break;
                case Trade.Barber:
                    for (float x = -hw + 1.2f; x < hw - 1f; x += 2f)
                    {
                        k.Fit(r, "chairDesk", new Vector3(x, 0f, back - 0.4f), new Vector3(0.7f, 0f, 0f), 180f);
                        k.Fit(r, "bathroomMirror", new Vector3(x, 1.2f, cz - 0.45f), new Vector3(0.8f, 0f, 0f), 180f);
                    }
                    for (float x = -hw + 0.8f; x < 0f; x += 0.8f) k.Fit(r, "chair", new Vector3(x, 0f, front - 0.6f), new Vector3(0.6f, 0f, 0f));
                    break;
                case Trade.Thrift:
                case Trade.Books:
                case Trade.UsedFurniture:
                case Trade.Florist:
                    for (float z = front; z < back; z += 1.6f)
                    {
                        k.Fit(r, "bookcaseOpen", new Vector3(-hw + 0.3f, 0f, z), new Vector3(0f, 2f, 1.2f), 90f);
                        k.Fit(r, "bookcaseOpen", new Vector3(hw - 0.3f, 0f, z), new Vector3(0f, 2f, 1.2f), 270f);
                    }
                    string[] middle = b.Trade == Trade.UsedFurniture ? new[] { "loungeChair", "loungeSofa", "tableCoffee", "sideTableDrawers", "lampRoundFloor" }
                        : b.Trade == Trade.Florist ? new[] { "pottedPlant", "plantSmall1", "plantSmall2", "plantSmall3" }
                        : b.Trade == Trade.Books ? new[] { "bookcaseClosedWide", "table", "books" }
                        : new[] { "coatRackStanding", "table", "chairCushion", "lampSquareFloor" };
                    for (float z = front + 0.8f; z < back - 0.5f; z += 2f)
                    for (float x = -hw + 2.4f; x < hw - 2.4f; x += 2.2f)
                        k.Fit(r, middle[rng.Next(middle.Length)], new Vector3(x, 0f, z), new Vector3(1.4f, 0f, 0f), rng.Next(4) * 90f);
                    break;
                case Trade.Diner:
                case Trade.Restaurant:
                case Trade.FastFood:
                case Trade.Pizza:
                case Trade.Bakery:
                    // Stools along the counter, tables or booths, the kitchen behind.
                    for (float x = -hw + 1.6f; x < hw - 2f; x += 1f) k.Fit(r, "stoolBar", new Vector3(x, 0f, cz - 0.9f), new Vector3(0.45f, 0f, 0f));
                    for (float z = front; z < back - 1.5f; z += 3.2f)
                    for (float x = -hw + 1.6f; x < hw - 1.2f; x += 3.6f)
                    {
                        k.Fit(r, "table", new Vector3(x, 0f, z + 0.9f), new Vector3(1.1f, 0f, 0f));
                        k.Fit(r, "chair", new Vector3(x, 0f, z + 0.15f), new Vector3(0.5f, 0f, 0f));
                        k.Fit(r, "chair", new Vector3(x, 0f, z + 1.65f), new Vector3(0.5f, 0f, 0f), 180f);
                    }
                    k.Fit(r, "kitchenStove", new Vector3(-hw + 1f, 0f, d - 0.5f), new Vector3(0.9f, 0f, 0f), 180f);
                    k.Fit(r, "kitchenFridgeLarge", new Vector3(-hw + 2.2f, 0f, d - 0.5f), new Vector3(0.9f, 0f, 0f), 180f);
                    k.Fit(r, "kitchenCoffeeMachine", new Vector3(0f, 1.04f, cz), new Vector3(0.35f, 0f, 0f), 180f);
                    string food = b.Trade == Trade.Pizza ? "Pizza" : b.Trade == Trade.Bakery ? "Bread" : "Cheeseburger";
                    for (int i = 0; i < 3; i++) k.Prop(r, food, new Vector3(-hw + 2f + i * 0.5f, 1.04f, cz), 0.1f, i * 40f, 0.3f);
                    break;
                case Trade.Tattoo:
                    k.Fit(r, "loungeChairRelax", new Vector3(-hw + 1.5f, 0f, back - 0.5f), new Vector3(1f, 0f, 0f), 90f);
                    k.Fit(r, "loungeChairRelax", new Vector3(hw - 1.5f, 0f, back - 0.5f), new Vector3(1f, 0f, 0f), 270f);
                    for (float z = front; z < back; z += 0.9f)
                    for (float yy = 1.2f; yy < 2.8f; yy += 0.8f)
                        k.Box(r, "Flash", new Vector3(-hw + 0.03f, yy, z), new Vector3(0.02f, 0.6f, 0.6f), c.P.Lit(Color.HSVToRGB((float)rng.NextDouble(), 0.5f, 0.8f)), collider: false);
                    k.Fit(r, "loungeSofa", new Vector3(0f, 0f, front - 0.3f), new Vector3(1.8f, 0f, 0f));
                    break;
                case Trade.Police:
                    // Front desk behind glass, a bench to wait on, the notice board and the flag.
                    k.Span(r, "Desk glass", new Vector3(-hw + 1.2f, 1.04f, cz - 0.05f), new Vector3(hw - 1.6f, 2.3f, cz + 0.05f), c.P.Glass(new Color(0.7f, 0.8f, 0.85f, 0.3f)), collider: true);
                    k.Fit(r, "bench", new Vector3(-hw + 2f, 0f, front), new Vector3(2f, 0f, 0f));
                    k.Box(r, "Notice board", new Vector3(hw - 0.08f, 1.6f, front + 1.5f), new Vector3(0.05f, 1f, 1.6f), c.P.Lit(new Color(0.55f, 0.42f, 0.28f)), collider: false);
                    k.Box(r, "Wanted posters", new Vector3(hw - 0.11f, 1.6f, front + 1.5f), new Vector3(0.01f, 0.7f, 1.3f), c.P.Lit(new Color(0.92f, 0.9f, 0.82f)), collider: false);
                    k.Cylinder(r, "Flag pole", new Vector3(hw - 0.5f, 1.2f, d - 0.6f), 0.04f, 2.4f, c.P.Lit(new Color(0.75f, 0.72f, 0.6f), 0.6f));
                    k.Box(r, "Flag", new Vector3(hw - 0.9f, 2.1f, d - 0.6f), new Vector3(0.8f, 0.5f, 0.02f), c.P.Lit(new Color(0.15f, 0.3f, 0.55f)), collider: false);
                    break;
                case Trade.Bank:
                    k.Span(r, "Teller glass", new Vector3(-hw + 1.2f, 1.04f, cz - 0.05f), new Vector3(hw - 1.6f, 2.3f, cz + 0.05f), c.P.Glass(new Color(0.7f, 0.8f, 0.85f, 0.3f)), collider: true);
                    k.Fit(r, "bench", new Vector3(0f, 0f, front), new Vector3(2f, 0f, 0f));
                    k.Fit(r, "pottedPlant", new Vector3(-hw + 0.6f, 0f, front), new Vector3(0.6f, 0f, 0f));
                    k.Box(r, "Vault door", new Vector3(-hw + 1.5f, 1.2f, d - 0.12f), new Vector3(1.6f, 2f, 0.1f), c.P.Lit(new Color(0.55f, 0.56f, 0.58f), 0.7f), collider: false);
                    break;
                case Trade.Repair:
                    for (float x = -hw + 1f; x < hw - 1f; x += 1.5f)
                        k.Fit(r, "computerScreen", new Vector3(x, 1.04f, cz), new Vector3(0.5f, 0f, 0f), 180f);
                    for (float z = front; z < back; z += 1.6f) k.Fit(r, "bookcaseClosed", new Vector3(-hw + 0.3f, 0f, z), new Vector3(0f, 1.8f, 1.2f), 90f);
                    break;
                case Trade.Bar:
                case Trade.Nightclub:
                    for (float x = -hw + 1.6f; x < hw - 2f; x += 0.9f) k.Fit(r, "stoolBar", new Vector3(x, 0f, cz - 0.9f), new Vector3(0.45f, 0f, 0f));
                    for (float x = -hw + 1f; x < hw - 1f; x += 0.25f)
                        k.Prop(r, rng.Next(2) == 0 ? "Bottle1" : "Bottle2", new Vector3(x, 1.4f, d - 0.3f), 0.3f);
                    k.Span(r, "Back shelf", new Vector3(-hw + 0.8f, 1.35f, d - 0.5f), new Vector3(hw - 0.8f, 1.4f, d - 0.1f), counter, collider: false);
                    if (b.Trade == Trade.Bar)
                    {
                        k.Span(r, "Pool table", new Vector3(-1.2f, 0f, front + 0.5f), new Vector3(1.2f, 0.8f, front + 2.4f), c.P.Lit(new Color(0.1f, 0.4f, 0.2f), 0.2f));
                        for (float z = front; z < back; z += 2.2f)
                        {
                            k.Fit(r, "benchCushion", new Vector3(-hw + 0.6f, 0f, z), new Vector3(0f, 0f, 1.6f), 90f);
                            k.Fit(r, "table", new Vector3(-hw + 1.5f, 0f, z), new Vector3(0.9f, 0f, 0f));
                        }
                    }
                    Material beer = c.P.Sign(new Color(1f, 0.35f, 0.2f));
                    k.Box(r, "Neon beer sign", new Vector3(hw - 0.05f, 2.4f, (front + back) / 2f), new Vector3(0.04f, 0.5f, 1.4f), beer, collider: false);
                    break;
                case Trade.Arcade:
                    for (float z = front; z < back; z += 1.2f)
                    {
                        Cabinet(c, r, new Vector3(-hw + 0.5f, 0f, z), 90f, rng);
                        Cabinet(c, r, new Vector3(hw - 0.5f, 0f, z), 270f, rng);
                    }
                    for (float x = -hw + 2.5f; x < hw - 2.5f; x += 1.2f) Cabinet(c, r, new Vector3(x, 0f, (front + back) / 2f), 0f, rng);
                    break;
                case Trade.Gym:
                    for (float z = front; z < back; z += 2f)
                    for (float x = -hw + 1.2f; x < hw - 1f; x += 2.4f)
                    {
                        k.Span(r, "Bench", new Vector3(x - 0.2f, 0f, z), new Vector3(x + 0.2f, 0.45f, z + 1.3f), c.P.Lit(new Color(0.12f, 0.12f, 0.13f)));
                        k.Box(r, "Bar", new Vector3(x, 1.1f, z + 1f), new Vector3(1.8f, 0.05f, 0.05f), metal, collider: false);
                    }
                    k.Span(r, "Mirror", new Vector3(-hw + 0.02f, 0.3f, front), new Vector3(-hw + 0.04f, 2.4f, back), c.P.Lit(new Color(0.7f, 0.75f, 0.78f), 0.95f), collider: false);
                    break;
                case Trade.MotelOffice:
                    k.Box(r, "Key board", new Vector3(0f, 1.8f, d - 0.05f), new Vector3(1.4f, 0.9f, 0.05f), counter, collider: false);
                    k.Fit(r, "loungeChair", new Vector3(-hw + 0.8f, 0f, front), new Vector3(0.9f, 0f, 0f), 90f);
                    k.Box(r, "Ice machine", new Vector3(hw - 0.6f, 0.8f, front), new Vector3(0.8f, 1.6f, 0.8f), c.P.Lit(new Color(0.85f, 0.85f, 0.88f), 0.5f));
                    break;
                case Trade.Jeweler:
                    Jeweler.Dress(c, r, hw, d, front, back);
                    break;
                case Trade.CarWash:
                case Trade.Moto:
                    k.Fit(r, "desk", new Vector3(-hw + 1.5f, 0f, front + 0.5f), new Vector3(1.4f, 0f, 0f));
                    k.Fit(r, "chairDesk", new Vector3(-hw + 1.5f, 0f, front + 1.3f), new Vector3(0.6f, 0f, 0f), 180f);
                    for (float z = front; z < back; z += 1.6f) k.Fit(r, "bookcaseClosed", new Vector3(hw - 0.3f, 0f, z), new Vector3(0f, 1.8f, 1.2f), 270f);
                    break;
            }
        }

        /// <summary>
        /// One item of shop stock: the Sketchfab grocery pack ("food_…") is modelled at real size with its origin on
        /// the shelf, so it goes in as is; the Quaternius food is scaled to a 24 cm item.
        /// </summary>
        private static void Stock(Kit k, Transform r, string item, Vector3 at, float yaw)
        {
            if (item.StartsWith("food_")) k.Model(r, item, at, yaw);
            else k.Prop(r, item, at, 0.24f, yaw);
        }

        /// <summary>Two-sided aisles of shelving with goods (bottles, boxes, tins), coolers for liquor and groceries.</summary>
        private static void Aisles(CityContext c, Transform r, Trade t, float hw, float front, float back, System.Random rng)
        {
            Kit k = c.Kit;
            Material shelf = c.P.Lit(t == Trade.Pharmacy ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.5f, 0.52f, 0.55f), 0.4f);
            string[] goods = t switch
            {
                Trade.Liquor => new[] { "food_sm_wine_bottle", "food_sm_beer_bottle", "food_sm_bottle", "food_sm_wine_bottle", "food_sm_beer_can" },
                Trade.Supermarket => new[] { "food_sm_cereal", "food_sm_pasta", "food_sm_canned_food_3", "food_sm_juice_carton", "food_sm_milk_bottle",
                    "food_sm_sause", "food_sm_coffee_box", "food_sm_olive_oil", "food_sm_porridge" },
                Trade.FishMarket => new[] { "Fish", "Fish", "Lettuce_Whole", "Fish" },
                Trade.Discount => new[] { "food_sm_snack", "food_sm_chocolate_bar", "food_sm_canned_food_4", "food_sm_detergent_powder", "food_sm_dishwashing_liquid", "food_sm_bleach" },
                Trade.Pharmacy => new[] { "food_sm_pills_box", "food_sm_protein_jar", "food_sm_detergent_liquid", "food_sm_pills_box", "food_sm_glue" },
                _ => null,
            };
            Color[] boxes = { new Color(0.8f, 0.3f, 0.2f), new Color(0.2f, 0.4f, 0.7f), new Color(0.9f, 0.8f, 0.3f), new Color(0.3f, 0.6f, 0.35f), new Color(0.9f, 0.9f, 0.88f) };
            for (float x = -hw + 2.2f; x < hw - 1.8f; x += 2.6f)
            {
                k.Span(r, "Shelf", new Vector3(x - 0.3f, 0f, front), new Vector3(x + 0.3f, 1.7f, back), shelf);
                for (int level = 0; level < 3; level++)
                {
                    float y = 0.35f + level * 0.5f;
                    foreach (float side in new[] { -1f, 1f })
                        for (float z = front + 0.2f; z < back - 0.1f; z += goods != null ? 0.28f : 0.45f)
                        {
                            if (goods != null) Stock(k, r, goods[rng.Next(goods.Length)], new Vector3(x + side * 0.42f, y, z), side > 0f ? 90f : 270f);
                            else
                            {
                                float h = 0.15f + (float)rng.NextDouble() * 0.2f;
                                k.Box(r, "Goods", new Vector3(x + side * 0.4f, y + h / 2f, z), new Vector3(0.2f, h, 0.36f),
                                    c.P.Lit(boxes[rng.Next(boxes.Length)], 0.2f), collider: false);
                            }
                        }
                }
            }
            if (t == Trade.Liquor || t == Trade.Supermarket)
            {
                k.Span(r, "Cooler", new Vector3(-hw + 0.1f, 0f, front), new Vector3(-hw + 0.8f, 2.1f, back), c.P.Lit(new Color(0.85f, 0.87f, 0.9f), 0.6f));
                k.Span(r, "Cooler glow", new Vector3(-hw + 0.8f, 0.3f, front + 0.1f), new Vector3(-hw + 0.82f, 1.9f, back - 0.1f), c.P.Unlit(new Color(0.8f, 0.9f, 1f)), collider: false);
            }
            if (t == Trade.AutoParts)
                for (float z = front; z < back; z += 0.8f)
                    k.Cylinder(r, "Tyre", new Vector3(hw - 0.5f, 0.35f, z), 0.7f, 0.25f, c.P.Lit(new Color(0.08f, 0.08f, 0.09f))).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        /// <summary>
        /// Convenience-store fittings (the gas-station pack) in the strip between the last aisle and the right-hand wall:
        /// coffee and slush machines on a drinks counter by the windows, an ice-cream chest behind them, and the
        /// cigarette rack on the wall behind the till. Local frame: x across the shop, z from the storefront (0) back.
        /// </summary>
        private static void Convenience(CityContext c, Transform r, float hw, float d, float front, float back)
        {
            Kit k = c.Kit;
            if (back - front < 4f || k.Art == null || k.Art.Model("gas_coffee_machine") == null) return;
            float x = hw - 0.42f;
            k.Span(r, "Drinks counter", new Vector3(hw - 0.75f, 0f, front), new Vector3(hw - 0.05f, 0.9f, front + 1.5f), c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.3f));
            k.Fit(r, "gas_coffee_machine", new Vector3(x, 0.9f, front + 0.35f), new Vector3(0f, 0.8f, 0f), 270f); // the pack faces +z: yaw 270 turns fronts to -x, into the shop
            k.Fit(r, "gas_ice_slush_machine", new Vector3(x, 0.9f, front + 1.05f), new Vector3(0f, 0.75f, 0f), 270f);
            k.Solid(k.Fit(r, "gas_fridge", new Vector3(hw - 0.55f, 0f, front + 2.5f), new Vector3(0f, 0.9f, 0f), 270f));
            k.Fit(r, "gas_cigars", new Vector3(-hw * 0.3f, 1.1f, d - 0.18f), new Vector3(1.8f, 0f, 0f), 180f);
        }

        private static void GlassCase(CityContext c, Transform r, Vector3 at, float length, float width)
        {
            Kit k = c.Kit;
            k.Span(r, "Case base", at + new Vector3(-width / 2f, 0f, -length / 2f), at + new Vector3(width / 2f, 0.8f, length / 2f), c.P.Lit(new Color(0.25f, 0.22f, 0.2f), 0.3f));
            k.Pane(r, "Case glass", at + new Vector3(-width / 2f, 0.8f, -length / 2f), at + new Vector3(width / 2f, 1.1f, length / 2f), new Color(0.75f, 0.85f, 0.9f, 0.25f));
            for (float z = -length / 2f + 0.2f; z < length / 2f; z += 0.35f)
                k.Box(r, "Trinket", at + new Vector3(0f, 0.85f, z), new Vector3(0.12f, 0.08f, 0.12f), c.P.Lit(new Color(0.85f, 0.75f, 0.3f), 0.8f), collider: false);
        }

        private static void Cabinet(CityContext c, Transform r, Vector3 at, float yaw, System.Random rng)
        {
            Transform g = Kit.Group(r, "Arcade cabinet", at, yaw);
            c.Kit.Box(g, "Body", new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 1.8f, 0.7f), c.P.Lit(Color.HSVToRGB((float)rng.NextDouble(), 0.7f, 0.4f), 0.3f));
            c.Kit.Box(g, "Screen", new Vector3(0f, 1.35f, -0.36f), new Vector3(0.55f, 0.45f, 0.02f), c.P.Glow(Color.HSVToRGB((float)rng.NextDouble(), 0.6f, 1f), 1.6f), collider: false);
        }
    }

    /// <summary>
    /// A shop's open / closed look (TOWN_SPEC A12): lights up while open (a dim night-light while shut), the door
    /// sign reading OPEN, CLOSED or BACK SOON (staff on a break), and neon names that go dark after hours.
    /// </summary>
    public sealed class OpenLights : MonoBehaviour
    {
        private CityContext _c;
        private Hours _hours;
        private Light _light;
        private StaffNpc _staff;
        private Renderer _panel;
        private TextMesh _text;
        private TextMesh _neon;
        private Material _open, _back, _closed, _neonOn, _neonOff;
        private float _full;
        private float _timer;

        public enum State { Open, Break, Closed }
        public State Now { get; private set; } = (State)(-1);

        public void Configure(CityContext c, Hours hours, Light light, StaffNpc staff = null, Renderer panel = null, TextMesh text = null,
            TextMesh neon = null, Material open = null, Material back = null, Material closed = null, Material neonOff = null)
        {
            _neonOff = neonOff;
            _c = c;
            _hours = hours;
            _light = light;
            _full = light.intensity;
            _staff = staff;
            _panel = panel;
            _text = text;
            _neon = neon;
            _open = open;
            _back = back;
            _closed = closed;
            if (neon != null) _neonOn = neon.GetComponent<MeshRenderer>().sharedMaterial;
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f || _c == null) return;
            _timer = 1f;
            bool open = _hours.Contains(_c.Game.Clock.Now);
            State state = !open ? State.Closed : _staff != null && !_staff.AtStation ? State.Break : State.Open;
            if (state == Now) return;
            Now = state;
            _light.intensity = open ? _full : _full * 0.12f;
            if (_panel != null)
            {
                _panel.sharedMaterial = state == State.Open ? _open : state == State.Break ? _back : _closed;
                _text.text = state == State.Open ? "OPEN" : state == State.Break ? "BACK SOON" : "CLOSED";
                _text.color = state == State.Closed ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.05f, 0.05f, 0.05f);
            }
            if (_neon != null && _neonOff != null) _neon.GetComponent<MeshRenderer>().sharedMaterial = open ? _neonOn : _neonOff;
        }
    }
}
