using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Street-side things with a purpose (TOWN_SPEC A11, A15): bus stops where people wait, and the clutter that
    /// makes a street look used.
    /// </summary>
    public static class StreetProps
    {
        /// <summary>Bus stops: a point on the street's centre line and which side (+1 left of the segment, -1 right).</summary>
        private static readonly (Vector2 At, int Side, string Name)[] Stops =
        {
            (new Vector2(-195f, -14f), 1, "Maple & Willow"), (new Vector2(-90f, -14f), -1, "Maple & Cedar"), (new Vector2(22f, -14f), 1, "Maple Park"),
            (new Vector2(100f, -14f), -1, "Main Street"), (new Vector2(250f, -14f), 1, "City Hall"), (new Vector2(400f, -14f), -1, "Canal Row"),
            (new Vector2(-100f, 70f), 1, "Grove & Cedar"), (new Vector2(100f, 70f), -1, "Grove & First"), (new Vector2(0f, -270f), 1, "Harbor Rd"),
            (new Vector2(-80f, -160f), 1, "Foundry St"), (new Vector2(-320f, -14f), 1, "FreshWay"),
        };

        public static void BusStops(CityContext c)
        {
            Kit k = c.Kit;
            Transform root = Kit.Group(c.Static, "Bus stops");
            Material frame = c.P.Lit(new Color(0.22f, 0.24f, 0.26f), 0.4f);
            Material glass = c.P.Glass(new Color(0.7f, 0.8f, 0.85f, 0.3f));
            Material sign = c.P.Lit(new Color(0.1f, 0.35f, 0.6f), 0.3f);
            foreach (var (at, side, name) in Stops)
            {
                StreetMap.Segment s = c.Roads.Map.Nearest(at, out float t, out _);
                if (s == null || s.Sidewalk <= 0f) continue;
                Vector2 outward = s.Left * side;
                float g = s.GradeAt(t);
                // Local frame: origin on the kerb, +z away from the street (the shelter at the sidewalk's back edge).
                Vector2 kerb = s.At(t) + outward * s.HalfWidth;
                float yaw = Mathf.Atan2(outward.x, outward.y) * Mathf.Rad2Deg;
                Transform stop = Kit.Group(root, "Bus stop " + name, new Vector3(kerb.x, g, kerb.y), yaw);
                float back = s.Sidewalk - 0.1f;
                k.Box(stop, "Sign pole", new Vector3(-2f, 1.4f, 0.4f), new Vector3(0.08f, 2.8f, 0.08f), frame);
                k.Box(stop, "Sign", new Vector3(-2f, 2.6f, 0.4f), new Vector3(0.5f, 0.5f, 0.04f), sign, collider: false);
                k.Text(stop, "BUS", new Vector3(-2f, 2.6f, 0.37f), 0f, 0.12f, Color.white);
                k.Box(stop, "Roof", new Vector3(0f, 2.5f, back - 0.65f), new Vector3(3.2f, 0.1f, 1.4f), frame, collider: false);
                k.Box(stop, "Back", new Vector3(0f, 1.3f, back), new Vector3(3.2f, 2.3f, 0.05f), glass);
                foreach (float x in new[] { -1.55f, 1.55f })
                    k.Box(stop, "Post", new Vector3(x, 1.25f, back - 0.65f), new Vector3(0.06f, 2.5f, 1.35f), glass, collider: false);
                k.Box(stop, "Bench", new Vector3(0f, 0.45f, back - 0.35f), new Vector3(2.4f, 0.08f, 0.45f), c.P.Lit(new Color(0.45f, 0.32f, 0.2f)));
                k.Text(stop, name.ToUpperInvariant(), new Vector3(0f, 2.3f, back - 0.03f), 0f, 0.08f, Color.white);
                string tag = "bus stop " + name;
                c.Place(stop.TransformPoint(new Vector3(0.8f, 0f, 1.2f)), PlaceKind.Stand, tag);
                c.PlaceInfo[tag] = (PlaceCategory.Stand, Hours.Of(5.5, 24));
            }
        }
    }
}
