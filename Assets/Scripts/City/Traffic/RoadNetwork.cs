using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    public enum Signal
    {
        Green,
        Yellow,
        Red,
    }

    /// <summary>
    /// Lanes and junction movements for traffic, built from <see cref="StreetMap"/>. Right-hand traffic, one lane
    /// each way. Positions are 2D (x, z) with a height alongside. A lane runs stop line to stop line; a movement is
    /// the curve through a junction (or round a bend) from one lane's end to another lane's start. Movements whose
    /// curves come within a car width of each other conflict, which is all the right-of-way logic needs. Dead ends
    /// turn cars round. Alleys and dirt roads carry no traffic.
    /// </summary>
    public sealed class RoadNetwork
    {
        public sealed class Node
        {
            public int Id;
            public string Name;
            public Vector2 P;
            public float Y;
            public NodeControl Control;
            public string MainStreet;
            public StreetMap.Node Map;
            public readonly List<Lane> Incoming = new List<Lane>();
            public readonly List<Lane> Outgoing = new List<Lane>();
            public readonly List<Movement> Movements = new List<Movement>();
            public LightCycle Lights;
            public bool IsJunction => Map.IsJunction;
        }

        public sealed class Lane
        {
            public int Id;
            public string Street;
            public Node From, To;
            public Vector2 Start, End, Dir;
            public float Length;
            public float StartY, EndY;
            /// <summary>0 or 1: the light phase this lane gets green in at its end node.</summary>
            public int Phase;
            /// <summary>On the minor street at its end node (it stops or yields there).</summary>
            public bool IsStem;
            public StreetMap.Segment Segment;
            public readonly List<Movement> Exits = new List<Movement>();

            public Vector2 At(float s) => Start + Dir * s;
            public float HeightAt(float s) => Mathf.Lerp(StartY, EndY, Length > 0f ? s / Length : 0f);
        }

        public enum Turn
        {
            Straight,
            Left,
            Right,
            UTurn,
        }

        public sealed class Movement
        {
            public int Id;
            public Node Node;
            public Lane In, Out;
            public Turn Turn;
            /// <summary>Right of way: through and right on the main street 2, main-street left 1, minor street 0.</summary>
            public int Priority;
            public float Length;
            public readonly List<Movement> Conflicts = new List<Movement>();

            private Vector2 _p0, _p1, _p2;
            private float[] _arc; // cumulative length at uniform t samples

            internal void Build(Vector2 p0, Vector2 p1, Vector2 p2)
            {
                _p0 = p0;
                _p1 = p1;
                _p2 = p2;
                const int n = 24;
                _arc = new float[n + 1];
                Vector2 prev = p0;
                for (int i = 1; i <= n; i++)
                {
                    Vector2 p = Bezier((float)i / n);
                    _arc[i] = _arc[i - 1] + Vector2.Distance(prev, p);
                    prev = p;
                }
                Length = _arc[n];
            }

            /// <summary>Point at arc length <paramref name="s"/> (clamped).</summary>
            public Vector2 At(float s) => Bezier(ParamAt(s));

            public float HeightAt(float s) => Mathf.Lerp(In.EndY, Out.StartY, Length > 0f ? Mathf.Clamp01(s / Length) : 0f);

            public Vector2 TangentAt(float s)
            {
                float t = ParamAt(s);
                Vector2 d = 2f * (1f - t) * (_p1 - _p0) + 2f * t * (_p2 - _p1);
                return d.sqrMagnitude > 1e-6f ? d.normalized : In.Dir;
            }

            private float ParamAt(float s)
            {
                if (s <= 0f) return 0f;
                if (s >= Length) return 1f;
                int n = _arc.Length - 1;
                int i = 1;
                while (_arc[i] < s) i++;
                float k = (s - _arc[i - 1]) / Mathf.Max(1e-5f, _arc[i] - _arc[i - 1]);
                return (i - 1 + k) / n;
            }

            private Vector2 Bezier(float t)
            {
                float u = 1f - t;
                return u * u * _p0 + 2f * u * t * _p1 + t * t * _p2;
            }
        }

        /// <summary>Two-phase signal: phase 0 is the main street, phase 1 the cross street. Times in world seconds.</summary>
        public sealed class LightCycle
        {
            public float MainGreen = 24f, SideGreen = 14f, Yellow = 3f, AllRed = 2f;
            public float Period => MainGreen + SideGreen + 2f * (Yellow + AllRed);

            public Signal SignalFor(int phase, float time) => SignalFor(phase, time, out _);

            /// <summary>Signal for a phase and how many seconds it stays that way.</summary>
            public Signal SignalFor(int phase, float time, out float remaining)
            {
                float t = Mathf.Repeat(time, Period);
                float mainEnd = MainGreen, mainYellow = mainEnd + Yellow, sideStart = mainYellow + AllRed;
                float sideEnd = sideStart + SideGreen, sideYellow = sideEnd + Yellow;
                if (phase == 0)
                {
                    if (t < mainEnd) { remaining = mainEnd - t; return Signal.Green; }
                    if (t < mainYellow) { remaining = mainYellow - t; return Signal.Yellow; }
                    remaining = Period - t;
                    return Signal.Red;
                }
                if (t < sideStart) { remaining = sideStart - t; return Signal.Red; }
                if (t < sideEnd) { remaining = sideEnd - t; return Signal.Green; }
                if (t < sideYellow) { remaining = sideYellow - t; return Signal.Yellow; }
                remaining = Period - t + 0f;
                return Signal.Red;
            }
        }

        public readonly List<Node> Nodes = new List<Node>();
        public readonly List<Lane> Lanes = new List<Lane>();
        public readonly List<Movement> Movements = new List<Movement>();
        public StreetMap Map { get; private set; }

        public static RoadNetwork FromPlan() => From(StreetMap.Plan);

        public static RoadNetwork From(StreetMap map)
        {
            var net = new RoadNetwork { Map = map };
            var nodes = new Dictionary<StreetMap.Node, Node>();
            foreach (StreetMap.Node m in map.Nodes)
            {
                bool traffic = m.Approaches.Exists(a => a.Segment.Traffic);
                if (!traffic) continue;
                var n = new Node { Id = net.Nodes.Count, Name = m.Name, P = m.P, Y = m.Grade + CityPlan.RoadY, Control = m.Control, MainStreet = m.MainStreet, Map = m };
                if (n.Control == NodeControl.Lights) n.Lights = new LightCycle();
                nodes[m] = n;
                net.Nodes.Add(n);
            }
            foreach (StreetMap.Segment s in map.Segments)
            {
                if (!s.Traffic) continue;
                net.AddLane(s, nodes[s.A], nodes[s.B], forward: true);
                net.AddLane(s, nodes[s.B], nodes[s.A], forward: false);
            }
            foreach (Node node in net.Nodes) net.BuildJunction(node);
            return net;
        }

        /// <summary>Right-hand side of a travel direction (x east, y north).</summary>
        public static Vector2 RightOf(Vector2 dir) => new Vector2(dir.y, -dir.x);

        private void AddLane(StreetMap.Segment seg, Node from, Node to, bool forward)
        {
            StreetMap.Approach start = forward ? seg.AtA : seg.AtB, end = forward ? seg.AtB : seg.AtA;
            Vector2 dir = forward ? seg.Dir : -seg.Dir;
            Vector2 offset = RightOf(dir) * CityPlan.LaneOffsetFor(seg.Class);
            float s0 = start.StopLine, s1 = seg.Length - end.StopLine;
            var lane = new Lane
            {
                Id = Lanes.Count,
                Street = seg.Street,
                Segment = seg,
                From = from,
                To = to,
                Dir = dir,
                Start = from.P + dir * s0 + offset,
                End = from.P + dir * s1 + offset,
                StartY = start.GradeAt(s0) + CityPlan.RoadY,
                EndY = start.GradeAt(s1) + CityPlan.RoadY,
            };
            lane.Length = Mathf.Max(0.5f, s1 - s0);
            Lanes.Add(lane);
            from.Outgoing.Add(lane);
            to.Incoming.Add(lane);
        }

        private void BuildJunction(Node node)
        {
            bool deadEnd = node.Incoming.Count == 1 && node.Outgoing.Count == 1;
            foreach (Lane lane in node.Incoming)
            {
                StreetMap.Approach arriving = lane.Segment.B == node.Map ? lane.Segment.AtB : lane.Segment.AtA;
                lane.IsStem = node.IsJunction && !node.Map.IsMain(arriving);
                lane.Phase = lane.IsStem ? 1 : 0;
                foreach (Lane exit in node.Outgoing)
                {
                    bool uTurn = exit.To == lane.From && exit.Segment == lane.Segment;
                    if (uTurn && !deadEnd) continue; // U-turns only where the road ends
                    var m = new Movement { Id = Movements.Count, Node = node, In = lane, Out = exit };
                    float cross = lane.Dir.x * exit.Dir.y - lane.Dir.y * exit.Dir.x;
                    float dot = Vector2.Dot(lane.Dir, exit.Dir);
                    m.Turn = uTurn ? Turn.UTurn : dot > 0.7f ? Turn.Straight : cross < 0f ? Turn.Right : Turn.Left;
                    m.Priority = !node.IsJunction ? 1 : lane.IsStem ? 0 : m.Turn == Turn.Left ? 1 : 2;
                    Vector2 control = uTurn ? node.P + lane.Dir * 7f : Control(lane, exit);
                    m.Build(lane.End, control, exit.Start);
                    lane.Exits.Add(m);
                    node.Movements.Add(m);
                    Movements.Add(m);
                }
            }

            // Conflicts: curves closer than a car width plus margin anywhere along them.
            const float clearance = 2.4f;
            List<Movement> ms = node.Movements;
            for (int i = 0; i < ms.Count; i++)
            for (int j = i + 1; j < ms.Count; j++)
            {
                if (ms[i].In == ms[j].In) continue; // same queue: ordinary following handles it
                if (MinDistance(ms[i], ms[j]) < clearance)
                {
                    ms[i].Conflicts.Add(ms[j]);
                    ms[j].Conflicts.Add(ms[i]);
                }
            }
        }

        /// <summary>Bezier control point: where the incoming and outgoing lane lines meet (midpoint for straights).</summary>
        private static Vector2 Control(Lane a, Lane b)
        {
            float denom = a.Dir.x * b.Dir.y - a.Dir.y * b.Dir.x;
            if (Mathf.Abs(denom) < 1e-2f) return (a.End + b.Start) * 0.5f;
            Vector2 d = b.Start - a.End;
            float t = (d.x * b.Dir.y - d.y * b.Dir.x) / denom;
            return a.End + a.Dir * t;
        }

        private static float MinDistance(Movement a, Movement b)
        {
            float best = float.MaxValue;
            for (float s = 0f; s <= a.Length; s += 0.5f)
            {
                Vector2 p = a.At(s);
                for (float u = 0f; u <= b.Length; u += 0.5f) best = Mathf.Min(best, Vector2.Distance(p, b.At(u)));
            }
            return best;
        }

        public Node FindNode(string name) => Nodes.Find(n => n.Name == name) ?? throw new ArgumentException(name);
    }
}
