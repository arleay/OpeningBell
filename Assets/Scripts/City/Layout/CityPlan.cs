using UnityEngine;

namespace OpeningBell.City
{
    public enum NodeControl
    {
        /// <summary>Corner or uncontrolled junction: first come, first served.</summary>
        None,
        /// <summary>T junction: the stem stops, the through street has priority.</summary>
        StopOnStem,
        Lights,
    }

    public enum FacadeStyle
    {
        Brick,
        Stucco,
        Concrete,
        Glass,
        Townhouse,
    }

    /// <summary>A plain building volume: outside only, no interior.</summary>
    public readonly struct Shell
    {
        public readonly Rect Footprint; // x/z
        public readonly float Height;
        public readonly FacadeStyle Style;
        public readonly bool Storefront;
        public readonly string Sign;

        public Shell(float x0, float z0, float x1, float z1, float height, FacadeStyle style, bool storefront = false, string sign = null)
        {
            Footprint = Rect.MinMaxRect(x0, z0, x1, z1);
            Height = height;
            Style = style;
            Storefront = storefront;
            Sign = sign;
        }
    }

    /// <summary>
    /// The authored town layout. The Phase 9 core (the apartment on Maple St, the shops, the Calder Building and a
    /// low-rise town centre) sits in the middle of a small open town: an outer grid of streets (Oak, Birch, Willow,
    /// Pine) with houses and yards, an open field and green edges instead of a wall of tall blocks. Everything is
    /// generated from these numbers. +x is east, +z is north; the player's apartment sits at the origin.
    /// </summary>
    public static class CityPlan
    {
        public const float RoadHalfWidth = 5f;
        public const float SidewalkWidth = 3.5f;
        // Lanes sit 2 m off the centre line: leaves room for a car parked at the kerb beside passing traffic.
        public const float LaneOffset = 2f;
        public const float RoadY = -0.15f;
        /// <summary>Stop lines sit just outside the crosswalk band (5–8.5 m from the junction centre).</summary>
        public const float StopLine = 9f;
        public const float CrosswalkNear = RoadHalfWidth;
        public const float CrosswalkFar = RoadHalfWidth + SidewalkWidth;

        public static readonly Rect World = Rect.MinMaxRect(-275f, -150f, 255f, 200f);

        // Street lines: x = -235 Pine, -140 Willow, -45 Cedar, 55 First, 135 Exchange, 215 Harbor;
        // z = -110 Oak, -14 Maple, 70 Grove, 160 Birch. First and Exchange only run Maple–Grove (the core).
        // Four-way crossings are first come, first served; T junctions stop on the stem. Lights only work at a T
        // (their two phases are "through" and "stem"), so new crossings don't get them. No dead ends: every lane
        // needs an exit.
        public static readonly (string Name, Vector2 P, NodeControl Control)[] Nodes =
        {
            ("Maple & Cedar", new Vector2(-45f, -14f), NodeControl.None),
            ("Maple & First", new Vector2(55f, -14f), NodeControl.StopOnStem),
            ("Maple & Exchange", new Vector2(135f, -14f), NodeControl.Lights),
            ("Maple & Harbor", new Vector2(215f, -14f), NodeControl.StopOnStem),
            ("Grove & Cedar", new Vector2(-45f, 70f), NodeControl.None),
            ("Grove & First", new Vector2(55f, 70f), NodeControl.StopOnStem),
            ("Grove & Exchange", new Vector2(135f, 70f), NodeControl.StopOnStem),
            ("Grove & Harbor", new Vector2(215f, 70f), NodeControl.StopOnStem),
            ("Maple & Willow", new Vector2(-140f, -14f), NodeControl.None), // 8
            ("Maple & Pine", new Vector2(-235f, -14f), NodeControl.StopOnStem),
            ("Grove & Willow", new Vector2(-140f, 70f), NodeControl.None), // 10
            ("Grove & Pine", new Vector2(-235f, 70f), NodeControl.StopOnStem),
            ("Oak & Pine", new Vector2(-235f, -110f), NodeControl.None), // 12
            ("Oak & Willow", new Vector2(-140f, -110f), NodeControl.StopOnStem),
            ("Oak & Cedar", new Vector2(-45f, -110f), NodeControl.StopOnStem),
            ("Oak & Harbor", new Vector2(215f, -110f), NodeControl.None), // 15
            ("Birch & Pine", new Vector2(-235f, 160f), NodeControl.None), // 16
            ("Birch & Willow", new Vector2(-140f, 160f), NodeControl.StopOnStem),
            ("Birch & Cedar", new Vector2(-45f, 160f), NodeControl.StopOnStem),
            ("Birch & Harbor", new Vector2(215f, 160f), NodeControl.None), // 19
        };

