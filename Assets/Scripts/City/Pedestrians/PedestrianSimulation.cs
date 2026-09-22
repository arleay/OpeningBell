using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// A few convincing pedestrians rather than many broken ones (spec §12): each walks from a door to another
    /// door or a bench along the sidewalk graph, keeps to the right, waits for the walk signal (or a gap in
    /// traffic) at crosswalks and steps around the player.
    /// </summary>
    public sealed class PedestrianSimulation
    {
        public enum WalkerState
        {
            Walking,
            Waiting,
            Sitting,
            Arrived,
        }

        public sealed class Walker
        {
            public int Id;
            public int Seed;
            public List<SidewalkGraph.Node> Route;
            public int Leg;            // walking from Route[Leg] to Route[Leg + 1]
            public float S;            // along that leg
            public float Speed;
            public float KeepRight;    // metres right of the sidewalk centre line
            public Vector2 Position;
            public Vector2 Heading = Vector2.up;
            public WalkerState State;
            public float Timer;
            public float Blocked;
            public float Swerve;
            public SidewalkGraph.Node Bench;

            public SidewalkGraph.Node Destination => Route[Route.Count - 1];
        }

        private readonly SidewalkGraph _graph;
        private readonly System.Random _rng;
        private readonly List<Walker> _walkers = new List<Walker>();
        private readonly List<SidewalkGraph.Node> _doors = new List<SidewalkGraph.Node>();
        private readonly List<SidewalkGraph.Node> _benches = new List<SidewalkGraph.Node>();
        private readonly HashSet<SidewalkGraph.Node> _benchTaken = new HashSet<SidewalkGraph.Node>();
        private int _nextId;

        /// <summary>Crosswalk rule supplied by the city: walk signal, or no traffic close by.</summary>
        public Func<SidewalkGraph.Crosswalk, bool> MayCross = _ => true;

        /// <summary>When true, walkers heading out prefer doors (the city wants fewer people).</summary>
        public bool Thinning { get; set; }

        public IReadOnlyList<Walker> Walkers => _walkers;
        public SidewalkGraph Graph => _graph;

        public PedestrianSimulation(SidewalkGraph graph, int seed)
        {
            _graph = graph;
            _rng = new System.Random(seed);
            _doors.AddRange(graph.Places(PlaceKind.Door));
            _benches.AddRange(graph.Places(PlaceKind.Bench));
        }

        /// <summary>Someone steps out of a random door (accepted by <paramref name="allowed"/>).</summary>
        public Walker Spawn(Func<Vector2, bool> allowed = null)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                SidewalkGraph.Node door = _doors[_rng.Next(_doors.Count)];
                if (allowed != null && !allowed(door.P)) continue;
                var w = new Walker
                {
                    Id = _nextId++,
                    Seed = _rng.Next(),
                    Speed = 1.15f + 0.4f * (float)_rng.NextDouble(),
                    KeepRight = 0.35f + 0.8f * (float)_rng.NextDouble(),
                    Position = door.P,
                };
                if (!PlanFrom(w, door)) continue;
                _walkers.Add(w);
                return w;
            }
            return null;
        }

        public void Remove(Walker w)
        {
            if (w.Bench != null) _benchTaken.Remove(w.Bench);
            _walkers.Remove(w);
        }

        private bool PlanFrom(Walker w, SidewalkGraph.Node from)
        {
            SidewalkGraph.Node target = null;
            if (!Thinning && _rng.NextDouble() < 0.3)
            {
                SidewalkGraph.Node bench = _benches.Count > 0 ? _benches[_rng.Next(_benches.Count)] : null;
                if (bench != null && bench != from && !_benchTaken.Contains(bench)) target = bench;
            }
            for (int i = 0; target == null && i < 6; i++)
            {
                SidewalkGraph.Node door = _doors[_rng.Next(_doors.Count)];
                if (door != from && Vector2.Distance(door.P, from.P) > 25f) target = door;
            }
            if (target == null) return false;
            List<SidewalkGraph.Node> route = _graph.Route(from, target);
            if (route == null || route.Count < 2) return false;
            if (w.Bench != null) _benchTaken.Remove(w.Bench);
            w.Bench = target.Kind == PlaceKind.Bench ? target : null;
            if (w.Bench != null) _benchTaken.Add(w.Bench);
            w.Route = route;
            w.Leg = 0;
            w.S = 0f;
            w.State = WalkerState.Walking;
            return true;
        }

        public void Step(float dt, Vector2 player)
        {
            for (int i = _walkers.Count - 1; i >= 0; i--)
            {
                Walker w = _walkers[i];
                switch (w.State)
                {
                    case WalkerState.Sitting:
                        w.Timer -= dt;
                        if (w.Timer <= 0f && !PlanFrom(w, w.Destination)) w.Timer = 5f;
                        break;
                    case WalkerState.Arrived:
                        break;
                    default:
                        Walk(w, dt, player);
                        break;
                }
            }
        }

        private void Walk(Walker w, float dt, Vector2 player)
        {
            SidewalkGraph.Node a = w.Route[w.Leg], b = w.Route[w.Leg + 1];
            SidewalkGraph.Edge edge = SidewalkGraph.EdgeBetween(a, b);

            // Kerbside: wait for the signal or a gap before stepping onto a crosswalk.
            if (w.S <= 0.01f && edge.Crosswalk != null)
            {
                bool go = MayCross(edge.Crosswalk);
                w.State = go ? WalkerState.Walking : WalkerState.Waiting;
                if (!go)
                {
                    Settle(w, a, b, dt);
                    return;
                }
            }

            // Step around the player instead of walking through them.
            Vector2 toPlayer = player - w.Position;
            bool blocked = toPlayer.sqrMagnitude < 0.95f * 0.95f && Vector2.Dot(toPlayer, w.Heading) > 0f;
            w.Blocked = blocked ? w.Blocked + dt : 0f;
            if (w.Blocked > 1f) w.Swerve = 1.2f;
            if (blocked && w.Swerve <= 0f) return;

            w.S += w.Speed * dt;
            w.Swerve = Mathf.Max(0f, w.Swerve - dt * 0.5f);
            if (w.S >= edge.Length)
            {
                w.S -= edge.Length;
                w.Leg++;
                if (w.Leg >= w.Route.Count - 1)
                {
                    Arrive(w);
                    return;
                }
                a = w.Route[w.Leg];
                b = w.Route[w.Leg + 1];
                if (SidewalkGraph.EdgeBetween(a, b).Crosswalk != null) w.S = 0f; // stop at the kerb to check
            }
            Settle(w, a, b, dt);
        }

        /// <summary>Moves the body toward its point on the path (kept right of the centre line); smooths corners.</summary>
        private void Settle(Walker w, SidewalkGraph.Node a, SidewalkGraph.Node b, float dt)
        {
            Vector2 dir = (b.P - a.P).normalized;
            bool spur = a.Kind != PlaceKind.Walkway || b.Kind != PlaceKind.Walkway;
            float right = spur ? 0f : w.KeepRight + w.Swerve;
            Vector2 target = a.P + dir * Mathf.Min(w.S, Vector2.Distance(a.P, b.P)) + RoadNetwork.RightOf(dir) * right;
            Vector2 step = target - w.Position;
            float max = w.Speed * 1.6f * dt;
            w.Position = step.magnitude > max ? w.Position + step.normalized * max : target;
            if (w.State == WalkerState.Walking && step.sqrMagnitude > 1e-4f) w.Heading = Vector2.Lerp(w.Heading, step.normalized, 0.2f).normalized;
            else if (w.State == WalkerState.Waiting) w.Heading = dir;
        }

        private void Arrive(Walker w)
        {
            w.Position = w.Destination.P;
            if (w.Destination.Kind == PlaceKind.Bench)
            {
                w.State = WalkerState.Sitting;
                w.Timer = 25f + 45f * (float)_rng.NextDouble();
            }
            else
            {
                w.State = WalkerState.Arrived; // inside the building: the city removes them
            }
        }
    }
}
