using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Lightweight traffic (spec §11): cars follow lanes, stop at red lights and stop signs, yield by right of way,
    /// keep their distance and stop for people in front of them. Kinematic, deterministic for a given seed and
    /// step sequence, and independent of GameObjects so it can be tested headless.
    /// </summary>
    public sealed class TrafficSimulation
    {
        public const float CarLength = 4.4f;
        public const float Cruise = 11f;       // m/s, ~40 km/h in town
        public const float TurnSpeed = 5.5f;
        public const float Accel = 2.6f;
        public const float Brake = 5f;
        public const float MaxBrake = 9f;
        private const float FollowGap = CarLength + 2f;   // centre to centre
        private const float PersonGap = CarLength / 2f + 1.6f;
        private const float StopLineGap = CarLength / 2f;
        private const float LookAhead = 30f;
        private const float ExitClearance = 7.5f;
        private const float YieldHorizon = 32f;

        public sealed class Car
        {
            public int Id;
            public RoadNetwork.Lane Lane;          // on a lane…
            public RoadNetwork.Movement Move;      // …or inside a junction
            public RoadNetwork.Movement Next;      // planned movement at the end of the current lane
            public float S;
            public float Speed;
            public float CruiseSpeed;
            public bool StoppedAtSign;
            public bool Committed;
            /// <summary>Movement this car still occupies (inside the junction or just past it).</summary>
            public RoadNetwork.Movement Occupying;
            public Vector2 Position;
            public Vector2 Heading;
            /// <summary>Road surface height under the car.</summary>
            public float Y;
            public float Odometer;
            /// <summary>How far the car may go before it must be stopped (last step).</summary>
            public float Free = float.MaxValue;
            public int ColorSeed;
            /// <summary>Delivery vans: now and then they stop in the lane outside a business for a while.</summary>
            public bool MakesDeliveries;
            /// <summary>A stop planned on the current lane (distance along it), and how long to wait there.</summary>
            public float StopAtS = -1f, DwellLeft;
            public bool Delivering => StopAtS >= 0f && DwellLeft > 0f && Speed < 0.2f && Lane != null && Mathf.Abs(S - StopAtS) < 1f;
        }

        /// <summary>Seconds a delivery takes; how likely a van is to stop on a quiet street it turns into.</summary>
        public const float DeliverySeconds = 22f;
        public const double DeliveryChance = 0.18;

        private readonly RoadNetwork _net;
        private readonly System.Random _rng;
        private readonly List<Car> _cars = new List<Car>();
        private readonly Dictionary<RoadNetwork.Node, List<Car>> _occupants = new Dictionary<RoadNetwork.Node, List<Car>>();
        private readonly List<Vector2> _samples = new List<Vector2>(40);
        private int _nextId;
        private IReadOnlyList<Vector2> _otherCars;

        public RoadNetwork Network => _net;
        public IReadOnlyList<Car> Cars => _cars;
        /// <summary>World seconds; drives the traffic lights.</summary>
        public float Time { get; private set; }

        public TrafficSimulation(RoadNetwork network, int seed)
        {
            _net = network;
            _rng = new System.Random(seed);
            foreach (RoadNetwork.Node n in network.Nodes) _occupants[n] = new List<Car>();
        }

        public Signal SignalFor(RoadNetwork.Lane incoming) =>
            incoming.To.Lights == null ? Signal.Green : incoming.To.Lights.SignalFor(incoming.Phase, Time);

        // ---- population ----

        /// <summary>Places a car on a free stretch of lane accepted by <paramref name="allowed"/>. Null if none found.</summary>
        public Car Spawn(Func<Vector2, bool> allowed)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                RoadNetwork.Lane lane = _net.Lanes[_rng.Next(_net.Lanes.Count)];
                if (lane.Length < 30f) continue;
                float s = 6f + (float)_rng.NextDouble() * (lane.Length - 22f);
                Vector2 p = lane.At(s);
                if (allowed != null && !allowed(p)) continue;
                if (IsCrowded(p, 18f)) continue;
                var car = new Car
                {
                    Id = _nextId++,
                    Lane = lane,
                    S = s,
                    CruiseSpeed = Cruise * (0.88f + 0.2f * (float)_rng.NextDouble()),
                    ColorSeed = _rng.Next(),
                };
                car.Speed = car.CruiseSpeed * 0.7f;
                car.Next = ChooseExit(lane);
                UpdatePose(car);
                _cars.Add(car);
                return car;
            }
            return null;
        }

        public void Despawn(Car car)
        {
            Release(car);
            _cars.Remove(car);
        }

        private bool IsCrowded(Vector2 p, float radius)
        {
            foreach (Car c in _cars)
                if ((c.Position - p).sqrMagnitude < radius * radius) return true;
            return false;
        }

        private RoadNetwork.Movement ChooseExit(RoadNetwork.Lane lane)
        {
            List<RoadNetwork.Movement> exits = lane.Exits;
            if (exits.Count == 1) return exits[0];
            // Mostly straight on, sometimes turn: keeps traffic spread over the grid.
            double roll = _rng.NextDouble();
            RoadNetwork.Movement straight = exits.Find(m => m.Turn == RoadNetwork.Turn.Straight);
            if (straight != null && roll < 0.55) return straight;
            var turns = exits.FindAll(m => m.Turn != RoadNetwork.Turn.Straight);
            // A fork where every exit runs roughly straight on: pick any.
            if (turns.Count == 0) turns = exits;
            return turns[_rng.Next(turns.Count)];
        }

        // ---- step ----

        /// <param name="people">Positions of pedestrians and the player: cars stop for anyone in front of them.</param>
        /// <param name="otherCars">Cars outside the simulation (the player's, parked ones): followed like traffic.</param>
        private float _dt;

        public void Step(float dt, IReadOnlyList<Vector2> people, IReadOnlyList<Vector2> otherCars = null)
        {
            _dt = dt;
            Time += dt;
            _otherCars = otherCars;
            foreach (Car car in _cars)
            {
                float target = TargetSpeed(car, people);
                // Gentle acceleration; braking may be firm (the target curve itself asks for ~Brake).
                car.Speed = Mathf.MoveTowards(car.Speed, target, (target > car.Speed ? Accel : MaxBrake) * dt);
            }
            // Never step past the free distance: fixed-step integration would otherwise creep over stop lines.
            foreach (Car car in _cars) Advance(car, Mathf.Min(car.Speed * dt, Mathf.Max(0f, car.Free)));
        }

        private float TargetSpeed(Car car, IReadOnlyList<Vector2> people)
        {
            float cruise = car.Move != null ? Mathf.Min(car.CruiseSpeed, car.Move.Turn == RoadNetwork.Turn.Straight ? car.CruiseSpeed : TurnSpeed)
                : car.CruiseSpeed;
            float free = float.MaxValue;

            // Committed on green but held up since: if the light changed and we can still stop, stop.
            if (car.Lane != null && car.Committed && car.Next.Node.Lights != null && SignalFor(car.Lane) != Signal.Green)
            {
                float toLine = car.Lane.Length - car.S - StopLineGap;
                if (car.Speed * car.Speed / (2f * MaxBrake) <= toLine)
                {
                    car.Committed = false;
                    if (car.Occupying == car.Next) Release(car);
                }
            }

            // Stop line: decide whether we may enter the junction.
            if (car.Lane != null && !car.Committed)
            {
                float toLine = car.Lane.Length - car.S - StopLineGap;
                if (car.Next.Node.Control == NodeControl.StopOnStem && car.Lane.IsStem && car.Speed < 0.3f && toLine < 1.2f)
                    car.StoppedAtSign = true;
                float stoppingDistance = car.Speed * car.Speed / (2f * Brake) + 1.5f;
                if (toLine <= stoppingDistance)
                {
                    if (MayEnter(car, toLine, people))
                    {
                        // Reserve now, not on entry: otherwise two cars can commit to conflicting paths at once.
                        car.Committed = true;
                        Reserve(car, car.Next);
                    }
                    else free = Mathf.Min(free, toLine);
                }
                // Slow for the turn ahead.
                if (car.Next.Turn != RoadNetwork.Turn.Straight)
                    cruise = Mathf.Min(cruise, Mathf.Sqrt(TurnSpeed * TurnSpeed + 2f * Brake * 0.5f * Mathf.Max(0f, toLine)));
            }

            // Everything on the path ahead: cars and people.
            SamplePathAhead(car);
            foreach (Car other in _cars)
                if (other != car) free = Mathf.Min(free, AlongPath(other.Position, 1.6f) - FollowGap);
            if (_otherCars != null)
                foreach (Vector2 p in _otherCars) free = Mathf.Min(free, AlongPath(p, 1.6f) - FollowGap);
            if (people != null)
                foreach (Vector2 p in people) free = Mathf.Min(free, AlongPath(p, 1.9f) - PersonGap);

            // A delivery stop ahead on this lane: pull up there, wait, carry on.
            if (car.Lane != null && car.StopAtS >= 0f)
            {
                float toStop = car.StopAtS - car.S;
                if (toStop < -0.5f) car.StopAtS = -1f;
                else if (toStop < 0.3f && car.Speed < 0.2f)
                {
                    car.DwellLeft -= _dt;
                    if (car.DwellLeft <= 0f) car.StopAtS = -1f;
                    else free = 0f;
                }
                else free = Mathf.Min(free, toStop);
            }

            car.Free = free;
            if (free <= 0f) return 0f;
            return Mathf.Min(cruise, Mathf.Sqrt(2f * Brake * free));
        }

        /// <summary>Right of way at the end of the car's lane.</summary>
        private bool MayEnter(Car car, float toLine, IReadOnlyList<Vector2> people)
        {
            RoadNetwork.Movement m = car.Next;
            RoadNetwork.Node node = m.Node;

            // Someone on the crosswalks (anywhere along the curve): wait at the line rather than stop in their way.
            if (people != null)
                for (float s = 0f; s <= m.Length; s += 1.5f)
                {
                    Vector2 q = m.At(s);
                    foreach (Vector2 p in people)
                        if ((p - q).sqrMagnitude < 2.2f * 2.2f) return false;
                }

            if (node.Lights != null)
            {
                Signal s = node.Lights.SignalFor(car.Lane.Phase, Time);
                bool cannotStop = car.Speed * car.Speed / (2f * MaxBrake) > toLine;
                if (s == Signal.Red || (s == Signal.Yellow && !cannotStop)) return false;
            }
            if (node.Control == NodeControl.StopOnStem && car.Lane.IsStem && !car.StoppedAtSign) return false;

            // Don't block the box: the exit lane must have room.
            foreach (Car other in _cars)
                if (other.Lane == m.Out && other.S < ExitClearance) return false;

            foreach (Car other in _occupants[node])
                if (other != car && m.Conflicts.Contains(other.Occupying)) return false;

            // Yield to higher-priority traffic that is about to arrive on a conflicting movement.
            foreach (Car other in _cars)
            {
                if (other == car || other.Lane == null || other.Next == null || other.Next.Node != node) continue;
                if (other.Next.Priority <= m.Priority || !m.Conflicts.Contains(other.Next)) continue;
                if (node.Lights != null && node.Lights.SignalFor(other.Lane.Phase, Time) == Signal.Red) continue;
                float otherToLine = other.Lane.Length - other.S;
                if (otherToLine < YieldHorizon && (other.Speed > 1f || other.Committed)) return false;
            }
            return true;
        }

        private void SamplePathAhead(Car car)
        {
            _samples.Clear();
            RoadNetwork.Lane lane = car.Lane;
            RoadNetwork.Movement move = car.Move;
            RoadNetwork.Movement next = car.Lane != null ? car.Next : null; // the exit after a junction isn't planned yet
            float s = car.S;
            for (int i = 0; i < LookAhead; i++)
            {
                s += 1f;
                // Walk the piece chain: lane → movement → lane.
                while (s > (lane != null ? lane.Length : move.Length))
                {
                    s -= lane != null ? lane.Length : move.Length;
                    if (lane != null)
                    {
                        if (next == null) return;
                        move = next;
                        lane = null;
                        next = null;
                    }
                    else
                    {
                        lane = move.Out;
                        move = null;
                    }
                }
                _samples.Add(lane != null ? lane.At(s) : move.At(s));
            }
        }

        /// <summary>Distance along the sampled path at which <paramref name="p"/> comes within <paramref name="width"/>.</summary>
        private float AlongPath(Vector2 p, float width)
        {
            float w2 = width * width;
            for (int i = 0; i < _samples.Count; i++)
                if ((_samples[i] - p).sqrMagnitude < w2) return i + 1f;
            return float.MaxValue;
        }

        private void Advance(Car car, float distance)
        {
            car.Odometer += distance;
            car.S += distance;
            while (true)
            {
                if (car.Lane != null)
                {
                    // Clear of the junction just passed (not the reservation for the one ahead).
                    if (car.Occupying != null && car.Occupying.Out == car.Lane && car.S > ExitClearance) Release(car);
                    if (car.S <= car.Lane.Length) break;
                    // Into the junction.
                    car.S -= car.Lane.Length;
                    car.Move = car.Next;
                    car.Lane = null;
                    car.Committed = false;
                    car.StoppedAtSign = false;
                    if (car.Occupying != car.Move) Reserve(car, car.Move);
                }
                else
                {
                    if (car.S <= car.Move.Length) break;
                    car.S -= car.Move.Length;
                    car.Lane = car.Move.Out;
                    car.Move = null;
                    car.Next = ChooseExit(car.Lane);
                    // Vans stop on quieter streets (not the highway or through Maple's lights), mid-block.
                    if (car.MakesDeliveries && car.Lane.Length > 50f && car.Lane.Segment.Class != RoadClass.Highway && car.Lane.Street != "MAPLE ST"
                        && _rng.NextDouble() < DeliveryChance)
                    {
                        car.StopAtS = car.Lane.Length * (0.35f + 0.3f * (float)_rng.NextDouble());
                        car.DwellLeft = DeliverySeconds;
                    }
                }
            }
            UpdatePose(car);
        }

        private void Reserve(Car car, RoadNetwork.Movement m)
        {
            Release(car);
            car.Occupying = m;
            _occupants[m.Node].Add(car);
        }

        private void Release(Car car)
        {
            if (car.Occupying == null) return;
            _occupants[car.Occupying.Node].Remove(car);
            car.Occupying = null;
        }

        private static void UpdatePose(Car car)
        {
            if (car.Lane != null)
            {
                car.Position = car.Lane.At(car.S);
                car.Heading = car.Lane.Dir;
                car.Y = car.Lane.HeightAt(car.S);
            }
            else
            {
                car.Position = car.Move.At(car.S);
                car.Heading = car.Move.TangentAt(car.S);
                car.Y = car.Move.HeightAt(car.S);
            }
        }

        /// <summary>True when a car is within <paramref name="radius"/> of <paramref name="p"/> and moving (for pedestrians crossing).</summary>
        public bool AnyCarNear(Vector2 p, float radius)
        {
            float r2 = radius * radius;
            foreach (Car c in _cars)
                if ((c.Position - p).sqrMagnitude < r2) return true;
            if (_otherCars != null)
                foreach (Vector2 o in _otherCars)
                    if ((o - p).sqrMagnitude < r2) return true;
            return false;
        }
    }
}