        public static readonly (int A, int B, string Street)[] Streets =
        {
            (0, 1, "MAPLE ST"), (1, 2, "MAPLE ST"), (2, 3, "MAPLE ST"), (9, 8, "MAPLE ST"), (8, 0, "MAPLE ST"),
            (4, 5, "GROVE ST"), (5, 6, "GROVE ST"), (6, 7, "GROVE ST"), (11, 10, "GROVE ST"), (10, 4, "GROVE ST"),
            (0, 4, "CEDAR AVE"), (14, 0, "CEDAR AVE"), (4, 18, "CEDAR AVE"),
            (1, 5, "FIRST ST"), (2, 6, "EXCHANGE ST"),
            (3, 7, "HARBOR AVE"), (15, 3, "HARBOR AVE"), (7, 19, "HARBOR AVE"),
            (12, 13, "OAK ST"), (13, 14, "OAK ST"), (14, 15, "OAK ST"),
            (16, 17, "BIRCH ST"), (17, 18, "BIRCH ST"), (18, 19, "BIRCH ST"),
            (12, 9, "PINE RD"), (9, 11, "PINE RD"), (11, 16, "PINE RD"),
            (13, 8, "WILLOW AVE"), (8, 10, "WILLOW AVE"), (10, 17, "WILLOW AVE"),
        };

        private static readonly Color Grass = new Color(0.33f, 0.45f, 0.25f);
        private static readonly Color Paving = new Color(0.42f, 0.42f, 0.41f);

        /// <summary>
        /// Curb-to-curb blocks. Each gets a full sidewalk ring; buildings sit inside the ring. Lawn blocks are grass
        /// underfoot (footsteps and the look); the rest are paved.
        /// </summary>
        public static readonly (string Name, Rect Area, Color Ground, bool Lawn)[] Blocks =
        {
            // The core
            ("Residential", Rect.MinMaxRect(-40f, -9f, 50f, 65f), Grass, true),
            ("Commercial", Rect.MinMaxRect(60f, -9f, 130f, 65f), Paving, false),
            ("Downtown", Rect.MinMaxRect(140f, -9f, 210f, 65f), new Color(0.55f, 0.53f, 0.5f), false),
            // Neighbourhoods west of the core, between Maple and Grove
            ("Willow Park", Rect.MinMaxRect(-135f, -9f, -50f, 65f), Grass, true),
            ("Pine Hill", Rect.MinMaxRect(-230f, -9f, -145f, 65f), Grass, true),
            // South of Maple
            ("Southside", Rect.MinMaxRect(-40f, -105f, 210f, -19f), Grass, true),
            ("Willow South", Rect.MinMaxRect(-135f, -105f, -50f, -19f), Grass, true),
            ("Pine South", Rect.MinMaxRect(-230f, -105f, -145f, -19f), Grass, true),
            // North of Grove
            ("Northside", Rect.MinMaxRect(-40f, 75f, 210f, 155f), Grass, true),
            ("Willow North", Rect.MinMaxRect(-135f, 75f, -50f, 155f), Grass, true),
            ("Pine North", Rect.MinMaxRect(-230f, 75f, -145f, 155f), Grass, true),
            // Green edges beyond the outer streets
            ("South Edge", Rect.MinMaxRect(-275f, -150f, 255f, -115f), Grass, true),
            ("North Edge", Rect.MinMaxRect(-275f, 165f, 255f, 200f), Grass, true),
            ("West Edge", Rect.MinMaxRect(-275f, -115f, -240f, 165f), Grass, true),
            ("East Edge", Rect.MinMaxRect(220f, -115f, 255f, 165f), Grass, true),
        };

