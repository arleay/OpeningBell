using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Street geometry derived from <see cref="CityPlan.Streets"/>: nodes wherever streets share a point, segments
    /// between them, and at every node the approaches sorted counter-clockwise, the kerb corners between neighbouring
    /// approaches, how far each road stops short of the junction (its trim), crosswalk and stop-line positions, and
    /// the grade (ground height) along every segment. Bends are nodes too, so curves, T junctions and crossings at
    /// any angle all come out of one construction. Alleys and dirt roads are driveways: they meet streets at the kerb.
    /// </summary>
    public sealed class StreetMap
    {
        public sealed class Node
        {
            public int Id;
            public Vector2 P;
            public float Grade;
            public NodeControl Control;
            public string Name;
            /// <summary>The street passing through (priority, light phase 0); null at bends and dead ends.</summary>
            public string MainStreet;
            /// <summary>The approaches of the road passing through (it may change name here, like the highway into Maple).</summary>
            public readonly HashSet<Approach> Main = new HashSet<Approach>();
            public bool IsMain(Approach a) => Main.Contains(a);
            public readonly List<Approach> Approaches = new List<Approach>();
            /// <summary>Corner k lies between Approaches[k]'s left side and Approaches[k+1]'s right side.</summary>
            public readonly List<Corner> Corners = new List<Corner>();

            public int Degree => Approaches.Count;
            public bool IsJunction => Approaches.Count >= 3;
        }

        public sealed class Segment
        {
            public int Id;
            public string Street;
            public RoadClass Class;
            public Node A, B;
            public Vector2 Dir;
            public float Length;
            public float HalfWidth, Sidewalk;
            public Approach AtA, AtB;
            /// <summary>Over water between these distances from A (a bridge deck); both zero if not.</summary>
            public float BridgeFrom, BridgeTo;

            public bool IsBridge => BridgeTo > BridgeFrom;
            public Vector2 Left => new Vector2(-Dir.y, Dir.x);
            public Vector2 At(float s) => A.P + Dir * s;
            public bool Traffic => CityPlan.HasTraffic(Class);

            /// <summary>Sidewalk-level height at s: flat across each junction, a straight grade between them.</summary>
            public float GradeAt(float s)
            {
                float t0 = AtA.Trim, t1 = Length - AtB.Trim;
                if (s <= t0 || t1 <= t0) return A.Grade;
                if (s >= t1) return B.Grade;
                return Mathf.Lerp(A.Grade, B.Grade, (s - t0) / (t1 - t0));
            }
        }

        public sealed class Approach
        {
            public Node Node;
            public Segment Segment;
            public bool AtA;
            /// <summary>Direction away from the node along the segment.</summary>
            public Vector2 Out;
            public float Angle;
            /// <summary>The road surface of this segment starts here (the junction box covers the rest).</summary>
            public float Trim;
            /// <summary>Where the sidewalk strip starts on each side (distance from the node).</summary>
            public float LeftStart, RightStart;
            /// <summary>Crosswalk centre (distance from the node); 0 when there is none.</summary>
            public float Crosswalk;
            /// <summary>Traffic stops here (distance from the node).</summary>
            public float StopLine;

            public float HalfWidth => Segment.HalfWidth;
            public float Sidewalk => Segment.Sidewalk;
            public Vector2 LeftDir => new Vector2(-Out.y, Out.x);
            public Vector2 RightDir => new Vector2(Out.y, -Out.x);
            public Vector2 At(float s, float lateral) => Node.P + Out * s + LeftDir * lateral;
            public Approach Opposite => AtA ? Segment.AtB : Segment.AtA;
            public float GradeAt(float s) => Segment.GradeAt(AtA ? s : Segment.Length - s);
            public bool HasCrosswalk => Crosswalk > 0f;
        }

        /// <summary>A kerb corner: between one approach's left side and the next approach's right side.</summary>
        public sealed class Corner
        {
            public Node Node;
            public Approach Left;   // this approach's left side…
            public Approach Right;  // …and this one's right side
            public Vector2 Curb, Outer;
            public bool Walkable => Left.Sidewalk > 0f || Right.Sidewalk > 0f;
            public Vector2 Middle => (Curb + Outer) * 0.5f;
        }

        public readonly List<Node> Nodes = new List<Node>();
        public readonly List<Segment> Segments = new List<Segment>();
        public readonly List<StreetDef> Driveways = new List<StreetDef>();

        private static StreetMap _plan;
        /// <summary>The town's streets (built once; the plan is static).</summary>
        public static StreetMap Plan => _plan ??= Build(CityPlan.Streets);

        public const float CrosswalkWidth = 3f;

        public static StreetMap Build(IEnumerable<StreetDef> streets)
        {
            var map = new StreetMap();
            var byPoint = new Dictionary<Vector2, Node>();
            Node NodeAt(Vector2 p)
            {
                if (byPoint.TryGetValue(p, out Node n)) return n;
                n = new Node { Id = map.Nodes.Count, P = p };
                byPoint[p] = n;
                map.Nodes.Add(n);
                return n;
            }

            foreach (StreetDef def in streets)
            {
                if (def.IsDriveway)
                {
                    map.Driveways.Add(def);
                    continue;
                }
                for (int i = 0; i + 1 < def.Points.Length; i++)
                {
                    Node a = NodeAt(def.Points[i]), b = NodeAt(def.Points[i + 1]);
                    Vector2 d = b.P - a.P;
                    var seg = new Segment
                    {
                        Id = map.Segments.Count, Street = def.Name, Class = def.Class, A = a, B = b, Dir = d.normalized, Length = d.magnitude,
                        HalfWidth = CityPlan.HalfWidth(def.Class), Sidewalk = CityPlan.Sidewalk(def.Class),
                    };
                    seg.AtA = new Approach { Node = a, Segment = seg, AtA = true, Out = seg.Dir };
                    seg.AtB = new Approach { Node = b, Segment = seg, AtA = false, Out = -seg.Dir };
                    a.Approaches.Add(seg.AtA);
                    b.Approaches.Add(seg.AtB);
                    map.Segments.Add(seg);
                }
            }

            var lights = new HashSet<Vector2>(CityPlan.Lights);
            var free = new HashSet<Vector2>(CityPlan.Uncontrolled);
            foreach (Node n in map.Nodes)
            {
                n.Grade = CityPlan.Grades.TryGetValue(n.P, out float g) ? g : TownTerrain.Natural(n.P.x, n.P.y);
                foreach (Approach a in n.Approaches) a.Angle = Mathf.Atan2(a.Out.y, a.Out.x);
                n.Approaches.Sort((x, y) => x.Angle.CompareTo(y.Angle));
                n.Control = !n.IsJunction ? NodeControl.None : lights.Contains(n.P) ? NodeControl.Lights : free.Contains(n.P) ? NodeControl.None : NodeControl.StopOnStem;
                n.MainStreet = n.IsJunction ? MainStreet(n) : null;
                if (n.IsJunction) PickMain(n);
                var names = new List<string>();
                foreach (Approach a in n.Approaches)
                    if (!names.Contains(a.Segment.Street)) names.Add(a.Segment.Street);
                names.Sort((x, y) => Rank(x).CompareTo(Rank(y)));
                n.Name = string.Join(" & ", names);
                map.BuildCorners(n);
            }
            foreach (Segment s in map.Segments) FindBridge(s);
            return map;
        }

        private static int Rank(string street) => System.Array.FindIndex(CityPlan.Streets, d => d.Name == street);

        /// <summary>The first-listed street with two approaches (it continues through); else the first listed.</summary>
        private static string MainStreet(Node n)
        {
            string best = null;
            int bestRank = int.MaxValue;
            foreach (Approach a in n.Approaches)
            {
                int count = n.Approaches.FindAll(x => x.Segment.Street == a.Segment.Street).Count;
                int rank = Rank(a.Segment.Street) - (count >= 2 ? 1000 : 0);
                if (rank < bestRank)
                {
                    bestRank = rank;
                    best = a.Segment.Street;
                }
            }
            return best;
        }

        /// <summary>
        /// The through road: the main street's two approaches if it continues through; otherwise the straightest pair
        /// of approaches (a road changing name, a T onto a bend); failing that the main street's single approach.
        /// </summary>
        private static void PickMain(Node n)
        {
            List<Approach> same = n.Approaches.FindAll(a => a.Segment.Street == n.MainStreet);
            if (same.Count >= 2)
            {
                foreach (Approach a in same) n.Main.Add(a);
                return;
            }
            Approach bestA = null, bestB = null;
            float best = -0.7f;
            foreach (Approach a in n.Approaches)
            foreach (Approach b in n.Approaches)
            {
                if (a == b) continue;
                float dot = Vector2.Dot(a.Out, b.Out);
                if (dot < best)
                {
                    best = dot;
                    bestA = a;
                    bestB = b;
                }
            }
            if (bestA != null)
            {
                n.Main.Add(bestA);
                n.Main.Add(bestB);
            }
            else foreach (Approach a in same) n.Main.Add(a);
        }

        private void BuildCorners(Node n)
        {
            int count = n.Approaches.Count;
            if (count == 1)
            {
                // Dead end: the road runs to the node and stops; sidewalks end with it.
                Approach only = n.Approaches[0];
                only.Trim = only.LeftStart = only.RightStart = 0f;
                only.StopLine = 1.5f;
                return;
            }
            for (int k = 0; k < count; k++)
            {
                Approach i = n.Approaches[k], j = n.Approaches[(k + 1) % count];
                var c = new Corner { Node = n, Left = i, Right = j };
                c.Curb = Meet(n.P, i.Out, i.LeftDir * i.HalfWidth, j.Out, j.RightDir * j.HalfWidth);
                c.Outer = Meet(n.P, i.Out, i.LeftDir * (i.HalfWidth + i.Sidewalk), j.Out, j.RightDir * (j.HalfWidth + j.Sidewalk));
                n.Corners.Add(c);
            }
            foreach (Approach a in n.Approaches) a.Trim = 0f;
            foreach (Corner c in n.Corners)
            {
                c.Left.Trim = Mathf.Max(c.Left.Trim, Vector2.Dot(c.Curb - n.P, c.Left.Out));
                c.Right.Trim = Mathf.Max(c.Right.Trim, Vector2.Dot(c.Curb - n.P, c.Right.Out));
                c.Left.LeftStart = Mathf.Max(0f, Vector2.Dot(c.Outer - n.P, c.Left.Out));
                c.Right.RightStart = Mathf.Max(0f, Vector2.Dot(c.Outer - n.P, c.Right.Out));
            }
            foreach (Approach a in n.Approaches)
            {
                // Never trim more than a third of a segment (very sharp corners would eat the road).
                float cap = a.Segment.Length / 3f;
                a.Trim = Mathf.Min(a.Trim, cap);
                a.LeftStart = Mathf.Min(a.LeftStart, cap);
                a.RightStart = Mathf.Min(a.RightStart, cap);
                bool crossing = n.IsJunction && a.Sidewalk > 0f && a.Segment.Traffic;
                a.Crosswalk = crossing ? a.Trim + 0.3f + CrosswalkWidth / 2f : 0f;
                a.StopLine = crossing ? a.Crosswalk + CrosswalkWidth / 2f + 0.7f : n.IsJunction ? a.Trim + 1f : a.Trim;
            }
        }

        /// <summary>
        /// Where two offset lines meet: node + u·a + oa and node + v·b + ob. Parallel (a straight run through a
        /// bend): the first line's offset point.
        /// </summary>
        private static Vector2 Meet(Vector2 node, Vector2 a, Vector2 oa, Vector2 b, Vector2 ob)
        {
            float denom = a.x * b.y - a.y * b.x;
            if (Mathf.Abs(denom) < 0.02f) return node + oa;
            Vector2 d = (node + ob) - (node + oa);
            float u = (d.x * b.y - d.y * b.x) / denom;
            return node + oa + a * u;
        }

        private static void FindBridge(Segment s)
        {
            float from = -1f, to = -1f;
            for (float t = 0f; t <= s.Length; t += 1f)
            {
                Vector2 p = s.At(t);
                if (!TownTerrain.IsWater(p.x, p.y, out _)) continue;
                if (from < 0f) from = t;
                to = t;
            }
            if (from < 0f) return;
            s.BridgeFrom = Mathf.Max(0f, from - 4f);
            s.BridgeTo = Mathf.Min(s.Length, to + 4f);
        }

        // ---- queries ----

        /// <summary>The segment whose centre line passes closest to p (s along it, signed lateral offset: + is left).</summary>
        public Segment Nearest(Vector2 p, out float s, out float lateral)
        {
            Segment best = null;
            float bestD = float.MaxValue;
            s = lateral = 0f;
            foreach (Segment seg in Segments)
            {
                float t = Mathf.Clamp(Vector2.Dot(p - seg.A.P, seg.Dir), 0f, seg.Length);
                Vector2 q = seg.At(t);
                float d = (p - q).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = seg;
                s = t;
                lateral = Vector2.Dot(p - q, seg.Left);
            }
            return best;
        }

        /// <summary>Distance from p to the nearest street centre line.</summary>
        public float DistanceToStreet(Vector2 p)
        {
            Segment seg = Nearest(p, out float s, out _);
            return seg == null ? float.MaxValue : Vector2.Distance(p, seg.At(s));
        }

        /// <summary>Sidewalk-level ground at p taken from the nearest street (lots face their street).</summary>
        public float StreetGrade(Vector2 p)
        {
            Segment seg = Nearest(p, out float s, out _);
            return seg == null ? 0f : seg.GradeAt(s);
        }

        public Node FindNode(string name) => Nodes.Find(n => n.Name == name);
        public Node NodeAt(Vector2 p) => Nodes.Find(n => (n.P - p).sqrMagnitude < 0.01f);
    }
}
