using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The Foundry (TOWN_SPEC A6): the grittiest part of town. Corrugated sheds with roll-up doors and loading
    /// docks, a freight depot you can walk into, the building supply yard, self-storage, the salvage yard, stacked
    /// containers, the abandoned cannery behind a gap in its fence, cracked and patched yards, pallets, barrels,
    /// electrical boxes, pipes, graffiti, and utility poles with sagging wires along the industrial roads.
    /// </summary>
    public static class Industrial
    {
        public static readonly Rect SupplyYard = Rect.MinMaxRect(-350f, -150f, -300f, -86f);
        public static readonly Rect Salvage = Rect.MinMaxRect(-462f, -222f, -400f, -168f);
        public static readonly Rect Freight = Rect.MinMaxRect(-392f, -262f, -304f, -168f);
        public static readonly Rect Storage = Rect.MinMaxRect(-298f, -225f, -262f, -168f);
        public static readonly Rect Cannery = Rect.MinMaxRect(-556f, -190f, -484f, -100f);
        public static readonly Rect TruckYard = Rect.MinMaxRect(-460f, -93f, -405f, -84f);
        public static readonly Rect Terminal = Rect.MinMaxRect(-462f, -300f, -262f, -280f);

        public static void AddPads(CityContext c)
        {
            foreach (Rect r in new[] { SupplyYard, Salvage, Freight, Storage, Cannery, TruckYard, Terminal })
                c.Pads.Add(Pad.FromRect(r, 0f));
        }

        private static Texture2D _corrugated, _ribs, _chain;

        private static Texture2D Stripes(string name, int size, System.Func<int, int, float> shade)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float s = shade(x, y);
                px[y * size + x] = new Color(s, s, s, 1f);
            }
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        private static Material Corrugated(CityContext c, Color tint)
        {
            _corrugated ??= Stripes("corrugated", 64, (x, y) => 0.78f + 0.22f * Mathf.Cos(x / 64f * Mathf.PI * 2f * 4f));
            return c.P.Textured("corrugated", _corrugated, tint, 0.25f);
        }

        private static Material Ribbed(CityContext c, Color tint)
        {
            _ribs ??= Stripes("container ribs", 64, (x, y) => (x / 8) % 2 == 0 ? 0.85f : 1f);
            return c.P.Textured("ribs", _ribs, tint, 0.2f);
        }

        /// <summary>Chain-link: a diamond mesh texture with alpha, drawn alpha-tested.</summary>
        private static Material ChainLink(CityContext c)
        {
            if (_chain == null)
            {
                const int size = 64;
                _chain = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "chain link", wrapMode = TextureWrapMode.Repeat };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int a = (x + y) % 32, b = (x - y + 64) % 32;
                    bool wire = a < 3 || b < 3;
                    px[y * size + x] = wire ? new Color32(150, 152, 150, 255) : new Color32(0, 0, 0, 0);
                }
                _chain.SetPixels32(px);
                _chain.Apply(true, true);
            }
            Material m = c.P.Textured("chainlink", _chain, Color.white, 0.4f);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f);
            return m;
        }

        public static void Build(CityContext c)
        {
            Transform root = Kit.Group(c.Static, "Foundry");
            var rng = new System.Random(6061);
            BuildingSupply(c, root, rng);
            FreightDepot(c, root, rng);
            SelfStorage(c, root);
            SalvageYard(c, root, rng);
            CanneryRuin(c, root, rng);
            TruckYardAndTerminal(c, root, rng);
            UtilityLines(c, root);
        }

        // ---- building blocks ----

        /// <summary>
        /// A corrugated shed in a local frame (front wall along local x at z 0, facing -z): walls, a low-pitched roof,
        /// roll-up doors across the front (open ones leave a hole), optional person door.
        /// </summary>
        private static Transform Shed(CityContext c, Transform parent, string name, Vector3 at, float yaw, float w, float d, float h, Color colour,
            (float X, float Width, bool Open)[] doors, float personDoorX = float.NaN)
        {
            Kit k = c.Kit;
            Transform g = Kit.Group(parent, name, at, yaw);
            var walls = new MeshBuilder();
            float hw = w / 2f;
            // Front: pieces between the doors, plus lintels above them.
            var cuts = new List<(float A, float B)>();
            foreach (var dr in doors) cuts.Add((dr.X - dr.Width / 2f, dr.X + dr.Width / 2f));
            if (!float.IsNaN(personDoorX)) cuts.Add((personDoorX - 0.55f, personDoorX + 0.55f));
            cuts.Sort((p, q) => p.A.CompareTo(q.A));
            float x = -hw;
            foreach (var (a, b) in cuts)
            {
                if (a > x) walls.Panel(new Vector3(x, 0f, 0f), new Vector3(a, 0f, 0f), h);
                float top = b - a > 1.5f ? 4.2f : 2.3f;
                walls.Panel(new Vector3(a, top, 0f), new Vector3(b, top, 0f), h - top);
                x = b;
            }
            if (x < hw) walls.Panel(new Vector3(x, 0f, 0f), new Vector3(hw, 0f, 0f), h);
            walls.Panel(new Vector3(hw, 0f, d), new Vector3(-hw, 0f, d), h);
            walls.Panel(new Vector3(-hw, 0f, d), new Vector3(-hw, 0f, 0f), h);
            walls.Panel(new Vector3(hw, 0f, 0f), new Vector3(hw, 0f, d), h);
            // Inside faces (so interiors aren't see-through from within).
            walls.Panel(new Vector3(-hw + 0.1f, 0f, d - 0.1f), new Vector3(hw - 0.1f, 0f, d - 0.1f), h);
            walls.Panel(new Vector3(-hw + 0.1f, 0f, 0.1f), new Vector3(-hw + 0.1f, 0f, d - 0.1f), h);
            walls.Panel(new Vector3(hw - 0.1f, 0f, d - 0.1f), new Vector3(hw - 0.1f, 0f, 0.1f), h);
            walls.Build(g, "Walls", Corrugated(c, colour), collider: false);
            // Solid walls as boxes (cheap colliders), leaving the door openings.
            x = -hw;
            foreach (var (a, b) in cuts)
            {
                if (a > x) k.Span(g, "Wall collider", new Vector3(x, 0f, -0.05f), new Vector3(a, h, 0.15f), null, collider: true).GetComponent<Renderer>().enabled = false;
                x = b;
            }
            if (x < hw) k.Span(g, "Wall collider", new Vector3(x, 0f, -0.05f), new Vector3(hw, h, 0.15f), null, collider: true).GetComponent<Renderer>().enabled = false;
            foreach (var (from, to) in new[] { (new Vector3(-hw, 0f, d - 0.15f), new Vector3(hw, h, d + 0.05f)), (new Vector3(-hw - 0.05f, 0f, 0f), new Vector3(-hw + 0.15f, h, d)), (new Vector3(hw - 0.15f, 0f, 0f), new Vector3(hw + 0.05f, h, d)) })
                k.Span(g, "Wall collider", from, to, null, collider: true).GetComponent<Renderer>().enabled = false;
            Material roof = c.P.Lit(new Color(0.34f, 0.34f, 0.35f), 0.2f);
            k.Span(g, "Roof", new Vector3(-hw - 0.3f, h, -0.3f), new Vector3(hw + 0.3f, h + 0.35f, d + 0.3f), roof);
            k.Span(g, "Floor", new Vector3(-hw, 0f, 0f), new Vector3(hw, 0.03f, d), c.P.Lit(new Color(0.45f, 0.45f, 0.44f), 0.1f));
            Material door = Corrugated(c, new Color(0.62f, 0.64f, 0.66f));
            foreach (var (dx, dw, open) in doors)
            {
                if (open) k.Box(g, "Rolled door", new Vector3(dx, 4.35f, 0.3f), new Vector3(dw, 0.3f, 0.5f), door, collider: false);
                else k.Span(g, "Roll-up door", new Vector3(dx - dw / 2f, 0f, 0.02f), new Vector3(dx + dw / 2f, 4.2f, 0.12f), door);
            }
            // Roof clutter: vents and an AC unit.
            for (int i = 0; i < 3; i++)
                k.Box(g, "Roof vent", new Vector3(-hw + w * (i + 1) / 4f, h + 0.7f, d * 0.5f), new Vector3(0.9f, 0.7f, 0.9f), c.P.Lit(new Color(0.55f, 0.56f, 0.57f), 0.4f), collider: false);
            return g;
        }

        internal static void Pallets(CityContext c, Transform parent, Vector3 at, int count, System.Random rng)
        {
            Material wood = c.P.Lit(new Color(0.6f, 0.48f, 0.32f), 0.05f);
            for (int i = 0; i < count; i++)
                c.Kit.Box(parent, "Pallet", at + new Vector3((i % 3) * 1.3f, 0.07f + (i / 3) * 0.15f, 0f), new Vector3(1.2f, 0.14f, 1f), wood, collider: i < 3);
            if (rng.NextDouble() < 0.6)
                c.Kit.Box(parent, "Crate", at + new Vector3(0f, 0.6f + (count / 3) * 0.15f, 0f), new Vector3(1f, 1f, 0.9f), c.P.Lit(new Color(0.55f, 0.42f, 0.28f)), collider: true);
        }

        private static void Barrels(CityContext c, Transform parent, Vector3 at, int count, System.Random rng)
        {
            Color[] paints = { new Color(0.15f, 0.3f, 0.55f), new Color(0.65f, 0.15f, 0.1f), new Color(0.25f, 0.4f, 0.2f), new Color(0.5f, 0.5f, 0.5f) };
            for (int i = 0; i < count; i++)
                c.Kit.Cylinder(parent, "Barrel", at + new Vector3((i % 2) * 0.65f, 0.45f, (i / 2) * 0.65f), 0.58f, 0.9f,
                    c.P.Lit(paints[rng.Next(paints.Length)], 0.3f), collider: true);
        }

        /// <summary>A shipping container (12 or 6 m), ribbed, doors at one end.</summary>
        private static void Container(CityContext c, Transform parent, Vector3 at, float yaw, bool forty, Color colour)
        {
            Transform g = Kit.Group(parent, "Container", at, yaw);
            float len = forty ? 12.2f : 6.1f;
            c.Kit.Box(g, "Box", new Vector3(0f, 1.3f, 0f), new Vector3(2.44f, 2.6f, len), Ribbed(c, colour));
            c.Kit.Box(g, "Doors", new Vector3(0f, 1.3f, len / 2f + 0.01f), new Vector3(2.3f, 2.45f, 0.03f), c.P.Lit(colour * 0.85f, 0.2f), collider: false);
        }

        private static readonly Color[] ContainerColours =
        {
            new Color(0.62f, 0.15f, 0.1f), new Color(0.12f, 0.28f, 0.52f), new Color(0.15f, 0.4f, 0.25f), new Color(0.85f, 0.45f, 0.12f),
            new Color(0.55f, 0.56f, 0.58f), new Color(0.88f, 0.88f, 0.86f), new Color(0.45f, 0.25f, 0.15f),
        };

        private static void Fence(CityContext c, Transform parent, Vector3 a, Vector3 b, float height = 2.4f, float gapAt = -1f)
        {
            Kit k = c.Kit;
            Material post = c.P.Lit(new Color(0.5f, 0.52f, 0.53f), 0.5f);
            float len = Vector3.Distance(a, b);
            Vector3 dir = (b - a).normalized;
            var mesh = new MeshBuilder();
            for (float s = 0f; s < len; s += 3f)
            {
                float e = Mathf.Min(len, s + 3f);
                bool gap = gapAt >= 0f && s <= gapAt && gapAt < e; // a panel's missing: a way in
                k.Cylinder(parent, "Fence post", a + dir * s + Vector3.up * height / 2f, 0.07f, height, post, collider: true);
                if (gap) continue;
                mesh.Panel(a + dir * s, a + dir * e, height, 0.5f);
                mesh.Panel(a + dir * e, a + dir * s, height, 0.5f);
                var box = new GameObject("Fence collider").AddComponent<BoxCollider>();
                box.transform.SetParent(parent, false);
                box.transform.position = a + dir * (s + e) / 2f + Vector3.up * height / 2f;
                box.transform.rotation = Quaternion.LookRotation(dir);
                box.size = new Vector3(0.05f, height, e - s);
            }
            mesh.Build(parent, "Chain-link", ChainLink(c), collider: false);
        }

        internal static void Graffiti(CityContext c, Transform parent, Vector3 at, float yaw, System.Random rng)
        {
            string[] tags = { "KELL CREW", "RUST", "NO WAY", "SK8", "WHY", "DOC", "ZERO", "FOUNDRY 4 LIFE", "B00M", "SAGE" };
            Color[] spray = { new Color(0.9f, 0.2f, 0.5f), new Color(0.2f, 0.8f, 0.9f), new Color(1f, 0.85f, 0.2f), new Color(0.3f, 0.9f, 0.3f), Color.white, new Color(0.9f, 0.4f, 0.1f) };
            TextMesh t = c.Kit.Text(parent, tags[rng.Next(tags.Length)], at, yaw, 0.35f + (float)rng.NextDouble() * 0.4f, spray[rng.Next(spray.Length)]);
            t.transform.localRotation *= Quaternion.Euler(0f, 0f, (float)(rng.NextDouble() - 0.5) * 16f);
        }

        internal static void ElectricalBox(CityContext c, Transform parent, Vector3 at, float yaw)
        {
            Transform g = Kit.Group(parent, "Electrical box", at, yaw);
            c.Kit.Box(g, "Cabinet", new Vector3(0f, 0.75f, 0f), new Vector3(1.1f, 1.5f, 0.5f), c.P.Lit(new Color(0.3f, 0.42f, 0.34f), 0.3f));
            c.Kit.Box(g, "Warning", new Vector3(0f, 1.1f, -0.26f), new Vector3(0.3f, 0.3f, 0.01f), c.P.Lit(new Color(0.95f, 0.8f, 0.15f)), collider: false);
        }

        private static void Pipes(CityContext c, Transform parent, Vector3 from, Vector3 to, float y)
        {
            Material m = c.P.Lit(new Color(0.45f, 0.42f, 0.38f), 0.4f);
            Vector3 mid = (from + to) / 2f + Vector3.up * y;
            GameObject p = c.Kit.Cylinder(parent, "Pipe", mid, 0.3f, Vector3.Distance(from, to), m);
            p.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
        }

        private static void Yard(CityContext c, Transform parent, Rect r, System.Random rng)
        {
            // Cracked concrete with darker repair patches.
            c.Kit.Span(parent, "Yard", new Vector3(r.xMin, -0.05f, r.yMin), new Vector3(r.xMax, 0.008f, r.yMax), c.P.Lit(new Color(0.42f, 0.41f, 0.39f), 0.06f))
                .AddComponent<SurfaceTag>().Roughness = 0.4f;
            Material patch = c.P.Lit(new Color(0.3f, 0.3f, 0.3f), 0.06f);
            for (int i = 0; i < (int)(r.width * r.height / 180f); i++)
            {
                var p = new Vector2(Mathf.Lerp(r.xMin + 2f, r.xMax - 2f, (float)rng.NextDouble()), Mathf.Lerp(r.yMin + 2f, r.yMax - 2f, (float)rng.NextDouble()));
                c.Kit.Decal(parent, "Patch", new Vector3(p.x, 0.011f, p.y), new Vector2(1f + (float)rng.NextDouble() * 3f, 0.8f + (float)rng.NextDouble() * 2f), (float)rng.NextDouble() * 180f, patch);
            }
        }

        // ---- the places ----

        private static void BuildingSupply(CityContext c, Transform root, System.Random rng)
        {
            Rect r = SupplyYard;
            Yard(c, root, r, rng);
            // Warehouse at the south end of the yard, its doors facing north onto the lumber stacks and Yard Rd.
            Transform shed = Shed(c, root, "Cascade Building Supply", new Vector3(r.center.x, 0f, -96f), 180f, 46f, 34f, 9f, new Color(0.75f, 0.55f, 0.3f),
                new[] { (-10f, 5f, true), (8f, 5f, true) }, personDoorX: -18f);
            c.Kit.Span(shed, "Sign band", new Vector3(-20f, 6f, -0.2f), new Vector3(8f, 7.6f, -0.05f), c.P.Lit(new Color(0.15f, 0.3f, 0.2f)), collider: false);
            c.Kit.Text(shed, "CASCADE BUILDING SUPPLY", new Vector3(-6f, 6.8f, -0.22f), 0f, 0.45f, Color.white);
            c.Kit.Text(shed, "LUMBER · CONCRETE · TOOLS   7 AM – 5 PM", new Vector3(-6f, 5.4f, -0.22f), 0f, 0.14f, Color.white);
            // Racks of stock inside.
            Material rack = c.P.Lit(new Color(0.85f, 0.45f, 0.1f), 0.3f);
            for (float x = -18f; x < 20f; x += 6f)
            {
                c.Kit.Span(shed, "Rack", new Vector3(x - 0.6f, 0f, 12f), new Vector3(x + 0.6f, 0.08f, 30f), rack);
                for (float yy = 0.1f; yy < 6f; yy += 2f)
                {
                    c.Kit.Span(shed, "Rack beam", new Vector3(x - 0.6f, yy + 1.9f, 12f), new Vector3(x + 0.6f, yy + 2f, 30f), rack, collider: false);
                    for (float z = 13f; z < 29f; z += 2f)
                        c.Kit.Box(shed, "Stock", new Vector3(x, yy + 0.5f, z), new Vector3(1f, 0.9f, 1.4f), c.P.Lit(new Color(0.62f, 0.5f, 0.34f)), collider: false);
                }
            }
            Businesses.Build(c, new Business
            {
                Name = "Cascade Supply Counter", Trade = Trade.Hardware, Front = (Vector2)ToWorld(shed, new Vector3(-12f, 0f, 3f)), Inward = Flat(shed.forward),
                Width = 10f, Depth = 6f, Brand = new Color(0.15f, 0.3f, 0.2f), Style = FacadeStyle.Concrete,
            }, 9201);
            // Lumber stacks and pallets in the yard.
            Material lumber = c.P.Lit(new Color(0.78f, 0.65f, 0.45f), 0.05f);
            for (int i = 0; i < 6; i++)
                c.Kit.Box(root, "Lumber", new Vector3(r.xMin + 5f + i * 7f, 0.6f, -90f), new Vector3(5f, 1.2f, 2.4f), lumber);
            Pallets(c, root, new Vector3(r.xMax - 6f, 0f, -93f), 7, rng);
            Fence(c, root, new Vector3(r.xMin, 0f, r.yMax), new Vector3(r.xMin, 0f, r.yMin));
        }

        private static void FreightDepot(CityContext c, Transform root, System.Random rng)
        {
            Rect r = Freight;
            Yard(c, root, r, rng);
            // Long depot, docks on the south side facing the truck yard; walk in by the office door.
            Transform shed = Shed(c, root, "Port Kell Freight", new Vector3(r.center.x, 0f, -216f), 0f, 84f, 40f, 11f, new Color(0.55f, 0.6f, 0.66f),
                new[] { (-30f, 4f, false), (-18f, 4f, true), (-6f, 4f, false), (6f, 4f, false), (18f, 4f, true), (30f, 4f, false) }, personDoorX: -38f);
            c.Kit.Text(shed, "PORT KELL FREIGHT", new Vector3(0f, 9.4f, -0.05f), 0f, 0.9f, new Color(0.15f, 0.2f, 0.3f));
            Material dock = c.P.Lit(new Color(0.5f, 0.5f, 0.48f), 0.08f);
            c.Kit.Span(shed, "Loading dock", new Vector3(-36f, 0f, -3f), new Vector3(36f, 1.2f, 0f), dock);
            foreach (float x in new[] { -30f, -18f, -6f, 6f, 18f, 30f })
            {
                c.Kit.Box(shed, "Dock bumper", new Vector3(x - 1.6f, 0.9f, -3.05f), new Vector3(0.3f, 0.4f, 0.1f), c.P.Lit(new Color(0.08f, 0.08f, 0.08f)), collider: false);
                c.Kit.Box(shed, "Dock bumper", new Vector3(x + 1.6f, 0.9f, -3.05f), new Vector3(0.3f, 0.4f, 0.1f), c.P.Lit(new Color(0.08f, 0.08f, 0.08f)), collider: false);
            }
            // Two trailers backed up to docks.
            foreach (float x in new[] { -30f, 6f })
                Trailer(c, shed, new Vector3(x, 0f, -11f), 0f);
            c.Kit.Span(shed, "Dock steps", new Vector3(-39f, 0f, -3f), new Vector3(-37f, 0.6f, -1.5f), dock);
            // Inside: pallet racking, a glassed office in the corner, a forklift.
            Material rack = c.P.Lit(new Color(0.2f, 0.35f, 0.65f), 0.3f);
            for (float x = -32f; x < 36f; x += 8f)
                for (float yy = 0f; yy < 8f; yy += 2.6f)
                {
                    c.Kit.Span(shed, "Racking", new Vector3(x - 1f, yy + 2.4f, 10f), new Vector3(x + 1f, yy + 2.5f, 36f), rack, collider: false);
                    for (float z = 11f; z < 35f; z += 2.2f)
                        if (rng.NextDouble() < 0.7)
                            c.Kit.Box(shed, "Pallet load", new Vector3(x, yy + 0.8f, z), new Vector3(1.6f, 1.4f, 1.6f), c.P.Lit(new Color(0.62f, 0.52f, 0.36f)), collider: yy < 1f);
                }
            c.Kit.Span(shed, "Office", new Vector3(-41f, 0f, 1f), new Vector3(-34f, 3f, 8f), c.P.Lit(new Color(0.8f, 0.8f, 0.78f)));
            c.Kit.Pane(shed, "Office window", new Vector3(-34.02f, 1f, 2f), new Vector3(-33.98f, 2.4f, 7f), new Color(0.6f, 0.7f, 0.75f, 0.4f));
            c.Kit.Box(shed, "Forklift", new Vector3(-10f, 1.1f, 6f), new Vector3(1.2f, 2.2f, 2.4f), c.P.Lit(new Color(0.95f, 0.75f, 0.1f), 0.3f));
            c.PointLight(shed, new Vector3(-20f, 9f, 20f), 18f, 1f, new Color(0.95f, 0.97f, 1f));
            c.PointLight(shed, new Vector3(20f, 9f, 20f), 18f, 1f, new Color(0.95f, 0.97f, 1f));
            c.Place(ToWorld(shed, new Vector3(-38f, 0f, -4f)), PlaceKind.Door, "Port Kell Freight");
            Graffiti(c, shed, new Vector3(41.9f, 2f, 20f), 270f, rng);
            ElectricalBox(c, root, new Vector3(r.xMin + 2f, 0f, r.yMax - 3f), 90f);
            Pipes(c, shed, new Vector3(-42f, 0f, 40.3f), new Vector3(42f, 0f, 40.3f), 7f);
        }

        private static void Trailer(CityContext c, Transform parent, Vector3 at, float yaw)
        {
            Transform t = Kit.Group(parent, "Truck trailer", at, yaw);
            c.Kit.Box(t, "Box", new Vector3(0f, 2.55f, 0f), new Vector3(2.5f, 2.9f, 13f), Ribbed(c, new Color(0.9f, 0.9f, 0.88f)));
            c.Kit.Box(t, "Chassis", new Vector3(0f, 0.9f, 0f), new Vector3(2.2f, 0.4f, 12.5f), c.P.Lit(new Color(0.15f, 0.15f, 0.16f)), collider: false);
            foreach (float z in new[] { -4.5f, -3.2f })
            foreach (float x in new[] { -1f, 1f })
                c.Kit.Cylinder(t, "Wheel", new Vector3(x, 0.5f, z), 1f, 0.35f, c.P.Lit(new Color(0.08f, 0.08f, 0.08f))).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            c.Kit.Box(t, "Landing gear", new Vector3(0f, 0.55f, 4f), new Vector3(1.8f, 1.1f, 0.2f), c.P.Lit(new Color(0.3f, 0.3f, 0.3f)), collider: false);
        }

        private static void SelfStorage(CityContext c, Transform root)
        {
            Rect r = Storage;
            Yard(c, root, r, new System.Random(8));
            Color[] doors = { new Color(0.85f, 0.45f, 0.1f), new Color(0.2f, 0.45f, 0.75f) };
            for (int row = 0; row < 3; row++)
            {
                float x = r.xMin + 4f + row * 12f;
                Transform g = Kit.Group(root, "Storage row", new Vector3(x, 0f, r.center.y));
                c.Kit.Span(g, "Units", new Vector3(-2.5f, 0f, -24f), new Vector3(2.5f, 3f, 24f), c.P.Lit(new Color(0.8f, 0.78f, 0.72f)));
                c.Kit.Span(g, "Roof", new Vector3(-2.8f, 3f, -24.3f), new Vector3(2.8f, 3.2f, 24.3f), c.P.Lit(new Color(0.35f, 0.35f, 0.36f)));
                for (float z = -22f; z < 22f; z += 4f)
                    foreach (float side in new[] { -1f, 1f })
                        c.Kit.Box(g, "Unit door", new Vector3(side * 2.52f, 1.3f, z), new Vector3(0.04f, 2.4f, 3f), Corrugated(c, doors[row % 2]), collider: false);
            }
            c.Kit.Text(root, "KELL SELF STORAGE", new Vector3(r.xMax - 0.1f, 3.4f, r.center.y), 270f, 0.5f, new Color(0.85f, 0.45f, 0.1f));
            Fence(c, root, new Vector3(r.xMin, 0f, r.yMin), new Vector3(r.xMax, 0f, r.yMin));
        }

        private static void SalvageYard(CityContext c, Transform root, System.Random rng)
        {
            Rect r = Salvage;
            Yard(c, root, r, rng);
            c.Kit.Span(root, "Mud", new Vector3(r.xMin, 0.009f, r.yMin), new Vector3(r.xMax, 0.012f, r.yMax), c.P.Lit(new Color(0.33f, 0.28f, 0.22f), 0.02f), collider: false);
            // Rusted wrecks piled two and three high.
            var library = c.Game.VehicleLibrary;
            Material rust = c.P.Lit(new Color(0.42f, 0.24f, 0.14f), 0.05f);
            Material rustDark = c.P.Lit(new Color(0.3f, 0.2f, 0.14f), 0.05f);
            if (library != null && library.TrafficMix.Count > 0)
                for (float x = r.xMin + 6f; x < r.xMax - 5f; x += 8f)
                for (float z = r.yMin + 12f; z < r.yMax - 6f; z += 9f)
                {
                    int stack = 1 + rng.Next(3);
                    for (int s = 0; s < stack; s++)
                    {
                        GameObject mesh = library.CarMesh(library.TrafficMix[rng.Next(library.TrafficMix.Count)]);
                        if (mesh == null) continue;
                        GameObject wreck = Object.Instantiate(mesh, root, false);
                        wreck.name = "Wreck";
                        wreck.transform.localPosition = new Vector3(x + (float)(rng.NextDouble() - 0.5), s * 1.2f, z);
                        wreck.transform.localRotation = Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 8f, (float)rng.NextDouble() * 360f, (float)(rng.NextDouble() - 0.5) * 10f);
                        wreck.transform.localScale = new Vector3(CarFactory.Scale, CarFactory.Scale * 0.7f, CarFactory.Scale); // crushed a bit
                        foreach (Renderer rend in wreck.GetComponentsInChildren<Renderer>())
                        {
                            var mats = new Material[rend.sharedMaterials.Length];
                            for (int i = 0; i < mats.Length; i++) mats[i] = rng.Next(2) == 0 ? rust : rustDark;
                            rend.sharedMaterials = mats;
                        }
                        var box = wreck.AddComponent<BoxCollider>();
                        box.center = new Vector3(0f, 0.5f, 0f);
                        box.size = new Vector3(1.4f, 1f, 3.2f);
                    }
                }
            // A crane with a magnet, an office trailer, the sign.
            Material yellow = c.P.Lit(new Color(0.9f, 0.7f, 0.1f), 0.3f);
            Vector3 crane = new Vector3(r.xMax - 8f, 0f, r.yMin + 6f);
            c.Kit.Box(root, "Crane base", crane + new Vector3(0f, 1.2f, 0f), new Vector3(3f, 2.4f, 4f), yellow);
            GameObject arm = c.Kit.Box(root, "Crane arm", crane + new Vector3(-4f, 7f, 0f), new Vector3(0.6f, 12f, 0.6f), yellow);
            arm.transform.localRotation = Quaternion.Euler(0f, 0f, 35f);
            c.Kit.Cylinder(root, "Magnet", crane + new Vector3(-8.5f, 6f, 0f), 1.6f, 0.4f, c.P.Lit(new Color(0.15f, 0.15f, 0.16f)));
            c.Kit.Span(root, "Office trailer", new Vector3(r.xMax - 16f, 0f, r.yMax - 6f), new Vector3(r.xMax - 4f, 3f, r.yMax - 2f), c.P.Lit(new Color(0.85f, 0.85f, 0.8f)));
            c.Kit.Text(root, "RUST BUCKET SALVAGE", new Vector3(r.xMax - 10f, 3.6f, r.yMax + 0.1f), 180f, 0.5f, new Color(0.8f, 0.35f, 0.1f));
            Fence(c, root, new Vector3(r.xMin, 0f, r.yMax), new Vector3(r.xMax, 0f, r.yMax), 2.8f, gapAt: 20f);
            Fence(c, root, new Vector3(r.xMax, 0f, r.yMax), new Vector3(r.xMax, 0f, r.yMin), 2.8f);
            Fence(c, root, new Vector3(r.xMin, 0f, r.yMin), new Vector3(r.xMin, 0f, r.yMax), 2.8f);
        }

        /// <summary>
        /// The Harborside Cannery: closed for years, windows boarded, tagged all over. The fence has a gap on the
        /// Depot Rd side and a door hangs open round the back: a hidden place to explore.
        /// </summary>
        private static void CanneryRuin(CityContext c, Transform root, System.Random rng)
        {
            Rect r = Cannery;
            Yard(c, root, r, rng);
            Transform hall = Kit.Group(root, "Harborside Cannery", new Vector3(r.center.x, 0f, r.yMin + 10f));
            Material brick = c.P.Facade(FacadeStyle.Brick, false);
            Material dark = c.P.Lit(new Color(0.12f, 0.11f, 0.1f));
            Material board = c.P.Lit(new Color(0.55f, 0.45f, 0.3f));
            float w = 60f, d = 55f, h = 12f;
            // Walls as separate slabs with a doorway round the back (east side, facing Depot Rd).
            c.Kit.Facade(hall, "West wall", new Vector3(-w / 2f, 0f, 0f), new Vector3(-w / 2f + 1f, h, d), brick, dark);
            c.Kit.Facade(hall, "North wall", new Vector3(-w / 2f, 0f, d - 1f), new Vector3(w / 2f, h, d), brick, dark);
            c.Kit.Facade(hall, "South wall", new Vector3(-w / 2f, 0f, 0f), new Vector3(w / 2f, h, 1f), brick, dark);
            c.Kit.Facade(hall, "East wall south", new Vector3(w / 2f - 1f, 0f, 0f), new Vector3(w / 2f, h, d / 2f - 1.2f), brick, dark);
            c.Kit.Facade(hall, "East wall north", new Vector3(w / 2f - 1f, 0f, d / 2f + 1.2f), new Vector3(w / 2f, h, d), brick, dark);
            c.Kit.Span(hall, "Lintel", new Vector3(w / 2f - 1f, 2.6f, d / 2f - 1.2f), new Vector3(w / 2f, h, d / 2f + 1.2f), dark);
            // Half the roof has fallen in.
            c.Kit.Span(hall, "Roof", new Vector3(-w / 2f, h, d / 2f), new Vector3(w / 2f, h + 0.4f, d), dark);
            for (int i = 0; i < 8; i++)
            {
                GameObject beam = c.Kit.Box(hall, "Fallen beam", new Vector3(-w / 2f + 5f + i * 6.5f, 0.5f, 6f + (float)rng.NextDouble() * 15f), new Vector3(0.4f, 0.4f, 9f), dark);
                beam.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 20f, (float)rng.NextDouble() * 60f, 0f);
            }
            for (float x = -w / 2f + 4f; x < w / 2f - 3f; x += 6f)
            {
                c.Kit.Box(hall, "Boarded window", new Vector3(x, 4f, -0.05f), new Vector3(2.4f, 2f, 0.1f), board, collider: false);
                Graffiti(c, hall, new Vector3(x + 2.8f, 1.6f, -0.08f), 0f, rng);
            }
            c.Kit.Text(hall, "HARBORSIDE CANNERY CO. · 1921", new Vector3(0f, 9.5f, -0.08f), 0f, 0.7f, new Color(0.75f, 0.7f, 0.6f));
            Barrels(c, hall, new Vector3(10f, 0f, 30f), 5, rng);
            // A camp inside: a mattress, a crate table, cans.
            c.Kit.Box(hall, "Mattress", new Vector3(-20f, 0.12f, 40f), new Vector3(1.4f, 0.24f, 2f), c.P.Lit(new Color(0.6f, 0.55f, 0.48f)), collider: false);
            c.Kit.Box(hall, "Crate", new Vector3(-17.8f, 0.4f, 40f), new Vector3(0.8f, 0.8f, 0.8f), board);
            Fence(c, root, new Vector3(r.xMax, 0f, r.yMin), new Vector3(r.xMax, 0f, r.yMax), 2.6f, gapAt: 38f);
            c.Anchor("cannery_inside", ToWorld(hall, new Vector3(0f, 0f, 30f)));
        }

        private static void TruckYardAndTerminal(CityContext c, Transform root, System.Random rng)
        {
            Rect t = TruckYard;
            Yard(c, root, t, rng);
            for (float x = t.xMin + 4f; x < t.xMax - 8f; x += 14f)
                Container(c, root, new Vector3(x + 6f, 0f, t.center.y), 90f, true, ContainerColours[rng.Next(ContainerColours.Length)]);
            // The container terminal on the waterfront: stacks up to three high, a gantry crane over them.
            Rect r = Terminal;
            for (float x = r.xMin + 8f; x < r.xMax - 8f; x += 14f)
            for (float z = r.yMin + 3f; z < r.yMax - 2f; z += 3f)
            {
                int stack = 1 + rng.Next(3);
                for (int s = 0; s < stack; s++)
                    Container(c, root, new Vector3(x, s * 2.6f, z), 90f, true, ContainerColours[rng.Next(ContainerColours.Length)]);
            }
            Material steel = c.P.Lit(new Color(0.8f, 0.5f, 0.12f), 0.3f);
            float gx = r.xMin + 60f;
            foreach (float z in new[] { r.yMin - 2f, r.yMax + 2f })
                c.Kit.Box(root, "Gantry leg", new Vector3(gx, 11f, z), new Vector3(1.2f, 22f, 1.2f), steel);
            c.Kit.Box(root, "Gantry beam", new Vector3(gx, 22f, r.center.y), new Vector3(2f, 1.6f, r.height + 6f), steel);
            c.Kit.Box(root, "Gantry cab", new Vector3(gx, 20.5f, r.center.y + 4f), new Vector3(2.4f, 1.8f, 2.4f), c.P.Lit(new Color(0.9f, 0.9f, 0.88f)));
        }

        /// <summary>Wooden utility poles with drooping wires along the industrial roads.</summary>
        private static void UtilityLines(CityContext c, Transform root)
        {
            Transform g = Kit.Group(root, "Utility lines");
            Material wood = c.P.Lit(new Color(0.35f, 0.26f, 0.18f), 0.05f);
            Material wire = c.P.Lit(new Color(0.05f, 0.05f, 0.05f));
            (Vector2 From, Vector2 To)[] lines =
            {
                (new Vector2(-462f, -20f), new Vector2(-462f, -262f)),   // Depot Rd, east side
                (new Vector2(-462f, -151f), new Vector2(-258f, -151f)),  // Foundry St west, behind the north sidewalk
                (new Vector2(-462f, -279.2f), new Vector2(-258f, -279.2f)),  // Harbor Rd west, behind the south sidewalk
            };
            foreach (var (from, to) in lines)
            {
                float len = Vector2.Distance(from, to);
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 30f));
                Vector3 prev = Vector3.zero;
                for (int i = 0; i <= n; i++)
                {
                    Vector2 p = Vector2.Lerp(from, to, i / (float)n);
                    Vector3 top = new Vector3(p.x, 8.4f, p.y);
                    c.Kit.Cylinder(g, "Pole", new Vector3(p.x, 4.4f, p.y), 0.3f, 8.8f, wood, collider: true);
                    GameObject cross = c.Kit.Box(g, "Crossarm", top, new Vector3(1.8f, 0.12f, 0.12f), wood, collider: false);
                    cross.transform.localRotation = Quaternion.LookRotation(new Vector3(to.x - from.x, 0f, to.y - from.y));
                    if (i > 0)
                        foreach (float o in new[] { -0.8f, 0f, 0.8f })
                        {
                            Vector3 side = Vector3.Cross(Vector3.up, (top - prev).normalized) * o;
                            Vector3 a = prev + side + Vector3.up * 0.1f, b = top + side + Vector3.up * 0.1f;
                            // Two straight spans dipping to a sag in the middle.
                            Vector3 mid = (a + b) / 2f + Vector3.down * 0.9f;
                            foreach (var (s0, s1) in new[] { (a, mid), (mid, b) })
                            {
                                GameObject w = c.Kit.Box(g, "Wire", (s0 + s1) / 2f, new Vector3(0.025f, 0.025f, Vector3.Distance(s0, s1)), wire, collider: false);
                                w.transform.localRotation = Quaternion.LookRotation(s1 - s0);
                            }
                        }
                    prev = top;
                }
            }
        }

        private static Vector3 ToWorld(Transform t, Vector3 local) => t.TransformPoint(local);
        private static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z).normalized;
    }
}
