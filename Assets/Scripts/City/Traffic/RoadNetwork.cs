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
    /// Lanes and junction movements derived from <see cref="CityPlan"/>. Right-hand traffic, one lane each way.
    /// Positions are 2D (x, z). A lane runs stop line to stop line; a movement is the curve through a junction
    /// from one lane's end to another lane's start. Movements whose curves come within a car width of each other
    /// conflict, which is all the right-of-way logic needs.
    /// </summary>
    public sealed class RoadNetwork
    {
        public sealed class Node
        {
            public int Id;
            public string Name;
            public Vector2 P;
            public NodeControl Control;
            public readonly List<Lane> Incoming = new List<Lane>();
            public readonly List<Lane> Outgoing = new List<Lane>();
            public readonly List<Movement> Movements = new List<Movement>();
            public LightCycle Lights;
            /// <summary>Direction (from the node) of the T junction's stem, or zero.</summary>
            public Vector2 StemDir;
        }

        public sealed class Lane
        {
            public int Id;
            public string Street;
            public Node From, To;
            public Vector2 Start, End, Dir;
            public float Length;
            /// <summary>0 or 1: the light phase this lane gets green in at its end node.</summary>
            public int Phase;
            public bool IsStem;
            public readonly List<Movement> Exits = new List<Movement>();

            public Vector2 At(float s) => Start + Dir * s;
        }

        public enum Turn
        {
            Straight,
            Left,
            Right,
        }

        public sealed class Movement
        {
            public int Id;
            public Node Node;
            public Lane In, Out;
            public Turn Turn;
            /// <summary>Right of way: through and right on the main street 2, main-street left 1, stem 0.</summary>
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

        /// <summary>Two-phase signal: phase 0 is the through street, phase 1 the stem. Times in world seconds.</summary>
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

        public static RoadNetwork FromPlan()
        {
            var net = new RoadNetwork();
            foreach (var (name, p, control) in CityPlan.Nodes)
                net.Nodes.Add(new Node { Id = net.Nodes.Count, Name = name, P = p, Control = control });
            foreach (var (a, b, street) in CityPlan.Streets)
            {
                net.AddLane(net.Nodes[a], net.Nodes[b], street);
                net.AddLane(net.Nodes[b], net.Nodes[a], street);
            }
            foreach (Node node in net.Nodes) net.BuildJunction(node);
            return net;
        }

        /// <summary>Right-hand side of a travel direction (x east, y north).</summary>
        public static Vector2 RightOf(Vector2 dir) => new Vector2(dir.y, -dir.x);

        private void AddLane(Node from, Node to, string street)
        {
            Vector2 dir = (to.P - from.P).normalized;
            Vector2 offset = RightOf(dir) * CityPlan.LaneOffset;
            var lane = new Lane
            {
                Id = Lanes.Count,
                Street = street,
                From = from,
                To = to,
                Dir = dir,
                Start = from.P + dir * CityPlan.StopLine + offset,
                End = to.P - dir * CityPlan.StopLine + offset,
            };
            lane.Length = Vector2.Distance(lane.Start, lane.End);
            Lanes.Add(lane);
            from.Outgoing.Add(lane);
            to.Incoming.Add(lane);
        }

        private void BuildJunction(Node node)
        {
            // T junction: the stem is the outgoing direction with no opposite.
            if (node.Outgoing.Count == 3)
                foreach (Lane o in node.Outgoing)
                {
                    bool hasOpposite = node.Outgoing.Exists(x => Vector2.Dot(x.Dir, o.Dir) < -0.9f);
                    if (!hasOpposite) node.StemDir = o.Dir;
                }
            if (node.Control == NodeControl.Lights) node.Lights = new LightCycle();

            foreach (Lane lane in node.Incoming)
            {
                // An incoming lane on the stem travels against the stem direction.
                lane.IsStem = node.StemDir != Vector2.zero && Vector2.Dot(lane.Dir, node.StemDir) < -0.9f;
                lane.Phase = lane.IsStem ? 1 : 0;
                foreach (Lane exit in node.Outgoing)
                {
                    if (exit.To == lane.From) continue; // no U-turns
                    var m = new Movement { Id = Movements.Count, Node = node, In = lane, Out = exit };
                    float cross = lane.Dir.x * exit.Dir.y - lane.Dir.y * exit.Dir.x;
                    m.Turn = Vector2.Dot(lane.Dir, exit.Dir) > 0.7f ? Turn.Straight : cross < 0f ? Turn.Right : Turn.Left;
                    m.Priority = node.StemDir == Vector2.zero ? 1 : lane.IsStem ? 0 : m.Turn == Turn.Left ? 1 : 2;
                    m.Build(lane.End, Control(lane, exit), exit.Start);
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
            if (Mathf.Abs(denom) < 1e-4f) return (a.End + b.Start) * 0.5f;
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
