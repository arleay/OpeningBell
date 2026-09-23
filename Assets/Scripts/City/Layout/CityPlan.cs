using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    public enum NodeControl
    {
        /// <summary>Uncontrolled: first come, first served.</summary>
        None,
        /// <summary>The minor street stops; the main street (the one passing through) has priority.</summary>
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

    /// <summary>
    /// Road classes (TOWN_SPEC): width, sidewalks and whether traffic uses them. Alleys and dirt roads are
    /// "driveways": they join streets at the kerb rather than at junctions, and traffic keeps off them.
    /// </summary>
    public enum RoadClass
    {
        Highway,
        Street,
        Residential,
        Industrial,
        Alley,
        Dirt,
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

    /// <summary>A named polyline road. Streets meet wherever they share a point exactly.</summary>
    public sealed class StreetDef
    {
        public readonly string Name;
        public readonly RoadClass Class;
        public readonly Vector2[] Points;

        public StreetDef(string name, RoadClass cls, params Vector2[] points)
        {
            Name = name;
            Class = cls;
            Points = points;
        }

        public bool IsDriveway => Class == RoadClass.Alley || Class == RoadClass.Dirt;
    }

    /// <summary>Houses along one side of a street between two points on its centre line.</summary>
    public readonly struct Frontage
    {
        public readonly string Street;
        public readonly Vector2 From, To;
        /// <summary>+1: houses on the left of From→To; -1: on the right.</summary>
        public readonly int Side;
        public readonly HouseTier Tier;

        public Frontage(string street, Vector2 from, Vector2 to, int side, HouseTier tier)
        {
            Street = street;
            From = from;
            To = to;
            Side = side;
            Tier = tier;
        }
    }

    /// <summary>
    /// The authored town layout (TOWN_SPEC). The old core (the apartment on Maple St at the origin, the shops, the
    /// Calder Building) is where it always was; the town now spreads around it: the highway entrance west, the
    /// industrial district south-west, working-class streets south of the rail line, the waterfront on the bay,
    /// the canal and the entertainment district across it, the residential slope north and the Crest hill.
    /// Everything is generated from these numbers. +x is east, +z is north.
    /// </summary>
    public static class CityPlan
    {
        /// <summary>Road surfaces sit this far below the sidewalk / lot level (a kerb).</summary>
        public const float RoadY = -0.15f;
        public const float SidewalkWidth = 3.5f;
        public const float RoadHalfWidth = 5f; // a Street; the old core is all Streets
        public const float LaneOffset = 2f;
        public const float StopLine = 9f;
        public const float CrosswalkNear = RoadHalfWidth;
        public const float CrosswalkFar = RoadHalfWidth + SidewalkWidth;

        /// <summary>The playable area (the map shows it; outside is forest, hills and water).</summary>
        public static readonly Rect World = Rect.MinMaxRect(-800f, -420f, 760f, 540f);

        public static float HalfWidth(RoadClass c) => c switch
        {
            RoadClass.Highway => 6f,
            RoadClass.Street => 5f,
            RoadClass.Residential => 4f,
            RoadClass.Industrial => 5.5f,
            RoadClass.Alley => 3f,
            _ => 2.6f,
        };

        public static float Sidewalk(RoadClass c) => c switch
        {
            RoadClass.Street => SidewalkWidth,
            RoadClass.Residential => 2.5f,
            RoadClass.Industrial => 2f,
            _ => 0f,
        };

        /// <summary>Lane centre off the road centre: room for a car parked at the kerb beside passing traffic.</summary>
        public static float LaneOffsetFor(RoadClass c) => c switch
        {
            RoadClass.Highway => 2.4f,
            RoadClass.Residential => 1.8f,
            RoadClass.Industrial => 2.4f,
            _ => LaneOffset,
        };

        public static bool HasTraffic(RoadClass c) => c <= RoadClass.Industrial;

        private static Vector2 P(float x, float z) => new Vector2(x, z);

        /// <summary>Most important first: the first street through a junction is its main street.</summary>
        public static readonly StreetDef[] Streets =
        {
            new StreetDef("KELL HWY", RoadClass.Highway, P(-780, 70), P(-700, 62), P(-620, 40), P(-545, 8), P(-470, -14)),
            new StreetDef("MAPLE ST", RoadClass.Street, P(-470, -14), P(-360, -14), P(-250, -14), P(-140, -14), P(-45, -14), P(55, -14),
                P(135, -14), P(215, -14), P(290, -14), P(345, -14), P(450, -14), P(560, -14)),
            new StreetDef("HARBOR RD", RoadClass.Street, P(-470, -270), P(-360, -270), P(-250, -270), P(-140, -270), P(-45, -270), P(80, -272),
                P(215, -270), P(290, -270)),
            new StreetDef("GROVE ST", RoadClass.Street, P(-360, 70), P(-250, 70), P(-140, 70), P(-45, 70), P(55, 70), P(135, 70), P(215, 70), P(290, 70)),
            new StreetDef("CANAL ST", RoadClass.Street, P(290, -270), P(290, -160), P(290, -64), P(290, -14), P(290, 70), P(290, 170)),
            new StreetDef("MILL BRIDGE", RoadClass.Street, P(290, 170), P(345, 170)),
            new StreetDef("QUAY ST", RoadClass.Street, P(345, -150), P(345, -14), P(345, 50), P(345, 110), P(345, 170)),
            new StreetDef("HARBOR AVE", RoadClass.Street, P(215, -270), P(215, -215), P(215, -160), P(215, -64), P(215, -14), P(215, 70), P(215, 160), P(215, 230)),
            new StreetDef("EXCHANGE ST", RoadClass.Street, P(135, -160), P(135, -64), P(135, -14), P(135, 70), P(135, 160)),
            new StreetDef("FIRST ST", RoadClass.Street, P(55, -160), P(55, -64), P(55, -14), P(55, 70)),
            new StreetDef("CEDAR AVE", RoadClass.Street, P(-45, -270), P(-45, -215), P(-45, -160), P(-45, -64), P(-45, -14), P(-45, 70), P(-45, 160)),
            new StreetDef("PINE RD", RoadClass.Street, P(-250, -270), P(-250, -215), P(-250, -160), P(-250, -64), P(-250, -14), P(-250, 70), P(-250, 160), P(-250, 250)),
            new StreetDef("LUMBER RD", RoadClass.Street, P(-360, -14), P(-360, 70), P(-360, 150), P(-310, 178), P(-250, 160)),
            new StreetDef("RAIL ROW", RoadClass.Street, P(-250, -64), P(-140, -64), P(-45, -64), P(55, -64), P(135, -64), P(215, -64), P(290, -64)),
            new StreetDef("FOUNDRY ST", RoadClass.Street, P(-470, -160), P(-360, -160), P(-250, -160), P(-140, -160), P(-45, -160), P(55, -160),
                P(135, -160), P(215, -160), P(290, -160)),
            new StreetDef("NEON ROW", RoadClass.Street, P(345, 50), P(450, 50), P(560, 50)),
            new StreetDef("BAYVIEW AVE", RoadClass.Street, P(450, -150), P(450, -14), P(450, 50), P(450, 110)),
            new StreetDef("LANTERN ST", RoadClass.Street, P(345, 110), P(450, 110), P(560, 110)),
            new StreetDef("BAY BLVD", RoadClass.Street, P(345, -150), P(450, -150), P(560, -150), P(630, -165)),
            new StreetDef("EAST END RD", RoadClass.Street, P(560, -150), P(560, -14), P(560, 50), P(560, 110), P(560, 150)),
            new StreetDef("DEPOT RD", RoadClass.Industrial, P(-470, -14), P(-470, -100), P(-470, -160), P(-470, -270)),
            new StreetDef("MILL RD", RoadClass.Industrial, P(-470, -100), P(-400, -100), P(-360, -125), P(-360, -160)),
            new StreetDef("WILLOW AVE", RoadClass.Residential, P(-140, -270), P(-150, -215), P(-140, -160), P(-150, -110), P(-140, -64), P(-140, -14),
                P(-140, 70), P(-140, 150)),
            new StreetDef("BIRCH ST", RoadClass.Residential, P(-250, 160), P(-140, 150), P(-45, 160), P(45, 168), P(135, 160), P(215, 160)),
            new StreetDef("HILLCREST DR", RoadClass.Residential, P(-45, 160), P(-25, 210), P(25, 245), P(95, 262), P(160, 252), P(215, 230)),
            new StreetDef("ALDER WAY", RoadClass.Residential, P(-140, 150), P(-120, 205), P(-100, 250), P(-100, 298)),
            new StreetDef("RIDGE RD", RoadClass.Residential, P(-250, 250), P(-180, 280), P(-100, 298), P(-20, 302), P(60, 305), P(140, 300), P(200, 288)),
            new StreetDef("SUMMIT WAY", RoadClass.Residential, P(25, 245), P(40, 275), P(60, 305)),
            new StreetDef("GULL ST", RoadClass.Residential, P(-45, -215), P(20, -205), P(80, -218), P(135, -212), P(215, -215)),
            new StreetDef("TERN LN", RoadClass.Residential, P(-250, -215), P(-190, -222), P(-150, -215)),
            new StreetDef("CREST RD", RoadClass.Residential, P(560, 150), P(615, 180), P(655, 228), P(615, 268), P(650, 312), P(595, 345), P(525, 358)),

            // Alleys behind the Maple St shops and the main street, and the lane to the parking behind the Maple shops.
            new StreetDef("ALLEY", RoadClass.Alley, P(-250, 16), P(-140, 16)),
            new StreetDef("ALLEY", RoadClass.Alley, P(-140, 16), P(-45, 16)),
            new StreetDef("ALLEY", RoadClass.Alley, P(-250, -48), P(-140, -48)),
            new StreetDef("ALLEY", RoadClass.Alley, P(-140, -48), P(-45, -48)),
            new StreetDef("ALLEY", RoadClass.Alley, P(55, -49), P(135, -49)),
            new StreetDef("ALLEY", RoadClass.Alley, P(135, -49), P(215, -49)),
            new StreetDef("ALLEY", RoadClass.Alley, P(55, 30), P(100, 30)),
            // Dirt: to the trailer park, up to the overlook, to the campsite, to the water tower.
            new StreetDef("OLD MILL RD", RoadClass.Dirt, P(-470, -230), P(-540, -230), P(-620, -205), P(-690, -150), P(-712, -95)),
            new StreetDef("LOOKOUT RD", RoadClass.Dirt, P(-250, 235), P(-300, 240), P(-335, 300), P(-365, 372), P(-395, 405)),
            new StreetDef("CAMP RD", RoadClass.Dirt, P(-365, 372), P(-330, 400), P(-300, 425)),
            new StreetDef("TOWER RD", RoadClass.Dirt, P(615, 268), P(680, 262), P(700, 300)),
        };

        /// <summary>Signalised junctions; every other junction of three or more streets stops the minor street.</summary>
        public static readonly Vector2[] Lights =
        {
            P(-470, -14), P(-360, -14), P(-250, -14), P(-45, -14), P(135, -14), P(215, -14), P(290, -14), P(345, -14), P(450, -14), P(135, 70),
        };

        /// <summary>Uncontrolled crossings (quiet residential and waterfront corners).</summary>
        public static readonly Vector2[] Uncontrolled =
        {
            P(-140, 150), P(-100, 298), P(25, 245), P(60, 305), P(-150, -215), P(-250, 160),
        };

        /// <summary>Road grade overrides (metres) where the natural ground is too steep or a road runs into a hill.</summary>
        public static readonly Dictionary<Vector2, float> Grades = new Dictionary<Vector2, float>
        {
            [P(-780, 70)] = 6f, [P(-700, 62)] = 5f, [P(-620, 40)] = 2.5f, [P(-545, 8)] = 0.6f,
            // Crest Rd climbs steadily round the hill (cut into it); the mansions sit at the top.
            [P(615, 180)] = 8.5f, [P(655, 228)] = 13.5f, [P(615, 268)] = 18f, [P(650, 312)] = 22f, [P(595, 345)] = 26f, [P(525, 358)] = 29f,
            [P(25, 245)] = 9.4f, [P(40, 275)] = 11.8f,
        };

        // ---- the elevated railway ("Northline") and tunnels ----

        /// <summary>The viaduct runs along z = RailZ between its two tunnel portals.</summary>
        public const float RailZ = -64f, RailWest = -630f, RailEast = 670f;
        /// <summary>Maple Station: platforms on the viaduct between these x.</summary>
        public const float StationWest = 25f, StationEast = 85f;

        /// <summary>Top of the rail deck at x: 7.5 m over the flat town, 9 m east of the canal (the east bank is higher).</summary>
        public static float RailDeck(float x) => Mathf.Lerp(7.5f, 9f, Mathf.InverseLerp(300f, 345f, x));

        public readonly struct TunnelDef
        {
            public readonly Vector2 Portal, Inward;
            public readonly float Width, Floor, Height, Length;
            public readonly string Closed;

            public TunnelDef(Vector2 portal, Vector2 inward, float width, float floor, float height, float length, string closed)
            {
                Portal = portal;
                Inward = inward.normalized;
                Width = width;
                Floor = floor;
                Height = height;
                Length = length;
                Closed = closed;
            }

            /// <summary>Inside the tube's footprint (beyond the portal, within its width plus a margin).</summary>
            public bool Contains(Vector2 p, float margin = 0f)
            {
                float t = Vector2.Dot(p - Portal, Inward);
                float side = Mathf.Abs(Vector2.Dot(p - Portal, new Vector2(-Inward.y, Inward.x)));
                return t > -margin && t < Length + margin && side < Width / 2f + margin;
            }
        }

        /// <summary>The highway into the west hills (closed for repairs) and the railway into the west and east hills.</summary>
        public static readonly TunnelDef[] Tunnels =
        {
            new TunnelDef(P(-735, 66), P(-780, 70) - P(-735, 66), 16f, 5.4f, 7.5f, 50f, "TUNNEL CLOSED FOR REPAIRS"),
            new TunnelDef(P(RailWest, RailZ), P(-1, 0), 11f, 6.3f, 7f, 60f, null),
            new TunnelDef(P(RailEast, RailZ), P(1, 0), 11f, 7.8f, 7f, 60f, null),
        };

        public static bool InTunnel(Vector2 p, float margin = 0f)
        {
            foreach (TunnelDef t in Tunnels)
                if (t.Contains(p, margin)) return true;
            return false;
        }

        // ---- special buildings (interiors) ----

        /// <summary>Two-storey walk-up around the player's unit (the existing apartment room at the origin).</summary>
        public static readonly Rect ApartmentBuilding = Rect.MinMaxRect(-3.3f, -4.8f, 12.3f, 2.8f);
        public static readonly Rect CalderBuilding = Rect.MinMaxRect(144f, -5.5f, 168f, 18.5f);
        public static readonly Rect CoffeeShop = Rect.MinMaxRect(64f, -5.5f, 78f, 8f);
        public static readonly Rect CornerMart = Rect.MinMaxRect(112f, -5.5f, 126.5f, 7f);
        public static readonly Rect SkateShop = Rect.MinMaxRect(78.5f, -5.5f, 94f, 9f);
        public static readonly Rect BikeShop = Rect.MinMaxRect(94.5f, -5.5f, 111.5f, 9f);

        /// <summary>Paved ground (plazas, lots, the core blocks); everywhere else is the terrain's grass and dirt.</summary>
        public static readonly Rect[] Paved =
        {
            Rect.MinMaxRect(63.5f, -5.5f, 126.5f, 61.5f),   // commercial block
            Rect.MinMaxRect(143.5f, -5.5f, 206.5f, 61.5f),  // downtown
            Rect.MinMaxRect(62.5f, -55.5f, 206.5f, -18.5f), // main street, south side of Maple, and its alley
        };

        /// <summary>The residential block's lawn with the apartment building: the neighbourhood park.</summary>
        public static readonly Rect MaplePark = Rect.MinMaxRect(-36.5f, -5.5f, 46.5f, 61.5f);

        /// <summary>Everything else with walls: low-rise facades along the main streets.</summary>
        public static readonly Shell[] Shells =
        {
            // Maple Park block
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
            // Town centre (Calder is special)
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

        private static Frontage F(string street, float x0, float z0, float x1, float z1, int side, HouseTier tier) =>
            new Frontage(street, P(x0, z0), P(x1, z1), side, tier);

        /// <summary>Walkable houses (<see cref="HouseBuilder"/>): working-class south of the rail line, families on the
        /// slope north of Grove, bigger homes up on Ridge Rd, mansions on Crest Rd.</summary>
        public static readonly Frontage[] Frontages =
        {
            // Behind the Maple St shops, facing Grove
            F("GROVE ST", -250, 70, -140, 70, -1, HouseTier.Starter), F("GROVE ST", -140, 70, -45, 70, -1, HouseTier.Starter),
            // North of Grove
            F("GROVE ST", -250, 70, -140, 70, 1, HouseTier.Starter), F("GROVE ST", -140, 70, -45, 70, 1, HouseTier.Starter),
            F("GROVE ST", -45, 70, 55, 70, 1, HouseTier.Family), F("GROVE ST", 55, 70, 135, 70, 1, HouseTier.Family),
            F("BIRCH ST", -250, 160, -140, 150, -1, HouseTier.Family), F("BIRCH ST", -140, 150, -45, 160, -1, HouseTier.Family),
            F("BIRCH ST", -45, 160, 45, 168, -1, HouseTier.Family), F("BIRCH ST", 45, 168, 135, 160, -1, HouseTier.Family),
            F("BIRCH ST", -250, 160, -140, 150, 1, HouseTier.Family), F("HILLCREST DR", 25, 245, 95, 262, -1, HouseTier.Family),
            F("HILLCREST DR", 95, 262, 160, 252, -1, HouseTier.Family), F("HILLCREST DR", -25, 210, 25, 245, -1, HouseTier.Family),
            F("RIDGE RD", -180, 280, -100, 298, 1, HouseTier.Family), F("RIDGE RD", -100, 298, -20, 302, 1, HouseTier.Family),
            F("RIDGE RD", -20, 302, 60, 305, 1, HouseTier.Family), F("RIDGE RD", 60, 305, 140, 300, 1, HouseTier.Family),
            F("CREST RD", 650, 312, 595, 345, -1, HouseTier.Mansion), F("CREST RD", 595, 345, 525, 358, -1, HouseTier.Mansion),
            F("CREST RD", 615, 180, 655, 228, 1, HouseTier.Mansion),
            // South of the rail line: small houses
            F("FOUNDRY ST", -250, -160, -140, -160, 1, HouseTier.Starter), F("FOUNDRY ST", -140, -160, -45, -160, 1, HouseTier.Starter),
            F("FOUNDRY ST", -140, -160, -45, -160, -1, HouseTier.Starter), F("FOUNDRY ST", -45, -160, 55, -160, -1, HouseTier.Starter),
            F("FOUNDRY ST", -45, -160, 55, -160, 1, HouseTier.Starter), F("FOUNDRY ST", 55, -160, 135, -160, 1, HouseTier.Starter),
            F("GULL ST", -45, -215, 20, -205, -1, HouseTier.Starter), F("GULL ST", 20, -205, 80, -218, -1, HouseTier.Starter),
            F("GULL ST", 80, -218, 135, -212, -1, HouseTier.Starter),
        };

        /// <summary>District names for the map and for places you've discovered.</summary>
        public static readonly (string Name, Rect Area)[] Districts =
        {
            ("Kell Highway", Rect.MinMaxRect(-560f, -60f, -255f, 160f)),
            ("Maple Street", Rect.MinMaxRect(-255f, -60f, 135f, 66f)),
            ("Downtown", Rect.MinMaxRect(135f, -60f, 300f, 170f)),
            ("Foundry", Rect.MinMaxRect(-560f, -310f, -255f, -60f)),
            ("Southside", Rect.MinMaxRect(-255f, -250f, 300f, -60f)),
            ("Waterfront", Rect.MinMaxRect(-255f, -330f, 300f, -250f)),
            ("Canal Row", Rect.MinMaxRect(300f, -330f, 600f, 180f)),
            ("Grove Hill", Rect.MinMaxRect(-360f, 66f, 300f, 330f)),
            ("Crest", Rect.MinMaxRect(500f, 150f, 720f, 400f)),
        };
    }
}
