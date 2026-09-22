using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpeningBell.City;
using UnityEngine;

namespace OpeningBell.Tests
{
    public class CityLogicTests
    {
        // ---- roads ----

        [Test]
        public void RoadNetwork_FromPlan_IsConnected_WithJunctionRules()
        {
            RoadNetwork net = RoadNetwork.FromPlan();
            Assert.AreEqual(CityPlan.Streets.Length * 2, net.Lanes.Count);

            // Every lane reachable from every other by driving.
            var seen = new HashSet<RoadNetwork.Lane> { net.Lanes[0] };
            var queue = new Queue<RoadNetwork.Lane>(seen);
            while (queue.Count > 0)
                foreach (RoadNetwork.Movement m in queue.Dequeue().Exits)
                    if (seen.Add(m.Out)) queue.Enqueue(m.Out);
            Assert.AreEqual(net.Lanes.Count, seen.Count, "all lanes reachable");

            RoadNetwork.Node lights = net.FindNode("Maple & Exchange");
            Assert.IsNotNull(lights.Lights);
            Assert.AreEqual(6, lights.Movements.Count, "T junction: 3 approaches × 2 exits");
            Assert.AreEqual(2, net.FindNode("Maple & Cedar").Movements.Count, "corner: one turn each way");

            // Left from Maple into the stem crosses the opposing through lane; the two throughs don't meet.
            RoadNetwork.Movement[] through = lights.Movements.Where(m => m.Turn == RoadNetwork.Turn.Straight).ToArray();
            Assert.AreEqual(2, through.Length);
            CollectionAssert.DoesNotContain(through[0].Conflicts, through[1]);
            foreach (RoadNetwork.Movement left in lights.Movements.Where(m => m.Turn == RoadNetwork.Turn.Left && !m.In.IsStem))
                Assert.IsTrue(left.Conflicts.Any(c => c.Turn == RoadNetwork.Turn.Straight && Vector2.Dot(c.In.Dir, left.In.Dir) < -0.9f));
            Assert.IsTrue(lights.Movements.Where(m => m.In.IsStem).All(m => m.Priority == 0));
        }

        [Test]
        public void Traffic_TwentyMinutes_NoCollisionsNoGridlockNoRedLights()
        {
            var sim = new TrafficSimulation(RoadNetwork.FromPlan(), seed: 11);
            for (int i = 0; i < 18; i++) Assert.NotNull(sim.Spawn(null), "spawn " + i);
            // Front bumper's distance to the stop line, per car, while approaching the lights.
            var toLine = new Dictionary<TrafficSimulation.Car, float>();
            int redRuns = 0, junctionEntries = 0;
            string firstRun = null;

            for (int step = 0; step < 24000; step++) // 20 minutes at 0.05 s
            {
                sim.Step(0.05f, null);
                foreach (TrafficSimulation.Car car in sim.Cars)
                {
                    bool approaching = car.Lane != null && car.Lane.To.Lights != null;
                    float now = approaching ? car.Lane.Length - car.S - TrafficSimulation.CarLength / 2f : float.MaxValue;
                    if (approaching && toLine.TryGetValue(car, out float before) && before > 0f && now <= 0f)
                    {
                        junctionEntries++;
                        // Crossing the stop line on red is never allowed (on yellow it is, when too close to stop).
                        if (sim.SignalFor(car.Lane) == Signal.Red)
                        {
                            redRuns++;
                            if (firstRun == null)
                                firstRun = $"car {car.Id} lane {car.Lane.Id} phase {car.Lane.Phase} v={car.Speed:F2} committed={car.Committed} " +
                                           $"next={car.Next.Turn} t={sim.Time:F2} before={before:F2} now={now:F2}";
                        }
                    }
                    toLine[car] = now;
                }
                if (step % 10 == 0) AssertNoOverlap(sim);
            }
            Assert.AreEqual(0, redRuns, "cars crossed the stop line on red; first: " + firstRun);
            Assert.Greater(junctionEntries, 20, "traffic went through the lights");
            foreach (TrafficSimulation.Car car in sim.Cars)
                Assert.Greater(car.Odometer, 1500f, $"car {car.Id} barely moved (gridlock?)");
        }

