using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    public enum HouseTier
    {
        /// <summary>One storey: open living and kitchen, a bedroom and a bathroom.</summary>
        Starter,
        /// <summary>Two storeys: living, kitchen, bathroom downstairs; two bedrooms off a landing upstairs.</summary>
        Family,
        /// <summary>Modern two-storey villa: foyer and stairs, living wing, kitchen and dining wing, three bedrooms, a pool.</summary>
        Mansion,
    }

    /// <summary>
    /// Walkable houses, built from boxes like the apartment and the Calder Building: walls with real door and window
    /// openings, floors, stairs, roofs, a hinged front door and Kenney furniture in every room. Local frame: origin
    /// at the middle of the footprint on the ground, the front (street side) facing -z.
    /// </summary>
    public static class HouseBuilder
    {
        public const float Floor = 0.12f;
        /// <summary>Floor to floor; rooms are 2.8 m clear under a 0.2 m slab.</summary>
        public const float Storey = 3f;
        private const float Clear = 2.8f, Outer = 0.2f, Inner = 0.12f;
        private const float SillY = 0.95f, HeadY = 2.2f;

        public static Vector2 Footprint(HouseTier tier) => tier switch
        {
            HouseTier.Starter => new Vector2(11f, 8.5f),
            HouseTier.Family => new Vector2(14f, 10f),
            _ => new Vector2(24f, 16f),
        };

        private static readonly Color[] Siding =
        {
            new Color(0.86f, 0.82f, 0.72f), new Color(0.62f, 0.72f, 0.8f), new Color(0.8f, 0.66f, 0.58f),
            new Color(0.68f, 0.77f, 0.64f), new Color(0.9f, 0.89f, 0.86f), new Color(0.58f, 0.6f, 0.64f), new Color(0.88f, 0.78f, 0.55f),
        };

        private static readonly Color[] Roofs =
        {
            new Color(0.25f, 0.26f, 0.29f), new Color(0.42f, 0.28f, 0.22f), new Color(0.3f, 0.36f, 0.45f), new Color(0.35f, 0.4f, 0.33f),
        };

        /// <summary>What one house needs from the town: materials, the kit and somewhere to register its lights.</summary>
        private sealed class Ctx
        {
            public CityContext C;
            public Kit K;
            public Transform Root;
            public Material Siding, InnerWall, FloorWood, FloorTile, Ceiling, Trim, Roof, Glass, DoorLeaf;
            public HouseLights Lights;
        }

        public static void Build(CityContext c, Transform root, HouseTier tier, System.Random rng, HouseLights lights)
        {
            var x = new Ctx
            {
                C = c,
                K = c.Kit,
                Root = root,
                Siding = c.P.Lit(Siding[rng.Next(Siding.Length)], 0.08f),
                InnerWall = c.P.Lit(new Color(0.9f, 0.88f, 0.83f), 0.05f),
                FloorWood = c.P.Lit(new Color(0.5f, 0.36f, 0.24f), 0.25f),
                FloorTile = c.P.Lit(new Color(0.78f, 0.79f, 0.8f), 0.4f),
                Ceiling = c.P.Lit(new Color(0.93f, 0.92f, 0.9f)),
                Trim = c.P.Lit(new Color(0.95f, 0.95f, 0.93f), 0.1f),
                Roof = c.P.Lit(Roofs[rng.Next(Roofs.Length)], 0.1f),
                Glass = c.P.Glass(new Color(0.62f, 0.75f, 0.85f, 0.35f)),
                DoorLeaf = c.P.Lit(rng.Next(3) switch
                {
                    0 => new Color(0.55f, 0.16f, 0.14f),
                    1 => new Color(0.16f, 0.28f, 0.45f),
                    _ => new Color(0.38f, 0.26f, 0.17f),
                }, 0.3f),
                Lights = lights,
            };
            switch (tier)
            {
                case HouseTier.Starter: Starter(x); break;
                case HouseTier.Family: Family(x); break;
                default: Mansion(x); break;
            }
        }

        // ---------------------------------------------------------------- tiers

        private static void Starter(Ctx x)
        {
            const float w = 11f, d = 8.5f, hw = w / 2f, hd = d / 2f;
            float y0 = Floor, y1 = Floor + Clear;
            Slab(x, -hw, -hd, hw, hd, -0.3f, Floor, x.FloorWood);
            Shell(x, w, d, y0, y1,
                front: new[] { Opening.Door(-2f, 1f, y0, 2.15f), Window(-4.2f, 1.2f), Window(2.8f, 1.8f) },
                back: new[] { Window(-3f, 1.4f), Window(4f, 0.8f, 0.7f) },
                west: new[] { Window(-2f, 1.4f), Window(2.5f, 1.2f) },
                east: new[] { Window(-2.2f, 1.4f) });
            FrontDoor(x, -2f, -hd, 1f);
            // Partition: living and kitchen at the front, bedroom and bathroom behind.
            x.K.WallX(x.Root, "Partition", -hw + Outer, hw - Outer, 0.75f, y0, y1, Inner, x.InnerWall, Opening.Door(-1f, 0.9f, y0), Opening.Door(4f, 0.8f, y0));
            x.K.WallZ(x.Root, "Bath wall", 0.75f, hd - Outer, 2.5f, y0, y1, Inner, x.InnerWall);
            Slab(x, 2.5f, 0.8f, hw - Outer, hd - Outer, Floor, Floor + 0.005f, x.FloorTile, collider: false);
            Slab(x, -hw, -hd, hw, hd, y1, y1 + 0.2f, x.Ceiling);
            GableRoof(x, w, d, y1 + 0.2f, 2.3f);
            Light(x, new Vector3(-1f, y1 - 0.3f, -1.8f));

            // Living room and kitchen
            Put(x, "loungeSofa", -hw + 0.3f, -3.4f, -hw + 1.2f, -1f, 270f);
            Put(x, "tableCoffee", -3.4f, -2.8f, -2.6f, -1.6f, 90f, 0.4f, stretch: true);
            Put(x, "rugRectangle", -4.8f, -3.6f, -2.2f, -0.8f, 90f, 0.01f, stretch: true, solid: false);
            Put(x, "cabinetTelevision", -1.4f, -hd + 0.25f, 0.2f, -hd + 0.75f, 180f, solid: true);
            Put(x, "kitchenCabinet", 1.2f, 0.05f, 2f, 0.69f, 0f, 0.9f, stretch: true);
            Put(x, "kitchenSink", 2f, 0.05f, 2.8f, 0.69f, 0f, 0.98f, stretch: true);
            Put(x, "kitchenStoveElectric", 2.8f, 0.05f, 3.6f, 0.69f, 0f, 0.9f, stretch: true);
            Put(x, "kitchenFridge", hw - 0.9f, -0.1f, hw - 0.25f, 0.62f, 0f, 1.8f, stretch: true);
            Put(x, "tableRound", 1.6f, -2.8f, 2.8f, -1.6f, 0f, 0.75f, stretch: true);
            // Bedroom and bathroom
            Put(x, "bedDouble", -4.6f, hd - 2.45f, -2.8f, hd - 0.25f, 0f, stretch: true);
            Put(x, "cabinetBedDrawerTable", -2.6f, hd - 0.7f, -2.1f, hd - 0.25f, 0f, 0.5f, stretch: true);
            Put(x, "bookcaseClosedDoors", 0.9f, hd - 0.7f, 2.2f, hd - 0.25f, 0f);
            Put(x, "toilet", 4.4f, hd - 0.9f, 5f, hd - 0.25f, 0f);
            Put(x, "bathroomSink", 2.7f, 2.2f, 3.2f, 2.9f, 270f);
            Put(x, "shower", hw - 1.1f, 0.9f, hw - 0.25f, 1.9f, 0f);
        }

        private static void Family(Ctx x)
        {
            const float w = 14f, d = 10f, hw = w / 2f, hd = d / 2f;
            float g0 = Floor, g1 = Floor + Clear, u0 = Floor + Storey, u1 = u0 + Clear;
            Slab(x, -hw, -hd, hw, hd, -0.3f, Floor, x.FloorWood);
            // Two storeys of outer walls; the front door sits between the living room and the stairs.
            Shell(x, w, d, g0, g1,
                front: new[] { Window(-4.5f, 1.8f), Window(-1f, 1.4f), Opening.Door(3.2f, 1f, g0, 2.15f) },
                back: new[] { Window(-4f, 1.4f), Window(-0.5f, 1.4f), Window(5f, 0.8f, 0.7f) },
                west: new[] { Window(-2f, 1.4f), Window(3f, 1.2f) },
                east: new[] { Window(3.5f, 0.9f, 0.7f) });
            Shell(x, w, d, u0, u1,
                front: new[] { Window(-4.5f, 1.4f, 0f, u0), Window(-1f, 1.4f, 0f, u0), Window(5.2f, 0.9f, 0f, u0) },
                back: new[] { Window(-4.5f, 1.4f, 0f, u0), Window(-1f, 1.4f, 0f, u0) },
                west: new[] { Window(-2.5f, 1.2f, 0f, u0), Window(2.5f, 1.2f, 0f, u0) },
                east: new[] { Window(3.2f, 0.9f, 0f, u0) }, band: true);
            FrontDoor(x, 3.2f, -hd, 1f);

            // Stairs up the east wall from front to back, into a hole in the upper floor.
            const float sx0 = 5.6f, sx1 = 6.8f, sz0 = -3.4f;
            float top = Stairs(x, sx0, sx1, sz0, g0, u0);
            FloorWithHole(x, -hw, -hd, hw, hd, g1, u0, Rect.MinMaxRect(sx0 - 0.05f, sz0 - 0.2f, sx1 + 0.1f, top + 0.05f));
            // Railing along the stairwell upstairs.
            x.K.Span(x.Root, "Rail", new Vector3(sx0 - 0.1f, u0, sz0 - 0.25f), new Vector3(sx0 - 0.04f, u0 + 1f, top - 0.9f), x.Trim);
            x.K.Span(x.Root, "Rail", new Vector3(sx0 - 0.1f, u0, sz0 - 0.25f), new Vector3(sx1 + 0.12f, u0 + 1f, sz0 - 0.19f), x.Trim);

            // Ground: living at the front, kitchen and bathroom behind a partition with a wide opening.
            x.K.WallX(x.Root, "Partition", -hw + Outer, sx0 - 0.1f, 1.5f, g0, g1, Inner, x.InnerWall, new Opening(-2f, 2.4f, g0, g0 + 2.3f));
            x.K.WallZ(x.Root, "Bath wall", 1.5f, hd - Outer, 3.2f, g0, g1, Inner, x.InnerWall, Opening.Door(2.6f, 0.8f, g0));
            Slab(x, 3.2f, 1.55f, sx0 - 0.1f, hd - Outer, Floor, Floor + 0.005f, x.FloorTile, collider: false);
            Light(x, new Vector3(-2f, g1 - 0.3f, -1.8f));
            Light(x, new Vector3(-2f, g1 - 0.3f, 3.2f));

            Put(x, "loungeSofa", -hw + 0.3f, -3.2f, -hw + 1.2f, -0.2f, 270f);
            Put(x, "loungeChair", -3.2f, -hd + 0.4f, -2.2f, -hd + 1.4f, 180f);
            Put(x, "tableCoffee", -4.9f, -2.3f, -4.1f, -1.1f, 90f, 0.4f, stretch: true);
            Put(x, "rugRounded", -6f, -3.2f, -2.2f, -0.2f, 90f, 0.01f, stretch: true, solid: false);
            Put(x, "televisionModern", -0.6f, 1.2f, 0.9f, 1.4f, 0f, 0.9f);
            Put(x, "kitchenCabinetDrawer", -hw + 0.25f, 2.3f, -hw + 0.9f, 3.1f, 270f, 0.9f, stretch: true);
            Put(x, "kitchenSink", -hw + 0.25f, 3.1f, -hw + 0.9f, 3.9f, 270f, 0.98f, stretch: true);
            Put(x, "kitchenStove", -hw + 0.25f, 3.9f, -hw + 0.9f, 4.7f, 270f, 0.9f, stretch: true);
            Put(x, "kitchenFridgeLarge", -4.6f, hd - 0.85f, -3.4f, hd - 0.25f, 0f, 1.9f, stretch: true);
            Put(x, "table", -2.6f, 2.6f, -0.4f, 3.8f, 0f, 0.76f, stretch: true);
            Put(x, "chair", -2.2f, 2f, -1.8f, 2.45f, 180f);
            Put(x, "chair", -1.2f, 2f, -0.8f, 2.45f, 180f);
            Put(x, "chair", -2.2f, 3.95f, -1.8f, 4.4f, 0f);
            Put(x, "toilet", 3.4f, hd - 0.9f, 4f, hd - 0.25f, 0f);
            Put(x, "bathroomSinkSquare", 4.3f, hd - 0.7f, 4.9f, hd - 0.25f, 0f);
            Put(x, "bathtub", sx0 - 0.95f, 1.7f, sx0 - 0.15f, 3.4f, 90f);

            // Upper: a landing along the east, two bedrooms off it.
            x.K.WallZ(x.Root, "Landing wall", -hd + Outer, hd - Outer, 4.4f, u0, u1, Inner, x.InnerWall, Opening.Door(-2.5f, 0.9f, u0), Opening.Door(2.5f, 0.9f, u0));
            x.K.WallX(x.Root, "Bedroom wall", -hw + Outer, 4.4f, 0f, u0, u1, Inner, x.InnerWall);
            Slab(x, -hw, -hd, hw, hd, u1, u1 + 0.2f, x.Ceiling);
            GableRoof(x, w, d, u1 + 0.2f, 2.6f);
            Light(x, new Vector3(-1.5f, u1 - 0.3f, -2.5f));
            Light(x, new Vector3(-1.5f, u1 - 0.3f, 2.5f));
            Put(x, "bedDouble", -hw + 0.3f, -3.3f, -hw + 2.55f, -1.3f, 270f, 0f, stretch: true, y: u0);
            Put(x, "cabinetBed", -hw + 0.25f, -3.9f, -hw + 0.75f, -3.4f, 270f, 0.5f, stretch: true, y: u0);
            Put(x, "bookcaseOpen", 0.5f, -hd + 0.25f, 1.5f, -hd + 0.75f, 180f, y: u0);
            Put(x, "bedSingle", -hw + 0.3f, 2f, -hw + 2.55f, 3.2f, 270f, 0f, stretch: true, y: u0);
            Put(x, "desk", 1.2f, hd - 0.95f, 2.9f, hd - 0.25f, 0f, 0.76f, stretch: true, y: u0);
            Put(x, "chairDesk", 1.7f, hd - 1.6f, 2.4f, hd - 0.95f, 180f, y: u0);
        }

        private static void Mansion(Ctx x)
        {
            const float w = 24f, d = 16f, hw = w / 2f, hd = d / 2f;
            float g0 = Floor, g1 = Floor + Clear + 0.4f, u0 = g1 + 0.2f, u1 = u0 + Clear;
            Slab(x, -hw, -hd, hw, hd, -0.3f, Floor, x.FloorTile);
            // Big glazing: floor-to-ceiling on the garden side, wide windows at the front.
            Opening Tall(float at, float width) => new Opening(at, width, g0 + 0.05f, g0 + 2.6f);
            Shell(x, w, d, g0, g1,
                front: new[] { Window(-8f, 3f), Window(-4.5f, 1.2f), Opening.Door(0f, 1.6f, g0, 2.5f), Window(4.5f, 1.2f), Window(8f, 3f) },
                back: new[] { Tall(-8f, 4f), Tall(0f, 3f), Tall(8f, 4f) },
                west: new[] { Window(-4f, 2.4f), Window(3f, 2.4f) },
                east: new[] { Window(-4f, 2.4f), Window(3f, 2.4f) });
            Shell(x, w, d, u0, u1,
                front: new[] { Window(-8f, 3f, 0f, u0), Window(0f, 2.4f, 0f, u0), Window(8f, 3f, 0f, u0) },
                back: new[] { new Opening(-8f, 4f, u0 + 0.05f, u0 + 2.5f), new Opening(8f, 4f, u0 + 0.05f, u0 + 2.5f), Window(0f, 1.6f, 0f, u0) },
                west: new[] { Window(-4f, 2f, 0f, u0), Window(4f, 2f, 0f, u0) },
                east: new[] { Window(-4f, 2f, 0f, u0), Window(4f, 2f, 0f, u0) }, band: true);
            FrontDoor(x, 0f, -hd, 1.6f, 2.5f);
            // Entrance canopy on two columns.
            x.K.Span(x.Root, "Canopy", new Vector3(-2.6f, g0 + 3f, -hd - 3f), new Vector3(2.6f, g0 + 3.25f, -hd), x.Trim, collider: false);
            foreach (float cx in new[] { -2.2f, 2.2f })
                x.K.Box(x.Root, "Column", new Vector3(cx, g0 + 1.5f, -hd - 2.6f), new Vector3(0.35f, 3f, 0.35f), x.Trim);
            x.K.Span(x.Root, "Entrance", new Vector3(-2.6f, -0.05f, -hd - 3f), new Vector3(2.6f, Floor, -hd), x.FloorTile);

            // Foyer in the middle with the stairs; living to the west, kitchen and dining to the east.
            const float sx0 = 1.3f, sx1 = 2.7f, sz0 = -4.4f;
            float top = Stairs(x, sx0, sx1, sz0, g0, u0);
            FloorWithHole(x, -hw, -hd, hw, hd, g1, u0, Rect.MinMaxRect(sx0 - 0.05f, sz0 - 0.2f, sx1 + 0.1f, top + 0.05f));
            x.K.Span(x.Root, "Rail", new Vector3(sx0 - 0.1f, u0, sz0 - 0.25f), new Vector3(sx0 - 0.04f, u0 + 1f, top - 0.9f), x.Trim);
            x.K.Span(x.Root, "Rail", new Vector3(sx0 - 0.1f, u0, sz0 - 0.25f), new Vector3(sx1 + 0.12f, u0 + 1f, sz0 - 0.19f), x.Trim);
            x.K.WallZ(x.Root, "West wing", -hd + Outer, hd - Outer, -3.5f, g0, g1, Inner, x.InnerWall, new Opening(-3f, 2.6f, g0, g0 + 2.6f), new Opening(4f, 2.6f, g0, g0 + 2.6f));
            x.K.WallZ(x.Root, "East wing", -hd + Outer, hd - Outer, 3.5f, g0, g1, Inner, x.InnerWall, new Opening(-3f, 2.6f, g0, g0 + 2.6f), new Opening(4f, 2.6f, g0, g0 + 2.6f));
            foreach (float lx in new[] { -8f, 0f, 8f })
            foreach (float lz in new[] { -3.5f, 3.5f })
                Light(x, new Vector3(lx, g1 - 0.3f, lz));

            Put(x, "loungeDesignSofaCorner", -hw + 0.4f, -1f, -hw + 3.1f, 1.7f, 270f);
            Put(x, "loungeDesignSofa", -9f, -hd + 0.4f, -6.2f, -hd + 1.4f, 180f);
            Put(x, "tableCoffeeGlassSquare", -8.2f, -1.2f, -6.8f, 0.2f, 0f, 0.4f, stretch: true);
            Put(x, "rugRectangle", -10f, -3f, -5f, 2f, 90f, 0.01f, stretch: true, solid: false);
            Put(x, "televisionModern", -8.8f, hd - 0.45f, -6.4f, hd - 0.2f, 0f, 1.3f);
            Put(x, "speaker", -10.3f, hd - 0.7f, -9.8f, hd - 0.2f, 0f);
            Put(x, "pottedPlant", -4.4f, -hd + 0.4f, -3.9f, -hd + 0.9f, 0f, 1.3f);
            Put(x, "kitchenBar", 6f, -1f, 9.2f, -0.2f, 0f, 1f, stretch: true);
            Put(x, "stoolBar", 6.4f, -1.9f, 6.9f, -1.3f, 0f);
            Put(x, "stoolBar", 8.1f, -1.9f, 8.6f, -1.3f, 0f);
            for (float kz = -6.2f; kz < -3f; kz += 0.9f)
                Put(x, kz < -4.5f ? "kitchenCabinetDrawer" : "kitchenStoveElectric", hw - 0.9f, kz, hw - 0.25f, kz + 0.9f, 90f, 0.9f, stretch: true);
            Put(x, "kitchenFridgeBuiltIn", hw - 0.95f, -3f, hw - 0.25f, -2f, 90f, 2f, stretch: true);
            Put(x, "tableCloth", 5.6f, 3f, 9.6f, 5f, 0f, 0.76f, stretch: true);
            foreach (float cx in new[] { 6.2f, 7.6f, 9f })
            {
                Put(x, "chairModernCushion", cx - 0.22f, 2.3f, cx + 0.22f, 2.75f, 180f);
                Put(x, "chairModernCushion", cx - 0.22f, 5.25f, cx + 0.22f, 5.7f, 0f);
            }
            Put(x, "coatRackStanding", -2.9f, -hd + 0.4f, -2.3f, -hd + 1f, 0f, 1.7f);

            // Upper: bedrooms in each wing off the landing.
            x.K.WallZ(x.Root, "West wing", -hd + Outer, hd - Outer, -3.5f, u0, u1, Inner, x.InnerWall, Opening.Door(-4f, 1f, u0), Opening.Door(4f, 1f, u0));
            x.K.WallZ(x.Root, "East wing", -hd + Outer, hd - Outer, 3.5f, u0, u1, Inner, x.InnerWall, Opening.Door(3f, 1f, u0)); // past the top of the stairs, not beside the stairwell
            x.K.WallX(x.Root, "Bedroom wall", -hw + Outer, -3.5f, 0f, u0, u1, Inner, x.InnerWall);
            Slab(x, -hw - 0.3f, -hd - 0.3f, hw + 0.3f, hd + 0.3f, u1, u1 + 0.35f, x.Trim);
            // Flat roof with a parapet.
            foreach (var (a, b) in new[] { (new Vector3(-hw - 0.3f, u1 + 0.35f, -hd - 0.3f), new Vector3(hw + 0.3f, u1 + 0.8f, -hd)),
                         (new Vector3(-hw - 0.3f, u1 + 0.35f, hd), new Vector3(hw + 0.3f, u1 + 0.8f, hd + 0.3f)),
                         (new Vector3(-hw - 0.3f, u1 + 0.35f, -hd), new Vector3(-hw, u1 + 0.8f, hd)),
                         (new Vector3(hw, u1 + 0.35f, -hd), new Vector3(hw + 0.3f, u1 + 0.8f, hd)) })
                x.K.Span(x.Root, "Parapet", a, b, x.Trim, collider: false);
            foreach (float lx in new[] { -8f, 8f })
            foreach (float lz in new[] { -4f, 4f })
                Light(x, new Vector3(lx, u1 - 0.3f, lz));

            Put(x, "bedDouble", -hw + 0.3f, -5f, -hw + 2.55f, -2.9f, 270f, 0f, stretch: true, y: u0);
            Put(x, "bedDouble", -hw + 0.3f, 3f, -hw + 2.55f, 5.1f, 270f, 0f, stretch: true, y: u0);
            Put(x, "bedDouble", hw - 2.8f, -1.2f, hw - 0.3f, 1.2f, 90f, 0f, stretch: true, y: u0);
            Put(x, "cabinetBedDrawerTable", hw - 0.75f, 1.4f, hw - 0.25f, 1.9f, 90f, 0.5f, stretch: true, y: u0);
            Put(x, "cabinetBedDrawerTable", hw - 0.75f, -1.9f, hw - 0.25f, -1.4f, 90f, 0.5f, stretch: true, y: u0);
            Put(x, "loungeChairRelax", 5f, hd - 2.2f, 6.2f, hd - 0.6f, 180f, y: u0);
            Put(x, "bookcaseClosedWide", 5f, -hd + 0.25f, 6.6f, -hd + 0.75f, 180f, y: u0);
            Put(x, "desk", -8.8f, -hd + 0.25f, -7.2f, -hd + 1f, 180f, 0.76f, stretch: true, y: u0);

            // The garden: a raised pool behind the house.
            Material deck = x.C.P.Lit(new Color(0.82f, 0.79f, 0.72f), 0.1f);
            Material water = x.C.P.Lit(new Color(0.2f, 0.55f, 0.75f), 0.9f);
            float p0 = hd + 3f, p1 = hd + 9f;
            x.K.Span(x.Root, "Pool deck", new Vector3(-9f, -0.05f, hd), new Vector3(9f, 0.03f, p1 + 2f), deck, collider: false);
            x.K.Span(x.Root, "Pool rim", new Vector3(-6f, 0f, p0), new Vector3(6f, 0.45f, p0 + 0.3f), deck);
            x.K.Span(x.Root, "Pool rim", new Vector3(-6f, 0f, p1 - 0.3f), new Vector3(6f, 0.45f, p1), deck);
            x.K.Span(x.Root, "Pool rim", new Vector3(-6f, 0f, p0), new Vector3(-5.7f, 0.45f, p1), deck);
            x.K.Span(x.Root, "Pool rim", new Vector3(5.7f, 0f, p0), new Vector3(6f, 0.45f, p1), deck);
            GameObject pool = x.K.Span(x.Root, "Pool water", new Vector3(-5.7f, 0.03f, p0 + 0.3f), new Vector3(5.7f, 0.38f, p1 - 0.3f), water, collider: false);
            pool.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Put(x, "loungeChairRelax", -8.5f, p0 + 1f, -7.3f, p0 + 2.6f, 270f);
            Put(x, "loungeChairRelax", 7.3f, p0 + 1f, 8.5f, p0 + 2.6f, 90f);
        }

        // ---------------------------------------------------------------- pieces

        private static Opening Window(float at, float width, float height = 0f, float floor = Floor) =>
            height > 0f ? new Opening(at, width, floor + HeadY - height, floor + HeadY) : new Opening(at, width, floor + SillY, floor + HeadY);

        /// <summary>
        /// Four outer walls (siding outside, plaster lining inside) with their openings, glass in every window, sills
        /// and, upstairs, a band at the floor line. Openings are along each wall: x for front/back, z for the sides.
        /// </summary>
        private static void Shell(Ctx x, float w, float d, float y0, float y1, Opening[] front, Opening[] back, Opening[] west, Opening[] east, bool band = false)
        {
            float hw = w / 2f, hd = d / 2f, t = Outer;
            x.K.WallX(x.Root, "Front wall", -hw, hw, -hd + t / 2f, y0 - 0.02f, y1 + 0.2f, t, x.Siding, front);
            x.K.WallX(x.Root, "Back wall", -hw, hw, hd - t / 2f, y0 - 0.02f, y1 + 0.2f, t, x.Siding, back);
            x.K.WallZ(x.Root, "West wall", -hd + t, hd - t, -hw + t / 2f, y0 - 0.02f, y1 + 0.2f, t, x.Siding, west);
            x.K.WallZ(x.Root, "East wall", -hd + t, hd - t, hw - t / 2f, y0 - 0.02f, y1 + 0.2f, t, x.Siding, east);
            // Interior lining so rooms read as painted plaster rather than siding.
            foreach (GameObject g in x.K.WallX(x.Root, "Lining", -hw + t, hw - t, -hd + t + 0.015f, y0, y1, 0.03f, x.InnerWall, front)) Unsolid(g);
            foreach (GameObject g in x.K.WallX(x.Root, "Lining", -hw + t, hw - t, hd - t - 0.015f, y0, y1, 0.03f, x.InnerWall, back)) Unsolid(g);
            foreach (GameObject g in x.K.WallZ(x.Root, "Lining", -hd + t, hd - t, -hw + t + 0.015f, y0, y1, 0.03f, x.InnerWall, west)) Unsolid(g);
            foreach (GameObject g in x.K.WallZ(x.Root, "Lining", -hd + t, hd - t, hw - t - 0.015f, y0, y1, 0.03f, x.InnerWall, east)) Unsolid(g);

            void Glaze(Opening[] openings, System.Func<Opening, (Vector3, Vector3)> box)
            {
                foreach (Opening o in openings)
                {
                    if (o.Bottom <= y0 + 0.1f && o.Width < 1.7f) continue; // doorways (wider floor-level openings are glazed)
                    var (a, b) = box(o);
                    x.K.Pane(x.Root, "Window", a, b, new Color(0.62f, 0.75f, 0.85f, 0.35f));
                    x.K.Span(x.Root, "Sill", new Vector3(a.x - 0.08f, o.Bottom - 0.06f, a.z - 0.08f), new Vector3(b.x + 0.08f, o.Bottom, b.z + 0.08f), x.Trim, collider: false);
                }
            }
            Glaze(front, o => (new Vector3(o.Center - o.Width / 2f, o.Bottom, -hd + t / 2f - 0.02f), new Vector3(o.Center + o.Width / 2f, o.Top, -hd + t / 2f + 0.02f)));
            Glaze(back, o => (new Vector3(o.Center - o.Width / 2f, o.Bottom, hd - t / 2f - 0.02f), new Vector3(o.Center + o.Width / 2f, o.Top, hd - t / 2f + 0.02f)));
            Glaze(west, o => (new Vector3(-hw + t / 2f - 0.02f, o.Bottom, o.Center - o.Width / 2f), new Vector3(-hw + t / 2f + 0.02f, o.Top, o.Center + o.Width / 2f)));
            Glaze(east, o => (new Vector3(hw - t / 2f - 0.02f, o.Bottom, o.Center - o.Width / 2f), new Vector3(hw - t / 2f + 0.02f, o.Top, o.Center + o.Width / 2f)));
            if (band)
                x.K.Span(x.Root, "Band", new Vector3(-hw - 0.04f, y0 - 0.25f, -hd - 0.04f), new Vector3(hw + 0.04f, y0 - 0.05f, hd + 0.04f), x.Trim, collider: false);
        }

        private static void Unsolid(GameObject g)
        {
            if (g.TryGetComponent(out Collider col)) Object.Destroy(col);
        }

        private static void FrontDoor(Ctx x, float at, float z, float width, float height = 2.15f)
        {
            x.C.SwingDoor(x.Root, "front door", new Vector3(at - width / 2f, Floor, z + Outer / 2f), width, height - 0.02f, x.DoorLeaf);
            // Step and a porch light outside.
            x.K.Span(x.Root, "Step", new Vector3(at - width / 2f - 0.5f, -0.05f, z - 1.2f), new Vector3(at + width / 2f + 0.5f, Floor, z), x.Trim);
            x.K.Box(x.Root, "Porch lamp", new Vector3(at + width / 2f + 0.35f, Floor + 2.1f, z - 0.08f), new Vector3(0.16f, 0.24f, 0.12f),
                x.C.P.Lamp(new Color(0.7f, 0.68f, 0.6f), new Color(1f, 0.85f, 0.6f)), collider: false);
        }

        private static void Slab(Ctx x, float x0, float z0, float x1, float z1, float y0, float y1, Material m, bool collider = true) =>
            x.K.Span(x.Root, "Slab", new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), m, collider);

        /// <summary>The upper floor (ceiling below, boards on top) around a stairwell hole.</summary>
        private static void FloorWithHole(Ctx x, float x0, float z0, float x1, float z1, float y0, float y1, Rect hole)
        {
            void Piece(float a0, float b0, float a1, float b1)
            {
                if (a1 - a0 < 0.01f || b1 - b0 < 0.01f) return;
                x.K.Span(x.Root, "Ceiling", new Vector3(a0, y0, b0), new Vector3(a1, y1 - 0.03f, b1), x.Ceiling);
                x.K.Span(x.Root, "Boards", new Vector3(a0, y1 - 0.03f, b0), new Vector3(a1, y1, b1), x.FloorWood, collider: false);
            }
            Piece(x0, z0, x1, hole.yMin);
            Piece(x0, hole.yMax, x1, z1);
            Piece(x0, hole.yMin, hole.xMin, hole.yMax);
            Piece(hole.xMax, hole.yMin, x1, hole.yMax);
        }

        /// <summary>A straight flight climbing +z from <paramref name="z0"/>; returns where the top step ends.</summary>
        private static float Stairs(Ctx x, float x0, float x1, float z0, float from, float to)
        {
            int steps = Mathf.CeilToInt((to - from) / 0.19f);
            float rise = (to - from) / steps, run = 0.28f;
            for (int i = 0; i < steps; i++)
                x.K.Span(x.Root, "Step", new Vector3(x0, from - 0.05f, z0 + i * run), new Vector3(x1, from + rise * (i + 1), z0 + (i + 1) * run), x.FloorWood);
            return z0 + steps * run;
        }

        /// <summary>A pitched roof over the footprint (ridge along x) with gable ends in the siding colour.</summary>
        private static void GableRoof(Ctx x, float w, float d, float y, float height)
        {
            const float o = 0.45f;
            float hw = w / 2f + o, hd = d / 2f + o;
            var verts = new List<Vector3>();
            var roof = new List<int>();
            var gable = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 e, List<int> into)
            {
                int i = verts.Count;
                verts.AddRange(new[] { a, b, c, e });
                into.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            void Tri(Vector3 a, Vector3 b, Vector3 c, List<int> into)
            {
                int i = verts.Count;
                verts.AddRange(new[] { a, b, c });
                into.AddRange(new[] { i, i + 1, i + 2 });
            }
            Vector3 ridgeW = new Vector3(-hw, y + height, 0f), ridgeE = new Vector3(hw, y + height, 0f);
            // Slopes (outside and underside), then the gable triangles on the walls' planes.
            Quad(new Vector3(-hw, y, -hd), ridgeW, ridgeE, new Vector3(hw, y, -hd), roof);
            Quad(new Vector3(hw, y, hd), ridgeE, ridgeW, new Vector3(-hw, y, hd), roof);
            Quad(new Vector3(hw, y, -hd), ridgeE, ridgeW, new Vector3(-hw, y, -hd), roof);
            Quad(new Vector3(-hw, y, hd), ridgeW, ridgeE, new Vector3(hw, y, hd), roof);
            float gx = w / 2f, gz = d / 2f, gh = height * (gz / hd);
            Tri(new Vector3(-gx, y, gz), new Vector3(-gx, y + gh, 0f), new Vector3(-gx, y, -gz), gable);
            Tri(new Vector3(gx, y, -gz), new Vector3(gx, y + gh, 0f), new Vector3(gx, y, gz), gable);

            var mesh = new Mesh { name = "Gable roof" };
            mesh.SetVertices(verts);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(roof, 0);
            mesh.SetTriangles(gable, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Roof");
            go.transform.SetParent(x.Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { x.Roof, x.Siding };
            // Fascia boards along the eaves give the roof an edge instead of a paper-thin sheet.
            x.K.Span(x.Root, "Fascia", new Vector3(-hw, y - 0.22f, -hd - 0.03f), new Vector3(hw, y + 0.04f, -hd + 0.1f), x.Trim, collider: false);
            x.K.Span(x.Root, "Fascia", new Vector3(-hw, y - 0.22f, hd - 0.1f), new Vector3(hw, y + 0.04f, hd + 0.03f), x.Trim, collider: false);
        }

        private static void Light(Ctx x, Vector3 local)
        {
            Light light = x.C.PointLight(x.Root, local, 7f, 0.8f, new Color(1f, 0.9f, 0.75f));
            x.Lights.Add(light);
            x.K.Box(x.Root, "Ceiling lamp", local + new Vector3(0f, 0.22f, 0f), new Vector3(0.4f, 0.05f, 0.4f), x.C.P.Unlit(new Color(1f, 0.96f, 0.88f)), collider: false);
        }

        /// <summary>
        /// A furniture model into a box given by its x/z corners (house-local), resting on <paramref name="y"/>.
        /// <paramref name="height"/> 0 keeps the kit's proportions. Solid unless it's a rug.
        /// </summary>
        private static void Put(Ctx x, string model, float x0, float z0, float x1, float z1, float yaw, float height = 0f,
            bool stretch = false, bool solid = true, float y = Floor)
        {
            GameObject go = x.K.Fit(x.Root, model, new Vector3((x0 + x1) / 2f, y, (z0 + z1) / 2f), new Vector3(x1 - x0, height, z1 - z0), yaw, stretch);
            if (go != null && solid) x.K.Solid(go);
        }
    }

    /// <summary>
    /// House interior lights: dozens of houses would mean hundreds of lights, so only those near the player are on.
    /// Checked a few times a second.
    /// </summary>
    public sealed class HouseLights : MonoBehaviour
    {
        private const float Radius = 32f;
        private readonly List<Light> _lights = new List<Light>();
        private Transform _player;
        private float _next;

        public void Configure(Transform player) => _player = player;

        public void Add(Light light)
        {
            light.enabled = false;
            _lights.Add(light);
        }

        private void Update()
        {
            if (_player == null || Time.time < _next) return;
            _next = Time.time + 0.3f;
            Vector3 p = _player.position;
            foreach (Light l in _lights)
                l.enabled = (l.transform.position - p).sqrMagnitude < Radius * Radius;
        }
    }
}
