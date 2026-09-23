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
    /// Where pedestrians can walk, built from <see cref="StreetMap"/>: a path down the middle of each sidewalk,
    /// linked round every kerb corner, crosswalks across each approach of a junction (and across the end of a
    /// dead end), alleys as shortcuts, extra paths (park, canal towpath, pier), and short spurs to doors and benches.
    /// Every node knows its height, so people walk up the hill and over the bridges.
    /// </summary>
    public sealed class SidewalkGraph
    {
        public sealed class Node
        {
            public int Id;
            public Vector2 P;
            public float Y;
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

        public IEnumerable<Node> Places(PlaceKind kind)
        {
            foreach (Node n in Nodes)
                if (n.Kind == kind) yield return n;
        }

        public Node FindPlace(string tag) => Nodes.Find(n => n.Tag == tag);

        /// <param name="paths">Extra walkways (park paths, towpath, pier): polylines of (x, height, z) joined to the nearest sidewalk at both ends.</param>
        public static SidewalkGraph Build(RoadNetwork roads, IEnumerable<(Vector2 P, PlaceKind Kind, string Tag)> places, IEnumerable<Vector3[]> paths = null)
        {
            var g = new SidewalkGraph();
            StreetMap map = roads.Map;
            var junctions = new Dictionary<StreetMap.Node, RoadNetwork.Node>();
            foreach (RoadNetwork.Node n in roads.Nodes) junctions[n.Map] = n;

            // Side path ends: per approach, the point on its left and right sidewalk nearest the node.
            var leftEnd = new Dictionary<StreetMap.Approach, Node>();
            var rightEnd = new Dictionary<StreetMap.Approach, Node>();
            foreach (StreetMap.Node n in map.Nodes)
            {
                foreach (StreetMap.Approach a in n.Approaches)
                {
                    if (a.Sidewalk <= 0f) continue;
                    float mid = a.HalfWidth + a.Sidewalk / 2f;
                    float sl = a.HasCrosswalk ? a.Crosswalk : a.LeftStart, sr = a.HasCrosswalk ? a.Crosswalk : a.RightStart;
                    leftEnd[a] = g.Add(a.At(sl, mid), a.GradeAt(sl));
                    rightEnd[a] = g.Add(a.At(sr, -mid), a.GradeAt(sr));
                    if (a.HasCrosswalk)
                    {
                        junctions.TryGetValue(n, out RoadNetwork.Node j);
                        bool main = n.IsMain(a);
                        var cw = new Crosswalk
                        {
                            Junction = j, Center = a.At(a.Crosswalk, 0f), Along = a.Out,
                            Signalized = j != null && j.Lights != null,
                            // Crossing the main street is safe while it's red, i.e. in the cross street's phase.
                            WalkPhase = main ? 1 : 0,
                        };
                        g.Crosswalks.Add(cw);
                        g.Link(leftEnd[a], rightEnd[a], cw);
                    }
                }
                if (n.Degree == 1 && leftEnd.TryGetValue(n.Approaches[0], out Node l))
                {
                    // Dead end: cross the end of the road (no markings, just look both ways).
                    StreetMap.Approach only = n.Approaches[0];
                    var cw = new Crosswalk { Center = n.P, Along = only.Out };
                    g.Crosswalks.Add(cw);
                    g.Link(l, rightEnd[only], cw);
                }
                foreach (StreetMap.Corner c in n.Corners)
                {
                    bool hasLeft = leftEnd.TryGetValue(c.Left, out Node from);
                    bool hasRight = rightEnd.TryGetValue(c.Right, out Node to);
                    if (!hasLeft && !hasRight) continue;
                    Node corner = g.Add(c.Middle, n.Grade);
                    if (hasLeft) g.Link(from, corner, null);
                    if (hasRight) g.Link(corner, to, null);
                }
            }
            // Along each segment: A's left side runs to B's right side and vice versa.
            foreach (StreetMap.Segment s in map.Segments)
            {
                if (s.Sidewalk <= 0f) continue;
                g.Link(leftEnd[s.AtA], rightEnd[s.AtB], null);
                g.Link(rightEnd[s.AtA], leftEnd[s.AtB], null);
            }

            // Alleys: shortcuts between the sidewalks at their mouths.
            foreach (StreetDef d in map.Driveways)
            {
                if (d.Class != RoadClass.Alley) continue;
                Node prev = null;
                for (int i = 0; i < d.Points.Length; i++)
                {
                    Vector2 p = d.Points[i];
                    bool end = i == 0 || i == d.Points.Length - 1;
                    // Ends sit on a street's centre line: join the sidewalk on the alley's side of it.
                    Vector2 inward = i == 0 ? (d.Points[1] - p).normalized : (d.Points[i - 1] - p).normalized;
                    Node here = end ? g.AttachToSidewalk(map, p + inward * 7f) : null;
                    here ??= g.Add(p, TownTerrain.Height(p.x, p.y));
                    if (prev != null) g.Link(prev, here, null);
                    prev = here;
                }
            }
            foreach (Vector3[] path in paths ?? System.Array.Empty<Vector3[]>())
            {
                Node prev = g.AttachToSidewalk(map, new Vector2(path[0].x, path[0].z));
                for (int i = 0; i < path.Length; i++)
                {
                    Node here = i == path.Length - 1 ? g.AttachToSidewalk(map, new Vector2(path[i].x, path[i].z)) : null;
                    here ??= g.Add(new Vector2(path[i].x, path[i].z), path[i].y);
                    if (prev != null) g.Link(prev, here, null);
                    prev = here;
                }
            }

            foreach (var (p, kind, tag) in places)
            {
                Node landing = g.AttachToSidewalk(map, p);
                if (landing == null) continue;
                Node spot = g.Add(p, landing.Y);
                spot.Kind = kind;
                spot.Tag = tag;
                g.Link(spot, landing, null);
            }
            return g;
        }

        private Node Add(Vector2 p, float y)
        {
            var n = new Node { Id = Nodes.Count, P = p, Y = y };
            Nodes.Add(n);
            return n;
        }

        private void Link(Node a, Node b, Crosswalk cw)
        {
            if (a == b || EdgeBetween(a, b) != null) return;
            var e = new Edge { A = a, B = b, Length = Vector2.Distance(a.P, b.P), Crosswalk = cw };
            a.Edges.Add(e);
            b.Edges.Add(e);
        }

        private void Unlink(Edge e)
        {
            e.A.Edges.Remove(e);
            e.B.Edges.Remove(e);
        }

        /// <summary>
        /// Splits the nearest walkway edge (not a crosswalk, not a spur to a place) at the point closest to p and
        /// returns the new node there. Null if nothing is within 40 m.
        /// </summary>
        private Node AttachToSidewalk(StreetMap map, Vector2 p)
        {
            Edge best = null;
            float bestD = 40f * 40f, bestT = 0f;
            var seen = new HashSet<Edge>();
            foreach (Node n in Nodes)
            {
                if (n.Kind != PlaceKind.Walkway) continue;
                foreach (Edge e in n.Edges)
                {
                    if (!seen.Add(e) || e.Crosswalk != null || e.A.Kind != PlaceKind.Walkway || e.B.Kind != PlaceKind.Walkway) continue;
                    Vector2 ab = e.B.P - e.A.P;
                    float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - e.A.P, ab) / ab.sqrMagnitude);
                    float d = (e.A.P + ab * t - p).sqrMagnitude;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = e;
                        bestT = t;
                    }
                }
            }
            if (best == null) return null;
            if (bestT < 0.02f || best.Length * bestT < 0.3f) return best.A;
            if (bestT > 0.98f || best.Length * (1f - bestT) < 0.3f) return best.B;
            Node mid = Add(Vector2.Lerp(best.A.P, best.B.P, bestT), Mathf.Lerp(best.A.Y, best.B.Y, bestT));
            Unlink(best);
            Link(best.A, mid, null);
            Link(mid, best.B, null);
            return mid;
        }

        /// <summary>Shortest route (Dijkstra with a binary heap). Null if unreachable.</summary>
        public List<Node> Route(Node from, Node to)
        {
            var dist = new float[Nodes.Count];
            var prev = new Node[Nodes.Count];
            for (int i = 0; i < dist.Length; i++) dist[i] = float.MaxValue;
            dist[from.Id] = 0f;
            var open = new SortedSet<(float D, int Id)> { (0f, from.Id) };
            while (open.Count > 0)
            {
                var (d, id) = open.Min;
                open.Remove(open.Min);
                Node u = Nodes[id];
                if (u == to) break;
                if (d > dist[id]) continue;
                foreach (Edge e in u.Edges)
                {
                    Node v = e.Other(u);
                    // Places are dead ends: never route through a door or bench to reach somewhere else.
                    if (v != to && v.Kind != PlaceKind.Walkway) continue;
                    float alt = d + e.Length + (e.Crosswalk != null ? 4f : 0f);
                    if (alt >= dist[v.Id]) continue;
                    if (dist[v.Id] < float.MaxValue) open.Remove((dist[v.Id], v.Id));
                    dist[v.Id] = alt;
                    prev[v.Id] = u;
                    open.Add((alt, v.Id));
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