        [Test]
        public void Traffic_StopsForSomeoneInTheLane()
        {
            var net = RoadNetwork.FromPlan();
            var sim = new TrafficSimulation(net, seed: 5);
            TrafficSimulation.Car car = sim.Spawn(null);
            Vector2 person = car.Lane.At(Mathf.Min(car.S + 25f, car.Lane.Length - 1f));
            for (int i = 0; i < 400; i++) sim.Step(0.05f, new[] { person });
            Assert.Less(car.Speed, 0.1f, "stopped");
            Assert.Greater(Vector2.Distance(car.Position, person), 2.5f, "stopped short of the person");
        }

        private static void AssertNoOverlap(TrafficSimulation sim)
        {
            IReadOnlyList<TrafficSimulation.Car> cars = sim.Cars;
            for (int i = 0; i < cars.Count; i++)
            for (int j = i + 1; j < cars.Count; j++)
                if (Overlap(cars[i], cars[j]))
                    Assert.Fail($"cars {cars[i].Id} and {cars[j].Id} overlap at {cars[i].Position} / {cars[j].Position} (t={sim.Time:F1})");
        }

        /// <summary>Separating-axis test for two 4.4 × 1.8 m footprints (shrunk a little: bumpers may touch).</summary>
        private static bool Overlap(TrafficSimulation.Car a, TrafficSimulation.Car b)
        {
            if ((a.Position - b.Position).sqrMagnitude > 25f) return false;
            Vector2[] axes = { a.Heading, RoadNetwork.RightOf(a.Heading), b.Heading, RoadNetwork.RightOf(b.Heading) };
            foreach (Vector2 axis in axes)
            {
                float ra = Extent(a, axis), rb = Extent(b, axis);
                if (Mathf.Abs(Vector2.Dot(b.Position - a.Position, axis)) > ra + rb) return false;
            }
            return true;
        }

        private static float Extent(TrafficSimulation.Car c, Vector2 axis) =>
            2.0f * Mathf.Abs(Vector2.Dot(c.Heading, axis)) + 0.8f * Mathf.Abs(Vector2.Dot(RoadNetwork.RightOf(c.Heading), axis));

        // ---- sidewalks and pedestrians ----

        private static SidewalkGraph Walks(out RoadNetwork roads)
        {
            roads = RoadNetwork.FromPlan();
            var places = new List<(Vector2, PlaceKind, string)>
            {
                (new Vector2(6f, -5.9f), PlaceKind.Door, "home"),
                (new Vector2(156f, -6.3f), PlaceKind.Door, "office"),
                (new Vector2(71f, -6.3f), PlaceKind.Door, "coffee"),
                (new Vector2(-30f, 90f), PlaceKind.Door, "north"),
                (new Vector2(86f, -6.1f), PlaceKind.Bench, "bench"),
            };
            return SidewalkGraph.Build(roads, places);
        }

        [Test]
        public void Sidewalks_ConnectEveryPlace_AndTheCommuteUsesCrosswalks()
        {
            SidewalkGraph g = Walks(out _);
            SidewalkGraph.Node home = g.FindPlace("home");
            foreach (SidewalkGraph.Node place in g.Nodes.Where(n => n.Kind != PlaceKind.Walkway))
                Assert.NotNull(g.Route(home, place), "reachable: " + place.Tag);

            List<SidewalkGraph.Node> commute = g.Route(home, g.FindPlace("office"));
            int crossings = 0;
            for (int i = 0; i + 1 < commute.Count; i++)
                if (SidewalkGraph.EdgeBetween(commute[i], commute[i + 1]).Crosswalk != null) crossings++;
            Assert.AreEqual(2, crossings, "home to office crosses First St and Exchange St");
            Assert.Less(RouteLength(commute), 175f, "stays on the direct sidewalk");
        }

        private static float RouteLength(List<SidewalkGraph.Node> r)
        {
            float sum = 0f;
            for (int i = 0; i + 1 < r.Count; i++) sum += Vector2.Distance(r[i].P, r[i + 1].P);
            return sum;
        }

