using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    public enum PlaceKind
    {
        Walkway,
        Door,
        Bench,
    }

    /// <summary>
    /// Where pedestrians can walk: a loop along the middle of every block's sidewalk, crosswalks between them at
    /// each junction, and short spurs to doors and benches. Built from the plan and the road network.
    /// </summary>
    public sealed class SidewalkGraph
    {
        public sealed class Node
        {
            public int Id;
            public Vector2 P;
            public PlaceKind Kind;
            public string Tag;
            public readonly List<Edge> Edges = new List<Edge>();
        }

        public sealed class Edge
        {
            public Node A, B;
            public float Length;
            public Crosswalk Crosswalk;
            public Node Other(Node n) => n == A ? B : A;
        }

        public sealed class Crosswalk
        {
            public RoadNetwork.Node Junction;
            public Vector2 Center;
            /// <summary>Unit vector along the road being crossed.</summary>
            public Vector2 Along;
            public bool Signalized;
            /// <summary>Light phase whose green makes it safe to walk (the crossed road is red then).</summary>
            public int WalkPhase;
        }

        public readonly List<Node> Nodes = new List<Node>();
        public readonly List<Crosswalk> Crosswalks = new List<Crosswalk>();

        private const float Inset = CityPlan.SidewalkWidth / 2f;

        public IEnumerable<Node> Places(PlaceKind kind)
        {
            foreach (Node n in Nodes)
                if (n.Kind == kind) yield return n;
        }

        public Node FindPlace(string tag) => Nodes.Find(n => n.Tag == tag);

        public static SidewalkGraph Build(RoadNetwork roads, IEnumerable<(Vector2 P, PlaceKind Kind, string Tag)> places)
        {
            var g = new SidewalkGraph();
            var rings = new List<Rect>();
            foreach (var block in CityPlan.Blocks)
            {
                Rect a = block.Area;
                rings.Add(Rect.MinMaxRect(a.xMin + Inset, a.yMin + Inset, a.xMax - Inset, a.yMax - Inset));
            }
            var onRing = new List<List<Node>>();
            foreach (Rect r in rings)
            {
                var corners = new List<Node>
                {
                    g.Add(new Vector2(r.xMin, r.yMin)), g.Add(new Vector2(r.xMax, r.yMin)),
                    g.Add(new Vector2(r.xMax, r.yMax)), g.Add(new Vector2(r.xMin, r.yMax)),
                };
                onRing.Add(corners);
            }

            // Crosswalks: across every approach of every junction, just outside the junction box.
            float mid = (CityPlan.CrosswalkNear + CityPlan.CrosswalkFar) / 2f;
            foreach (RoadNetwork.Node j in roads.Nodes)
            foreach (RoadNetwork.Lane outLane in j.Outgoing)
            {
                Vector2 d = outLane.Dir;
                Vector2 across = RoadNetwork.RightOf(d);
                Vector2 c = j.P + d * mid;
                Node a = g.AttachToRing(rings, onRing, c + across * mid, exact: true);
                Node b = g.AttachToRing(rings, onRing, c - across * mid, exact: true);
                if (a == null || b == null) continue;
                bool stem = j.StemDir != Vector2.zero && Vector2.Dot(d, j.StemDir) > 0.9f;
                var cw = new Crosswalk { Junction = j, Center = c, Along = d, Signalized = j.Lights != null, WalkPhase = stem ? 0 : 1 };
                g.Crosswalks.Add(cw);
                g.Link(a, b, cw);
            }

            foreach (var (p, kind, tag) in places)
            {
                Node spot = g.Add(p);
                spot.Kind = kind;
                spot.Tag = tag;
                Node landing = g.AttachToRing(rings, onRing, p, exact: false);
                g.Link(spot, landing, null);
            }

            // Connect each ring's points in order around the loop.
            for (int i = 0; i < rings.Count; i++)
            {
                Rect r = rings[i];
                List<Node> pts = onRing[i];
                pts.Sort((x, y) => Perimeter(r, x.P).CompareTo(Perimeter(r, y.P)));
                for (int k = 0; k < pts.Count; k++) g.Link(pts[k], pts[(k + 1) % pts.Count], null);
            }
            return g;
        }

        private Node Add(Vector2 p)
        {
            var n = new Node { Id = Nodes.Count, P = p };
            Nodes.Add(n);
            return n;
        }

        private void Link(Node a, Node b, Crosswalk cw)
        {
            if (a == b) return;
            var e = new Edge { A = a, B = b, Length = Vector2.Distance(a.P, b.P), Crosswalk = cw };
            a.Edges.Add(e);
            b.Edges.Add(e);
        }

        /// <summary>Finds (or inserts) the ring point nearest to <paramref name="p"/>. Exact: p must lie on a ring.</summary>
        private Node AttachToRing(List<Rect> rings, List<List<Node>> onRing, Vector2 p, bool exact)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            Vector2 bestPoint = default;
            for (int i = 0; i < rings.Count; i++)
            {
                Vector2 q = ClosestOnBoundary(rings[i], p);
                float d = Vector2.Distance(p, q);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                    bestPoint = q;
                }
            }
            if (best < 0 || (exact && bestDistance > 0.05f)) return null;
            foreach (Node n in onRing[best])
                if (Vector2.Distance(n.P, bestPoint) < 0.3f) return n;
            Node added = Add(bestPoint);
            onRing[best].Add(added);
            return added;
        }

        private static Vector2 ClosestOnBoundary(Rect r, Vector2 p)
        {
            Vector2 c = new Vector2(Mathf.Clamp(p.x, r.xMin, r.xMax), Mathf.Clamp(p.y, r.yMin, r.yMax));
            if (c != p) return c; // outside: clamping lands on the boundary
            // Inside: push to the nearest side.
            float left = p.x - r.xMin, right = r.xMax - p.x, bottom = p.y - r.yMin, top = r.yMax - p.y;
            float m = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));
            if (m == left) return new Vector2(r.xMin, p.y);
            if (m == right) return new Vector2(r.xMax, p.y);
            if (m == bottom) return new Vector2(p.x, r.yMin);
            return new Vector2(p.x, r.yMax);
        }

        /// <summary>Distance along the rectangle's boundary, counter-clockwise from the min corner.</summary>
        private static float Perimeter(Rect r, Vector2 p)
        {
            const float e = 0.01f;
            if (Mathf.Abs(p.y - r.yMin) < e) return p.x - r.xMin;
            if (Mathf.Abs(p.x - r.xMax) < e) return r.width + (p.y - r.yMin);
            if (Mathf.Abs(p.y - r.yMax) < e) return r.width + r.height + (r.xMax - p.x);
            return 2f * r.width + r.height + (r.yMax - p.y);
        }

        /// <summary>Shortest route (Dijkstra). Null if unreachable.</summary>
        public List<Node> Route(Node from, Node to)
        {
            var dist = new float[Nodes.Count];
            var prev = new Node[Nodes.Count];
            var done = new bool[Nodes.Count];
            for (int i = 0; i < dist.Length; i++) dist[i] = float.MaxValue;
            dist[from.Id] = 0f;
            for (int iteration = 0; iteration < Nodes.Count; iteration++)
            {
                Node u = null;
                float best = float.MaxValue;
                foreach (Node n in Nodes)
                    if (!done[n.Id] && dist[n.Id] < best)
                    {
                        best = dist[n.Id];
                        u = n;
                    }
                if (u == null || u == to) break;
                done[u.Id] = true;
                foreach (Edge e in u.Edges)
                {
                    Node v = e.Other(u);
                    // Places are dead ends: never route through a door or bench to reach somewhere else.
                    if (v != to && v.Kind != PlaceKind.Walkway) continue;
                    float alt = dist[u.Id] + e.Length + (e.Crosswalk != null ? 4f : 0f);
                    if (alt < dist[v.Id])
                    {
                        dist[v.Id] = alt;
                        prev[v.Id] = u;
                    }
                }
            }
            if (dist[to.Id] == float.MaxValue) return null;
            var route = new List<Node>();
            for (Node n = to; n != null; n = prev[n.Id]) route.Add(n);
            route.Reverse();
            return route;
        }

        public static Edge EdgeBetween(Node a, Node b) => a.Edges.Find(e => e.Other(a) == b);
    }
}