        /// <summary>
        /// Rows of houses (front yards, driveways, back fences): along the north or south side of a block from X0 to
        /// X1, facing the street on that side.
        /// </summary>
        public static readonly (string Block, float X0, float X1, bool FacesNorth)[] HouseRows =
        {
            ("Willow Park", -135f, -50f, false), ("Willow Park", -135f, -50f, true),
            ("Pine Hill", -230f, -145f, false), ("Pine Hill", -230f, -145f, true),
            ("Willow South", -135f, -50f, false), ("Willow South", -135f, -50f, true),
            ("Pine South", -230f, -145f, false), ("Pine South", -230f, -145f, true),
            ("Willow North", -135f, -50f, false), ("Willow North", -135f, -50f, true),
            ("Pine North", -230f, -145f, false), ("Pine North", -230f, -145f, true),
            // Opposite the apartment on Maple (the main street's shops take over east of First), and along Oak
            ("Southside", -40f, 55f, true), ("Southside", -40f, 210f, false),
            // Along Grove up to the town field, and all along Birch
            ("Northside", -40f, 125f, false), ("Northside", -40f, 210f, true),
        };

        /// <summary>The open field on Grove (the rest of Northside's frontage): grass and a few trees, no buildings.</summary>
        public static readonly Rect TownField = Rect.MinMaxRect(128f, 78.5f, 206.5f, 118f);

        public static Rect Block(string name)
        {
            foreach (var b in Blocks)
                if (b.Name == name) return b.Area;
            throw new System.ArgumentException(name);
        }

        // ---- special buildings (interiors) ----

        /// <summary>Two-storey walk-up around the player's unit (the existing apartment room at the origin).</summary>
        public static readonly Rect ApartmentBuilding = Rect.MinMaxRect(-3.3f, -4.8f, 12.3f, 2.8f);
        public static readonly Rect CalderBuilding = Rect.MinMaxRect(144f, -5.5f, 168f, 18.5f);
        public static readonly Rect CoffeeShop = Rect.MinMaxRect(64f, -5.5f, 78f, 8f);
        public static readonly Rect CornerMart = Rect.MinMaxRect(112f, -5.5f, 126.5f, 7f);
        public static readonly Rect SkateShop = Rect.MinMaxRect(78.5f, -5.5f, 94f, 9f);
        public static readonly Rect BikeShop = Rect.MinMaxRect(94.5f, -5.5f, 111.5f, 9f);

        /// <summary>Everything else with walls: low-rise facades along the main streets. Houses are <see cref="HouseRows"/>.</summary>
        public static readonly Shell[] Shells =
        {
            // Residential block
            new Shell(-36.5f, -5.5f, -26f, 5f, 8.5f, FacadeStyle.Townhouse),
            new Shell(-25.5f, -5.5f, -16f, 5f, 9.5f, FacadeStyle.Brick),
            new Shell(-15.5f, -5.5f, -6.5f, 5f, 8f, FacadeStyle.Townhouse),
            new Shell(16f, -5.5f, 46.5f, 8f, 10.5f, FacadeStyle.Stucco),
            new Shell(-36.5f, 51f, -12f, 61.5f, 8f, FacadeStyle.Townhouse),
            new Shell(-8f, 51f, 20f, 61.5f, 9f, FacadeStyle.Brick),
            new Shell(24f, 51f, 46.5f, 61.5f, 7.5f, FacadeStyle.Stucco),
            // Commercial block (coffee shop, skate shop, bike shop and mart are special)
            new Shell(63.5f, 50f, 94f, 61.5f, 11f, FacadeStyle.Stucco, storefront: true),
            new Shell(97f, 50f, 126.5f, 61.5f, 12f, FacadeStyle.Concrete),
            // Town centre (Calder is special): low-rise, not a financial district
            new Shell(172f, -5.5f, 206.5f, 24f, 16f, FacadeStyle.Glass, sign: "MERIDIAN"),
            new Shell(144f, 27f, 168f, 61.5f, 14f, FacadeStyle.Concrete, sign: "PARKING"),
            new Shell(172f, 30f, 206.5f, 61.5f, 12f, FacadeStyle.Brick),
            // Main street, south side of Maple opposite the shops
            new Shell(62.5f, -45f, 88f, -22.5f, 8f, FacadeStyle.Brick, storefront: true),
            new Shell(88.5f, -45f, 112f, -22.5f, 10f, FacadeStyle.Stucco, storefront: true),
            new Shell(112.5f, -45f, 132f, -22.5f, 7.5f, FacadeStyle.Brick, storefront: true),
            new Shell(138f, -45f, 170f, -22.5f, 11f, FacadeStyle.Concrete, storefront: true),
            new Shell(170.5f, -45f, 206.5f, -22.5f, 9f, FacadeStyle.Townhouse, storefront: true),
        };
    }
}