        [Test]
        public void Pedestrians_OnlyEnterTheRoad_AtCrosswalks_WhenAllowed()
        {
            SidewalkGraph g = Walks(out RoadNetwork roads);
            var sim = new PedestrianSimulation(g, seed: 3) { MayCross = _ => false };
            for (int i = 0; i < 12; i++) sim.Spawn();
            for (int step = 0; step < 1200; step++)
            {
                sim.Step(0.25f, new Vector2(9999f, 9999f));
                foreach (PedestrianSimulation.Walker w in sim.Walkers)
                    Assert.IsFalse(InRoad(w.Position), $"walker {w.Id} in the road at {w.Position} while crossing is forbidden");
            }

            sim.MayCross = _ => true;
            bool crossed = false;
            for (int step = 0; step < 2400 && !crossed; step++)
            {
                sim.Step(0.25f, new Vector2(9999f, 9999f));
                crossed = sim.Walkers.Any(w => InRoad(w.Position));
                if (sim.Walkers.Count < 6) sim.Spawn();
            }
            Assert.IsTrue(crossed, "someone crossed once allowed");
        }

        /// <summary>On the asphalt: within a street's half width (not counting the ends inside junction corners).</summary>
        private static bool InRoad(Vector2 p)
        {
            foreach (var (a, b, _) in CityPlan.Streets)
            {
                Vector2 pa = CityPlan.Nodes[a].P, pb = CityPlan.Nodes[b].P;
                Vector2 ab = pb - pa;
                float t = Vector2.Dot(p - pa, ab) / ab.sqrMagnitude;
                if (t < 0f || t > 1f) continue;
                if (Vector2.Distance(p, pa + ab * t) < CityPlan.RoadHalfWidth - 0.4f) return true;
            }
            return false;
        }

        // ---- elevator, hours ----

        [Test]
        public void Elevator_CallTravelArriveOpenAndClose()
        {
            var e = new ElevatorLogic(2);
            (int, int)? arrived = null;
            e.Arrived += (from, to) => arrived = (from, to);

            e.Request(1);
            e.Update(0.1f, false);
            Assert.AreEqual(ElevatorState.Moving, e.State);
            float travel = e.TravelSeconds(0, 1);
            for (float t = 0f; t < travel + 0.05f; t += 0.1f) e.Update(0.1f, false);
            Assert.AreEqual((0, 1), arrived);
            Assert.AreEqual(1, e.CarFloor);
            for (int i = 0; i < 15; i++) e.Update(0.1f, false);
            Assert.AreEqual(ElevatorState.Open, e.State);

            // Someone in the doorway holds the doors.
            for (int i = 0; i < 100; i++) e.Update(0.1f, true);
            Assert.AreEqual(ElevatorState.Open, e.State);
            for (int i = 0; i < 70; i++) e.Update(0.1f, false);
            Assert.AreEqual(ElevatorState.Idle, e.State);
            Assert.AreEqual(0f, e.DoorOpen);

            // Calling from the floor it's on just opens the doors.
            e.Request(1);
            e.Update(0.1f, false);
            Assert.AreEqual(ElevatorState.Opening, e.State);
        }

        [Test]
        public void Hours_AndShifts()
        {
            var day = new DateTime(2030, 1, 7);
            var receptionist = new WorkSchedule { Shift = Hours.Of(7.5, 17.5), Break = Hours.Of(12, 12.75), HasBreak = true };
            Assert.IsFalse(receptionist.OnDuty(day.AddHours(7)));
            Assert.IsTrue(receptionist.OnDuty(day.AddHours(10)));
            Assert.IsFalse(receptionist.OnDuty(day.AddHours(12.25)));
            Assert.IsTrue(receptionist.OnDuty(day.AddHours(13)));
            Assert.IsFalse(receptionist.OnDuty(day.AddHours(18)));

            Assert.IsTrue(Shops.MartHours.Contains(day.AddHours(23.5)));
            Assert.IsFalse(Shops.MartHours.Contains(day.AddHours(0.5)));
            var lateBar = Hours.Of(18, 2);
            Assert.IsTrue(lateBar.Contains(day.AddHours(1)));
            Assert.IsFalse(lateBar.Contains(day.AddHours(3)));
        }
    }
}
