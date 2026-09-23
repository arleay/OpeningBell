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
            /// <summary>Hanging around at a stand (a bus stop, outside a bar).</summary>
            Standing,
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
            /// <summary>Ground height under the walker (sidewalks climb hills and bridges).</summary>
            public float Y;
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
        private Vector2 _playerVelocity;

        /// <summary>Crosswalk rule supplied by the city: walk signal, or no traffic close by.</summary>
        public Func<SidewalkGraph.Crosswalk, bool> MayCross = _ => true;

        /// <summary>When true, walkers heading out prefer doors (the city wants fewer people).</summary>
        public bool Thinning { get; set; }

        /// <summary>The city's idea of where someone goes next (by the hour, what's open, what's close); null: anywhere.</summary>
        public Func<SidewalkGraph.Node, System.Random, bool, SidewalkGraph.Node> PickTarget;

        private readonly Dictionary<SidewalkGraph.Node, int> _standing = new Dictionary<SidewalkGraph.Node, int>();

        /// <summary>Walkers at (or heading for) a stand: it holds two, who face each other.</summary>
        public int AtStand(SidewalkGraph.Node stand) => _standing.TryGetValue(stand, out int n) ? n : 0;

        private void Release(Walker w)
        {
            if (w.Bench == null) return;
            if (w.Bench.Kind == PlaceKind.Stand) _standing[w.Bench] = Mathf.Max(0, AtStand(w.Bench) - 1);
            else _benchTaken.Remove(w.Bench);
            w.Bench = null;
        }

        private bool Free(SidewalkGraph.Node n) =>
            n.Kind == PlaceKind.Door || (n.Kind == PlaceKind.Stand ? AtStand(n) < 2 : !_benchTaken.Contains(n));

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
                    Y = door.Y,
                };
                if (!PlanFrom(w, door)) continue;
                _walkers.Add(w);
                return w;
            }
            return null;
        }

        public void Remove(Walker w)
        {
            Release(w);
            _walkers.Remove(w);
        }

        private bool PlanFrom(Walker w, SidewalkGraph.Node from)
        {
            SidewalkGraph.Node target = null;
            for (int i = 0; PickTarget != null && target == null && i < 3; i++)
            {
                SidewalkGraph.Node t = PickTarget(from, _rng, Thinning);
                if (t != null && t != from && Free(t)) target = t;
            }
            if (target == null && !Thinning && _rng.NextDouble() < 0.3)
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
            Release(w);
            w.Bench = target.Kind == PlaceKind.Bench || target.Kind == PlaceKind.Stand ? target : null;
            if (w.Bench != null && w.Bench.Kind == PlaceKind.Stand) _standing[w.Bench] = AtStand(w.Bench) + 1;
            else if (w.Bench != null) _benchTaken.Add(w.Bench);
            w.Route = route;
            w.Leg = 0;
            w.S = 0f;
            w.State = WalkerState.Walking;
            return true;
        }

        /// <param name="playerVelocity">So people can step aside for someone riding at them.</param>
        public void Step(float dt, Vector2 player, Vector2 playerVelocity = default)
        {
            _playerVelocity = playerVelocity;
            for (int i = _walkers.Count - 1; i >= 0; i--)
            {
                Walker w = _walkers[i];
                switch (w.State)
                {
                    case WalkerState.Sitting:
                    case WalkerState.Standing:
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

            // Step around the player instead of walking through them; get out of the way of a rider early.
            Vector2 toPlayer = player - w.Position;
            if (toPlayer.sqrMagnitude < 6f * 6f && toPlayer.sqrMagnitude > 1e-4f && Vector2.Dot(_playerVelocity, -toPlayer.normalized) > 2f)
                w.Swerve = Mathf.Max(w.Swerve, 1.6f);
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
            float length = Vector2.Distance(a.P, b.P);
            Vector2 target = a.P + dir * Mathf.Min(w.S, length) + RoadNetwork.RightOf(dir) * right;
            w.Y = Mathf.Lerp(a.Y, b.Y, length > 0f ? Mathf.Clamp01(w.S / length) : 1f);
            Vector2 step = target - w.Position;
            float max = w.Speed * 1.6f * dt;
            w.Position = step.magnitude > max ? w.Position + step.normalized * max : target;
            if (w.State == WalkerState.Walking && step.sqrMagnitude > 1e-4f) w.Heading = Vector2.Lerp(w.Heading, step.normalized, 0.2f).normalized;
            else if (w.State == WalkerState.Waiting) w.Heading = dir;
        }

        private void Arrive(Walker w)
        {
            w.Position = w.Destination.P;
            w.Y = w.Destination.Y;
            if (w.Destination.Kind == PlaceKind.Bench)
            {
                w.State = WalkerState.Sitting;
                w.Timer = 25f + 45f * (float)_rng.NextDouble();
            }
            else if (w.Destination.Kind == PlaceKind.Stand)
            {
                // Two at a stand stand a pace apart.
                if (AtStand(w.Destination) > 1) w.Position += RoadNetwork.RightOf(w.Heading) * 0.9f;
                w.State = WalkerState.Standing;
                w.Timer = 30f + 60f * (float)_rng.NextDouble();
            }
            else
            {
                w.State = WalkerState.Arrived; // inside the building: the city removes them
            }
        }
    }
}
