using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Outside-only buildings from <see cref="CityPlan.Shells"/>: a facade volume (with a storefront band where
    /// asked), a front door on the side facing the nearest street, and an optional name sign. Their front doors
    /// are where pedestrians come and go.
    /// </summary>
    public static class ShellBuilder
    {
        private const float StorefrontHeight = 4.2f;
        /// <summary>Kenney's modular door faces its local +z; the door group's outside is -z.</summary>
        private const float KitDoorYaw = 180f;

        public static void Build(CityContext c)
        {
            Transform root = Kit.Group(c.Static, "Buildings");
            Material roof = c.P.Lit(new Color(0.24f, 0.24f, 0.25f));
            Material door = c.P.Lit(new Color(0.16f, 0.14f, 0.13f), 0.3f);
            Material frame = c.P.Lit(new Color(0.75f, 0.73f, 0.68f));
            int index = 0;
            foreach (Shell s in CityPlan.Shells)
            {
                Rect f = s.Footprint;
                int seed = 7000 + index;
                string name = $"Building {index++} ({s.Style})";
                // The parking garage keeps its banded concrete facade.
                Transform group = Kit.Group(root, name);
                bool dressed = s.Sign != "PARKING";
                if (dressed) Row(c, group, s, seed);
                else
                {
                    float baseTop = 0f;
                    if (s.Storefront)
                    {
                        c.Kit.Facade(group, name + " shops", new Vector3(f.xMin, 0f, f.yMin), new Vector3(f.xMax, StorefrontHeight, f.yMax), c.P.Facade(s.Style, true), roof);
                        baseTop = StorefrontHeight;
                    }
                    c.Kit.Facade(group, name, new Vector3(f.xMin, baseTop, f.yMin), new Vector3(f.xMax, s.Height, f.yMax), c.P.Facade(s.Style, false), roof);
                }

                // Front door facing the closest street.
                Vector2 outward = FrontDirection(f, out Vector2 frontCentre);
                Vector2 along = new Vector2(outward.y, -outward.x);
                Vector2 doorAt = frontCentre + along * (Mathf.Abs(Vector2.Dot(along, f.size)) * 0.18f);
                float yaw = Mathf.Atan2(-outward.x, -outward.y) * Mathf.Rad2Deg; // door faces outward (its -z)
                Transform d = Kit.Group(group, "Front door", new Vector3(doorAt.x, 0f, doorAt.y), yaw);
                if (!dressed || c.Kit.Model(d, "door-white-glass", new Vector3(0f, 0f, -0.04f), KitDoorYaw, 5f) == null)
                {
                    c.Kit.Box(d, "Frame", new Vector3(0f, 1.25f, -0.03f), new Vector3(1.5f, 2.5f, 0.06f), frame, collider: false);
                    c.Kit.Box(d, "Door", new Vector3(0f, 1.15f, -0.07f), new Vector3(1.2f, 2.3f, 0.04f), door, collider: false);
                }
                c.Place(new Vector3(doorAt.x + outward.x * 0.6f, 0f, doorAt.y + outward.y * 0.6f), PlaceKind.Door, name);

                if (s.Sign != null)
                {
                    Vector2 signAt = frontCentre + outward * 0.08f;
                    float height = Mathf.Min(s.Height - 1.5f, s.Storefront ? StorefrontHeight + 1.2f : 5.2f);
                    c.Kit.Text(root, s.Sign, new Vector3(signAt.x, height, signAt.y), yaw, 0.75f, new Color(0.92f, 0.9f, 0.84f));
                }
            }
        }

        /// <summary>
        /// A shell's frontage as a row of 1–4 separate buildings of differing widths and heights (a street of
        /// neighbours, not one long block), each a textured <see cref="ModularFacade"/> volume the full lot deep.
        /// </summary>
        private static void Row(CityContext c, Transform group, Shell s, int seed)
        {
            Rect f = s.Footprint;
            var rng = new System.Random(seed);
            Vector2 outward = FrontDirection(f, out _);
            bool alongX = Mathf.Abs(outward.y) > 0.5f; // front faces ±z, so the row runs along x
            float start = alongX ? f.xMin : f.yMin, length = alongX ? f.width : f.height;
            int count = Mathf.Clamp(Mathf.RoundToInt(length / 14f), 1, 4);
            // Uneven splits: each building 70–130% of an even share, rescaled to fill the frontage exactly.
            var shares = new float[count];
            float total = 0f;
            for (int i = 0; i < count; i++) total += shares[i] = 0.7f + 0.6f * (float)rng.NextDouble();
            float at = start;
            for (int i = 0; i < count; i++)
            {
                float w = length * shares[i] / total;
                // The first keeps the plan's height (signs are placed from it); neighbours step up and down.
                float height = i == 0 ? s.Height : Mathf.Max(ModularFacade.ShopStorey + 3.2f, s.Height * (0.75f + 0.5f * (float)rng.NextDouble()));
                Vector3 min = alongX ? new Vector3(at, 0f, f.yMin) : new Vector3(f.xMin, 0f, at);
                Vector3 max = alongX ? new Vector3(at + w, height, f.yMax) : new Vector3(f.xMax, height, at + w);
                ModularFacade.Build(c, group, $"Block {i}", min, max, s.Style, seed * 7 + i, s.Storefront);
                at += w;
            }
        }

        /// <summary>Unit vector out of the footprint side nearest to a street centre line, and that side's midpoint.</summary>
        public static Vector2 FrontDirection(Rect f, out Vector2 sideCentre)
        {
            (Vector2 Out, Vector2 Mid)[] sides =
            {
                (Vector2.down, new Vector2(f.center.x, f.yMin)), (Vector2.up, new Vector2(f.center.x, f.yMax)),
                (Vector2.left, new Vector2(f.xMin, f.center.y)), (Vector2.right, new Vector2(f.xMax, f.center.y)),
            };
            float best = float.MaxValue;
            sideCentre = sides[0].Mid;
            Vector2 outward = sides[0].Out;
            foreach (var (o, mid) in sides)
            {
                float d = DistanceToStreet(mid + o * 0.5f);
                if (d < best)
                {
                    best = d;
                    outward = o;
                    sideCentre = mid;
                }
            }
            return outward;
        }

        private static float DistanceToStreet(Vector2 p) => StreetMap.Plan.DistanceToStreet(p);
    }
}
