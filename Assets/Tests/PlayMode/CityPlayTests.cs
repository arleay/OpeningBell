using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Economy;
using OpeningBell.Gameplay;
using OpeningBell.Market;
using OpeningBell.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OpeningBell.Tests
{
    /// <summary>
    /// Phase 9 acceptance (city slice): leave the apartment, walk downtown, pass the receptionist, take the
    /// elevator, trade from the leased office, take the stairs down, buy a coffee, walk home, save. The player
    /// is walked with its real CharacterController, so the route itself is proven passable.
    /// </summary>
    public class CityPlayTests : SceneTestBase
    {
        private FirstPersonController _player;
        private CityBuilder _city;
        private Door[] _doors;

        private IEnumerator Setup(double hour)
        {
            yield return LoadMain();
            _player = Find<FirstPersonController>();
            _city = Find<CityBuilder>();
            _doors = UnityEngine.Object.FindObjectsByType<Door>(FindObjectsSortMode.None);
            GameBootstrap game = Find<GameBootstrap>();
            game.SkipTo(game.Clock.Now.Date.AddHours(hour));
            yield return null;
        }

        private Vector3 At(string anchor) => _city.Anchors[anchor];

        /// <summary>
        /// Walks toward <paramref name="target"/> (x/z) at a brisk 5 m/s, opening swing doors on the way. Like a
        /// person, steps back and waits when something (a car on the crosswalk) is in the way.
        /// </summary>
        private IEnumerator WalkTo(Vector3 target, string what, float timeout = 90f)
        {
            var body = _player.GetComponent<CharacterController>();
            float start = Time.realtimeSinceStartup, lastProgress = start;
            Vector3 progressFrom = _player.transform.position;
            while (true)
            {
                Vector3 p = _player.transform.position;
                Vector3 to = target - p;
                to.y = 0f;
                if (to.magnitude < 0.3f) yield break;
                Vector3 dir = to.normalized;
                foreach (Door d in _doors)
                {
                    Vector3 toDoor = d.transform.position - p;
                    toDoor.y = 0f;
                    if (toDoor.magnitude < 2.2f && !d.IsLocked && !d.WantsOpen && d.CanInteract && Vector3.Dot(toDoor.normalized, dir) > 0.2f) d.Interact();
                }
                _player.transform.rotation = Quaternion.LookRotation(dir);
                body.Move(dir * Mathf.Min(5f * Time.deltaTime, to.magnitude));
                float now = Time.realtimeSinceStartup;
                if ((p - progressFrom).magnitude > 0.5f)
                {
                    progressFrom = p;
                    lastProgress = now;
                }
                else if (now - lastProgress > 1.5f)
                {
                    // Blocked: back off a step and give it a moment.
                    for (float t = 0f; t < 0.35f; t += Time.deltaTime)
                    {
                        body.Move(-dir * (3f * Time.deltaTime));
                        yield return null;
                    }
                    float resume = Time.realtimeSinceStartup + 2f;
                    while (Time.realtimeSinceStartup < resume) yield return null;
                    progressFrom = _player.transform.position;
                    lastProgress = Time.realtimeSinceStartup;
                }
                if (now - start > timeout) Assert.Fail($"Stuck walking to {what}: at {p}, target {target}");
                yield return null;
            }
        }

        private IEnumerator Route(string what, params Vector3[] points)
        {
            for (int i = 0; i < points.Length; i++) yield return WalkTo(points[i], $"{what} #{i}");
        }

        private static Vector3 P(float x, float z, float y = 0f) => new Vector3(x, y, z);

        /// <summary>Walk to the kerb, wait for the walk signal (or a gap in traffic), cross.</summary>
        private IEnumerator Cross(Vector3 kerb, Vector3 other)
        {
            yield return WalkTo(kerb, "kerb");
            Vector2 mid = new Vector2((kerb.x + other.x) / 2f, (kerb.z + other.z) / 2f);
            TrafficSimulation traffic = _city.Traffic.Simulation;
            SidewalkGraph.Crosswalk cw = _city.Pedestrians.Simulation.Graph.Crosswalks.OrderBy(c => Vector2.Distance(c.Center, mid)).First();
            if (cw.Signalized) yield return WaitUntil(() => PedestrianView.WalkShown(traffic, cw), 60f, "the walk signal");
            else yield return WaitUntil(() => !traffic.AnyCarNear(mid, 14f), 40f, "a gap in traffic");
            yield return WalkTo(other, "across the street");
        }

        private IEnumerator Snapshot(Vector3 at, float yaw, string file, float pitch = 0f)
        {
            _player.PlaceAt(at, yaw);
            _player.CameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            for (int i = 0; i < 3; i++) yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), file);
        }

        [UnityTest]
        public IEnumerator Commute_HomeToOffice_TradeThere_Coffee_HomeAndSave()
        {
            yield return Setup(7.75);
            GameBootstrap game = Find<GameBootstrap>();
            InteractionHud hud = Find<InteractionHud>();
            WorkstationController workstation = Find<WorkstationController>();
            TradingTerminal terminal = Find<TradingTerminal>();
            Assert.IsNull(game.Economy.Buy(CityContext.OfficeLeaseId, game.Clock.Now), "lease the office");

            // 1–2: out of the apartment and onto Maple Street.
            yield return Route("apartment", P(-1.8f, -1.9f), At("apartment_door_hall"), At("apartment_front_in"), At("apartment_front_out"), P(6f, -7.25f));
            // 3: downtown along Maple's north sidewalk (crosses First St and Exchange St).
            yield return Cross(P(48f, -7.25f), P(62f, -7.25f));
            yield return Cross(P(128f, -7.25f), P(142f, -7.25f));
            yield return WalkTo(P(156f, -7.25f), "Calder frontage");
            // 4–5: into the Calder Building, past reception.
            yield return Route("lobby", At("calder_front_out"), At("calder_lobby"), P(152.4f, 0.5f), P(152.4f, 3.2f));
            yield return WaitUntil(() => hud.SubtitleText.Contains("tenant in 204"), 3f, "receptionist welcome");
            yield return WalkTo(At("calder_elevator_hall_L"), "elevator hall");

            // 6: elevator to floor 2.
            Elevator elevator = UnityEngine.Object.FindAnyObjectByType<Elevator>();
            ElevatorButton[] buttons = UnityEngine.Object.FindObjectsByType<ElevatorButton>(FindObjectsSortMode.None);
            buttons.First(b => b.Prompt == "Call elevator" && b.transform.position.y < 2f).Interact();
            yield return WaitUntil(() => elevator.Logic.State == ElevatorState.Open && elevator.CarFloor == 0, 15f, "car at L, doors open");
            yield return WalkTo(At("calder_elevator_car_L"), "into the car");
            buttons.First(b => b.Floor == 1 && b.Prompt.StartsWith("Floor 2") && b.transform.position.y < 2f).Interact();
            yield return WaitUntil(() => _player.transform.position.y > 4f, 20f, "rode up");
            yield return WaitUntil(() => elevator.Logic.State == ElevatorState.Open, 5f, "doors open on 2");

            // 7–8: into Suite 204 and trade from the office desk.
            yield return Route("to 204", At("calder_elevator_hall_2"), At("calder_corridor_204"), At("suite_204_inside"));
            yield return Snapshot(At("suite_204_inside") + new Vector3(-1.5f, 0f, 1f), 160f, "city-office.png");
            workstation.SitDown(_city.OfficeDesk);
            yield return WaitUntil(() => workstation.State == WorkstationState.Seated, 5f, "seated at the office desk");
            Assert.AreSame(_city.OfficeDesk, workstation.CurrentDesk);
            RenderTerminalOffscreen(terminal);
            yield return null;
            VisualElement root = terminal.Root;
            Click(root.Q("watch-APEX"));
            Press(root.Q<Button>("type-limit"));
            root.Q<TextField>("qty").value = "20";
            game.Market.TryGetQuote("APEX", out Quote quote);
            root.Q<TextField>("limit-price").value = Fmt.Price(quote.Ask);
            Press(root.Q<Button>("submit-order"));
            Assert.AreEqual(20, game.Account.Portfolio.QuantityOf("APEX"), "bought from the office");
            terminal.ShowOnScreen(true);
            workstation.StandUp();
            yield return WaitUntil(() => workstation.State == WorkstationState.Standing, 5f, "stood up");
            Assert.Less(Vector3.Distance(_player.transform.position, At("office_desk")), 0.5f, "stands up in the office, not at home");

            // 9: leave by the stairs.
            yield return Route("stairs down", At("suite_204_inside"), At("calder_corridor_204"), At("calder_stairs_door_upper"),
                At("calder_stairs_top"), P(166.55f, 9.2f, 4.8f), P(166.55f, 16.9f), P(161.2f, 16.9f), P(161.2f, 9.2f),
                At("calder_stairs_ground"), At("calder_stairs_door_ground"));
            Assert.Less(_player.transform.position.y, 0.5f, "back on the ground floor");
            yield return Route("out", P(152.4f, 3.2f), P(152.4f, 0.5f), At("calder_lobby"), At("calder_front_out"), P(156f, -7.25f));

            // 10: coffee on the way home.
            yield return Cross(P(142f, -7.25f), P(128f, -7.25f));
            yield return Route("coffee", P(71f, -7.25f), At("coffee_front_out"), P(71f, -3f), At("coffee_counter"));
            ShopCounter coffee = UnityEngine.Object.FindObjectsByType<ShopCounter>(FindObjectsSortMode.None).First(s => s.Item == "Coffee");
            Assert.IsTrue(coffee.CanInteract, "barista is in");
            decimal before = game.Economy.Bank.Balance;
            coffee.Interact();
            Assert.AreEqual(before - 4.50m, game.Economy.Bank.Balance);
            StringAssert.StartsWith("Coffee", hud.ToastText);

            // 12: home.
            yield return Route("out of the shop", P(71f, -3f), At("coffee_front_out"), P(71f, -7.25f));
            yield return Cross(P(62f, -7.25f), P(48f, -7.25f));
            yield return Route("home", P(6f, -7.25f),
                At("apartment_front_out"), At("apartment_front_in"), At("apartment_door_hall"), P(-1.8f, -1.9f), P(0.5f, -1f));

            // 13: the world saves (and the time spent is real game time).
            game.Save();
            Assert.IsTrue(SaveSystem.TryRead("slot1", out SaveGame save, out _));
            Assert.Less(Vector3.Distance(new Vector3(save.Player.X, save.Player.Y, save.Player.Z), P(0.5f, -1f)), 0.6f, "saved at home");
            CollectionAssert.Contains(save.Economy.Owned, CityContext.OfficeLeaseId);
            Assert.IsTrue(save.Economy.Transactions.Any(t => t.Description == "Coffee"));
            Assert.Greater(game.Clock.Now.TimeOfDay.TotalHours, 8.0, "the commute took game time");
        }

        [UnityTest]
        public IEnumerator Streets_HaveMovingTrafficAndPeople_AndNonTenantsAreKeptOut()
        {
            yield return Setup(8.5);
            GameBootstrap game = Find<GameBootstrap>();
            // The city was built at 6 AM; give it a few seconds to fill up for the rush.
            float until = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.Greater(_city.Traffic.CarCount, 10, "morning rush traffic");
            Assert.Greater(_city.Pedestrians.Count, 10, "morning pedestrians");

            var start = _city.Traffic.Simulation.Cars.ToDictionary(c => c, c => c.Odometer);
            until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.Greater(start.Count(kv => kv.Key.Odometer > kv.Value + 1f), 3, "cars are driving");

            // Without a lease, the suite and the upper floors stay locked.
            Assert.IsFalse(game.Economy.Owns(CityContext.OfficeLeaseId));
            Door suite = _doors.First(d => d.Label == "suite 204");
            StringAssert.Contains("lease it in the STORE app", suite.Prompt);
            ElevatorButton up = UnityEngine.Object.FindObjectsByType<ElevatorButton>(FindObjectsSortMode.None).First(b => b.Prompt.StartsWith("Floor 2"));
            StringAssert.Contains("tenants only", up.Prompt);

            // Screenshots for review: street by day, downtown, lobby, coffee shop, and the street at night.
            yield return Snapshot(P(8f, -7f), 90f, "city-street.png");

            // Frame time on the street, looking down Maple with traffic and people.
            var watch = new System.Diagnostics.Stopwatch();
            double total = 0, worst = 0;
            for (int i = 0; i < 180; i++)
            {
                watch.Restart();
                yield return null;
                total += watch.Elapsed.TotalMilliseconds;
                worst = Math.Max(worst, watch.Elapsed.TotalMilliseconds);
            }
            Debug.Log($"PERF street frame avg {total / 180:F2} ms, worst {worst:F2} ms, {_city.Traffic.CarCount} cars, {_city.Pedestrians.Count} people");
            Assert.Less(total / 180, 33.3, "street frame time");
            yield return Snapshot(P(126f, -16f), 60f, "city-downtown.png", -8f);
            yield return Snapshot(P(150f, -4f), 35f, "city-lobby.png");
            yield return Snapshot(P(75f, -3.5f), 300f, "city-coffee.png");
            game.SkipTo(game.Clock.Now.Date.AddHours(20.5));
            for (int i = 0; i < 5; i++) yield return null;
            yield return Snapshot(P(100f, -7f), 90f, "city-night.png", -4f);
        }
    }
}
