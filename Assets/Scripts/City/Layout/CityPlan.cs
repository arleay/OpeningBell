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
    /// The authored city layout (Phase 9 core): six streets on a ring with two cross streets, three core blocks
    /// (residential, commercial, downtown) and an outer ring of buildings that closes the world. Everything is
    /// generated from these numbers. +x is east, +z is north; the player's apartment sits at the origin.
    /// </summary>
    public static class CityPlan
    {
        public const float RoadHalfWidth = 5f;
        public const float SidewalkWidth = 3.5f;
        public const float LaneOffset = 2.5f;
        public const float RoadY = -0.15f;
        /// <summary>Stop lines sit just outside the crosswalk band (5–8.5 m from the junction centre).</summary>
        public const float StopLine = 9f;
        public const float CrosswalkNear = RoadHalfWidth;
        public const float CrosswalkFar = RoadHalfWidth + SidewalkWidth;

        public static readonly Rect World = Rect.MinMaxRect(-80f, -45f, 250f, 105f);

        public static readonly (string Name, Vector2 P, NodeControl Control)[] Nodes =
        {
            ("Maple & Cedar", new Vector2(-45f, -14f), NodeControl.None),
            ("Maple & First", new Vector2(55f, -14f), NodeControl.StopOnStem),
            ("Maple & Exchange", new Vector2(135f, -14f), NodeControl.Lights),
            ("Maple & Harbor", new Vector2(215f, -14f), NodeControl.None),
            ("Grove & Cedar", new Vector2(-45f, 70f), NodeControl.None),
            ("Grove & First", new Vector2(55f, 70f), NodeControl.StopOnStem),
            ("Grove & Exchange", new Vector2(135f, 70f), NodeControl.StopOnStem),
            ("Grove & Harbor", new Vector2(215f, 70f), NodeControl.None),
        };

        public static readonly (int A, int B, string Street)[] Streets =
        {
            (0, 1, "MAPLE ST"), (1, 2, "MAPLE ST"), (2, 3, "MAPLE ST"),
            (4, 5, "GROVE ST"), (5, 6, "GROVE ST"), (6, 7, "GROVE ST"),
            (0, 4, "CEDAR AVE"), (1, 5, "FIRST ST"), (2, 6, "EXCHANGE ST"), (3, 7, "HARBOR AVE"),
        };

        /// <summary>Curb-to-curb blocks. Each gets a full sidewalk ring; buildings sit inside the ring.</summary>
        public static readonly (string Name, Rect Area, Color Ground)[] Blocks =
        {
            ("Residential", Rect.MinMaxRect(-40f, -9f, 50f, 65f), new Color(0.33f, 0.45f, 0.25f)),
            ("Commercial", Rect.MinMaxRect(60f, -9f, 130f, 65f), new Color(0.42f, 0.42f, 0.41f)),
            ("Downtown", Rect.MinMaxRect(140f, -9f, 210f, 65f), new Color(0.55f, 0.53f, 0.5f)),
            ("South", Rect.MinMaxRect(-80f, -45f, 250f, -19f), new Color(0.4f, 0.4f, 0.4f)),
            ("North", Rect.MinMaxRect(-80f, 75f, 250f, 105f), new Color(0.4f, 0.4f, 0.4f)),
            ("West", Rect.MinMaxRect(-80f, -19f, -50f, 75f), new Color(0.4f, 0.4f, 0.4f)),
            ("East", Rect.MinMaxRect(220f, -19f, 250f, 75f), new Color(0.4f, 0.4f, 0.4f)),
        };

        // ---- special buildings (interiors) ----

        /// <summary>Two-storey walk-up around the player's unit (the existing apartment room at the origin).</summary>
        public static readonly Rect ApartmentBuilding = Rect.MinMaxRect(-3.3f, -4.8f, 12.3f, 2.8f);
        public static readonly Rect CalderBuilding = Rect.MinMaxRect(144f, -5.5f, 168f, 18.5f);
        public static readonly Rect CoffeeShop = Rect.MinMaxRect(64f, -5.5f, 78f, 8f);
        public static readonly Rect CornerMart = Rect.MinMaxRect(112f, -5.5f, 126.5f, 7f);
        public static readonly Rect SkateShop = Rect.MinMaxRect(78.5f, -5.5f, 94f, 9f);
        public static readonly Rect BikeShop = Rect.MinMaxRect(94.5f, -5.5f, 111.5f, 9f);

        /// <summary>Everything else: facades that make the streets.</summary>
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
            new Shell(97f, 50f, 126.5f, 61.5f, 14f, FacadeStyle.Concrete),
            // Downtown block (Calder is special)
            new Shell(172f, -5.5f, 206.5f, 24f, 42f, FacadeStyle.Glass, sign: "MERIDIAN TOWER"),
            new Shell(144f, 27f, 168f, 61.5f, 14f, FacadeStyle.Concrete, sign: "PARKING"),
            new Shell(172f, 30f, 206.5f, 61.5f, 30f, FacadeStyle.Concrete),
            // South ring (faces Maple)
            new Shell(-76.5f, -41.5f, -51f, -22.5f, 9f, FacadeStyle.Brick),
            new Shell(-50.5f, -41.5f, -21f, -22.5f, 7.5f, FacadeStyle.Townhouse),
            new Shell(-20.5f, -41.5f, 9f, -22.5f, 10f, FacadeStyle.Brick),
            new Shell(9.5f, -41.5f, 39f, -22.5f, 12f, FacadeStyle.Stucco, storefront: true),
            new Shell(39.5f, -41.5f, 69f, -22.5f, 14f, FacadeStyle.Brick, storefront: true),
            new Shell(69.5f, -41.5f, 99f, -22.5f, 16f, FacadeStyle.Concrete, storefront: true),
            new Shell(99.5f, -41.5f, 129f, -22.5f, 12f, FacadeStyle.Brick, storefront: true),
            new Shell(129.5f, -41.5f, 159f, -22.5f, 30f, FacadeStyle.Glass),
            new Shell(159.5f, -41.5f, 189f, -22.5f, 24f, FacadeStyle.Concrete, storefront: true),
            new Shell(189.5f, -41.5f, 219f, -22.5f, 36f, FacadeStyle.Glass),
            new Shell(219.5f, -41.5f, 246.5f, -22.5f, 18f, FacadeStyle.Concrete),
            // North ring (faces Grove)
            new Shell(-76.5f, 78.5f, -41f, 101.5f, 8f, FacadeStyle.Townhouse),
            new Shell(-40.5f, 78.5f, -6f, 101.5f, 9f, FacadeStyle.Brick),
            new Shell(-5.5f, 78.5f, 29f, 101.5f, 7.5f, FacadeStyle.Stucco),
            new Shell(29.5f, 78.5f, 64f, 101.5f, 10f, FacadeStyle.Townhouse),
            new Shell(64.5f, 78.5f, 99f, 101.5f, 13f, FacadeStyle.Brick, storefront: true),
            new Shell(99.5f, 78.5f, 134f, 101.5f, 12f, FacadeStyle.Concrete, storefront: true),
            new Shell(134.5f, 78.5f, 169f, 101.5f, 22f, FacadeStyle.Concrete),
            new Shell(169.5f, 78.5f, 204f, 101.5f, 28f, FacadeStyle.Glass),
            new Shell(204.5f, 78.5f, 246.5f, 101.5f, 20f, FacadeStyle.Concrete),
            // West ring (faces Cedar)
            new Shell(-76.5f, -15.5f, -53.5f, 18f, 8f, FacadeStyle.Stucco),
            new Shell(-76.5f, 18.5f, -53.5f, 44f, 9.5f, FacadeStyle.Townhouse),
            new Shell(-76.5f, 44.5f, -53.5f, 71.5f, 8.5f, FacadeStyle.Brick),
            // East ring (faces Harbor)
            new Shell(223.5f, -15.5f, 246.5f, 22f, 26f, FacadeStyle.Concrete),
            new Shell(223.5f, 22.5f, 246.5f, 71.5f, 34f, FacadeStyle.Glass),
        };
    }
}
