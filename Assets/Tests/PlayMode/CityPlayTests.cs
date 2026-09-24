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
using OpeningBell.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
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
            // Calder's, not Harborview's freight hoist.
            Elevator elevator = UnityEngine.Object.FindObjectsByType<Elevator>(FindObjectsSortMode.None)
                .OrderBy(e => Vector3.Distance(e.transform.position, At("calder_elevator_hall_L"))).First();
            ElevatorButton[] buttons = elevator.GetComponentsInChildren<ElevatorButton>();
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
            root.Q<TextField>("qty").value = "1";
            game.Market.TryGetQuote("APEX", out Quote quote);
            root.Q<TextField>("limit-price").value = Fmt.Price(quote.Ask);
            Press(root.Q<Button>("submit-order"));
            Assert.AreEqual(1, game.Account.Portfolio.QuantityOf("APEX"), "bought from the office");
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
            // Other places sell coffee too (the diner's is cheaper): Half Past Nine's till is the one by its counter.
            ShopCounter coffee = UnityEngine.Object.FindObjectsByType<ShopCounter>(FindObjectsSortMode.None).Where(s => s.Item == "Coffee")
                .OrderBy(s => Vector3.Distance(s.transform.position, At("coffee_counter"))).First();
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

        /// <summary>
        /// Rides toward <paramref name="target"/> with the real keyboard: W to go (S to brake near the end), A/D
        /// to steer at a point a few metres along the line from the start. Stops within about a metre.
        /// </summary>
        private IEnumerator RideTo(Vector3 target, string what, float timeout = 60f)
        {
            RideController rider = _city.Rider;
            Vector3 from = _player.transform.position;
            Vector3 line = target - from;
            line.y = 0f;
            float start = Time.realtimeSinceStartup, lastProgress = start;
            float best = float.MaxValue;
            while (true)
            {
                Assert.IsTrue(rider.IsRiding, $"still riding ({what})");
                Vector3 p = _player.transform.position;
                Vector3 to = target - p;
                to.y = 0f;
                float remaining = to.magnitude;
                if (remaining < 1.2f)
                {
                    while (rider.State.Speed > 0.3f)
                    {
                        HoldKeys(Key.S);
                        yield return null;
                    }
                    HoldKeys();
                    yield break;
                }
                // Pure pursuit on the line from the start keeps us on the middle of the sidewalk.
                float t = Mathf.Clamp01(Vector3.Dot(p - from, line) / line.sqrMagnitude);
                Vector3 aim = from + line * Mathf.Min(1f, t + 4f / line.magnitude) - p;
                float error = Vector3.SignedAngle(_player.transform.forward, new Vector3(aim.x, 0f, aim.z), Vector3.up);
                var keys = new List<Key>();
                bool slowDown = rider.State.Speed > Mathf.Max(1.5f, remaining * 0.9f);
                keys.Add(slowDown ? Key.S : Key.W);
                if (error > 3f) keys.Add(Key.D);
                else if (error < -3f) keys.Add(Key.A);
                HoldKeys(keys.ToArray());
                float now = Time.realtimeSinceStartup;
                if (remaining < best - 0.5f)
                {
                    best = remaining;
                    lastProgress = now;
                }
                if (now - lastProgress > 10f || now - start > timeout)
                    Assert.Fail($"Stuck riding to {what}: at {p}, target {target}, speed {rider.State.Speed:F2}, move {_player.Input.Move.ReadValue<Vector2>()}, " +
                                $"grade {rider.Ground.Grade:F2}, rough {rider.Ground.Roughness:F2}, heading {rider.State.Heading * Mathf.Rad2Deg:F0}, hit {rider.LastHit}");
                yield return null;
            }
        }

        /// <summary>Stop at the kerb, wait for a gap in traffic, ride across.</summary>
        private IEnumerator RideAcross(Vector3 kerb, Vector3 other)
        {
            yield return RideTo(kerb, "the kerb");
            Vector2 mid = new Vector2((kerb.x + other.x) / 2f, (kerb.z + other.z) / 2f);
            yield return WaitUntil(() => !_city.Traffic.Simulation.AnyCarNear(mid, 16f), 40f, "a gap in traffic");
            yield return RideTo(other, "across the street");
        }

        [UnityTest]
        public IEnumerator Bike_BoughtRiddenParked_StaysWhereItWasLeft_AfterReload()
        {
            UseSimulatedInput();
            yield return Setup(9.5);
            GameBootstrap game = Find<GameBootstrap>();
            _player.PlaceAt(At("bike_front_out"), 0f);
            yield return null;
            yield return Route("bike shop", P(103f, -3f), At("bike_counter"));
            ForSaleDisplay commuter = UnityEngine.Object.FindObjectsByType<ForSaleDisplay>(FindObjectsSortMode.None).First(d => d.Id == "bike_commuter");
            commuter.Interact();
            OwnedVehicle bike = game.Vehicles.Vehicles.Single();
            Assert.AreEqual(VehicleState.Parked, bike.State, "rolled out front");
            yield return null;
            Assert.NotNull(_city.Fleet.Shown(bike), "visible out front");
            ForSaleDisplay tuneUp = UnityEngine.Object.FindObjectsByType<ForSaleDisplay>(FindObjectsSortMode.None).First(d => d.Kind == SaleKind.TuneUp);
            StringAssert.Contains("Your Everyday Commuter 3", tuneUp.Details, "the shop sees the bike out front");

            yield return Snapshot(P(107.5f, -3.8f), 300f, "bike-shop.png", 12f);
            yield return Route("out", P(103f, -3f), At("bike_front_out"));
            _city.Fleet.Shown(bike).GetComponent<ParkedVehicle>().Interact();
            Assert.IsTrue(_city.Rider.IsRiding);
            yield return RideTo(P(113f, -7.25f), "onto the sidewalk");
            yield return TapKey(Key.C);
            Assert.IsTrue(_city.Rider.ChaseCamera, "C: chase camera");
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "bike-chase.png");
            yield return TapKey(Key.C);
            yield return RideTo(P(124f, -7.25f), "along Maple");
            Assert.Greater(bike.Odometer, 15);
            yield return TapKey(Key.E);
            Assert.IsFalse(_city.Rider.IsRiding, "E gets off");
            Assert.AreEqual(VehicleState.Parked, bike.State);
            Assert.Greater(Vector3.Distance(_player.transform.position, new Vector3((float)bike.X, (float)bike.Y, (float)bike.Z)), 0.6f, "stepped off beside it");
            var parkedAt = new Vector3((float)bike.X, 0f, (float)bike.Z);

            game.Save();
            yield return SceneManager.LoadSceneAsync("Main");
            yield return null;
            yield return null;
            GameBootstrap loaded = Find<GameBootstrap>();
            CityBuilder city = Find<CityBuilder>();
            OwnedVehicle again = loaded.Vehicles.Vehicles.Single();
            Assert.AreEqual(bike.Id, again.Id, "same bike, not a new spawn");
            Assert.AreEqual(VehicleState.Parked, again.State);
            Assert.Less(Vector3.Distance(parkedAt, new Vector3((float)again.X, 0f, (float)again.Z)), 0.01f, "where it was left");
            GameObject shown = city.Fleet.Shown(again);
            Assert.NotNull(shown, "in the world after the reload");
            Assert.Less(Vector3.Distance(new Vector3(shown.transform.position.x, 0f, shown.transform.position.z), parkedAt), 0.01f);
        }

        /// <summary>Drives toward <paramref name="target"/> with the keyboard (W, S near the end, A/D along the line).</summary>
        private IEnumerator DriveTo(Vector3 target, string what, float timeout = 60f)
        {
            DriveController driver = _city.Driver;
            Transform car = driver.Car.transform;
            Vector3 from = car.position;
            Vector3 line = target - from;
            line.y = 0f;
            float start = Time.realtimeSinceStartup, lastProgress = start, best = float.MaxValue;
            while (true)
            {
                Assert.IsTrue(driver.IsDriving, $"still driving ({what})");
                Vector3 p = car.position;
                Vector3 to = target - p;
                to.y = 0f;
                float remaining = to.magnitude;
                if (remaining < 2f)
                {
                    while (driver.Car.ForwardSpeed > 0.3f)
                    {
                        HoldKeys(Key.S);
                        yield return null;
                    }
                    HoldKeys();
                    yield break;
                }
                float t = Mathf.Clamp01(Vector3.Dot(p - from, line) / line.sqrMagnitude);
                Vector3 aim = from + line * Mathf.Min(1f, t + 8f / line.magnitude) - p;
                float error = Vector3.SignedAngle(car.forward, new Vector3(aim.x, 0f, aim.z), Vector3.up);
                var keys = new List<Key>();
                // Cruise ~30 km/h, brake to arrive.
                bool slow = driver.Car.ForwardSpeed > Mathf.Min(8.5f, Mathf.Max(2f, remaining * 0.5f));
                keys.Add(slow ? Key.S : Key.W);
                if (error > 2f) keys.Add(Key.D);
                else if (error < -2f) keys.Add(Key.A);
                HoldKeys(keys.ToArray());
                float now = Time.realtimeSinceStartup;
                if (remaining < best - 0.5f)
                {
                    best = remaining;
                    lastProgress = now;
                }
                if (now - lastProgress > 15f || now - start > timeout)
                    Assert.Fail($"Stuck driving to {what}: at {p}, target {target}, speed {driver.Car.SpeedKmh:F0} km/h, gear {driver.Car.Gear}");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator UsedCar_BoughtOnline_DrivenParkedFuelled_AndSaved()
        {
            UseSimulatedInput();
            yield return Setup(8.25);
            GameBootstrap game = Find<GameBootstrap>();

            // Buy the used sedan from the classifieds (bank first: it's more than the bank holds).
            Assert.IsNull(game.Economy.TransferFromBrokerage(3000m, game.Clock.Now));
            TradingTerminal terminal = Find<TradingTerminal>();
            yield return WaitUntil(() => terminal.Context != null && terminal.Context.BuyUsedCar != null, 3f, "classifieds wired");
            OpeningBell.Vehicles.UsedListing listing = game.Vehicles.Catalog.Listings.First(l => l.Id == "used_sedan_1");
            Assert.IsNull(terminal.Context.BuyUsedCar(listing), "bought");
            OwnedVehicle car = game.Vehicles.Vehicles.Single();
            Assert.AreEqual(VehicleKind.Car, car.Kind);
            Assert.IsNotNull(terminal.Context.BuyUsedCar(listing), "each car sells once");
            yield return null;
            GameObject parked = _city.Fleet.Shown(car);
            Assert.NotNull(parked, "left at the curb");
            Assert.Less(Vector3.Distance(parked.transform.position, CityBuilder.CurbSpots[0]), 0.1f);

            // Walk out to it and get in.
            yield return Route("to the car", P(-1.8f, -1.9f), At("apartment_door_hall"), At("apartment_front_in"), At("apartment_front_out"),
                P(6f, -7.25f), P(12f, -8.3f));
            parked.GetComponent<ParkedVehicle>().Interact();
            yield return WaitUntil(() => _city.Driver.IsDriving && _city.Driver.Car != null && !_city.Driver.Car.Body.isKinematic, 3f, "in the driver's seat");
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "car-chase.png");

            // Drive west along Maple, stop, get out.
            double odometer = car.Odometer;
            yield return DriveTo(P(-18f, -11.6f), "west on Maple");
            Assert.Greater(car.Odometer - odometer, 20, "odometer");
            Assert.Less(car.FuelLiters, 50 * listing.FuelFraction, "burned some fuel");
            Assert.Less(50 * listing.FuelFraction - car.FuelLiters, 0.05, "a few litres per hundred km, not per street: fuel follows distance, not the game clock");
            yield return TapKey(Key.E);
            Assert.IsFalse(_city.Driver.IsDriving, "E gets out once stopped");
            Assert.AreEqual(VehicleState.Parked, car.State);
            Assert.IsTrue(parked.GetComponent<CarController>().Body.isKinematic, "parked cars stay put");
            Assert.Greater(Vector3.Distance(_player.transform.position, parked.transform.position), 1f, "stepped out beside it");

            // Fuel: park it at a pump and fill up.
            Vector3 bay = At("fuel_bay_west");
            parked.transform.SetPositionAndRotation(new Vector3(bay.x, 0f, bay.z), Quaternion.identity);
            game.Vehicles.Park(car, bay.x, 0f, bay.z, 0f);
            FuelPump pump = UnityEngine.Object.FindObjectsByType<FuelPump>(FindObjectsSortMode.None).OrderBy(x => Vector3.Distance(x.transform.position, bay)).First();
            FuelHands hands = _player.GetComponent<FuelHands>();
            decimal bank = game.Economy.Bank.Balance;
            double before = car.FuelLiters;
            Assert.AreEqual("Take the nozzle", pump.Prompt);
            pump.Interact();
            Assert.AreEqual(FuelHands.Tool.Nozzle, hands.Held, "the nozzle's in your hand");
            // Hold the left button aimed at the car: fuel pours from the nozzle.
            _player.PlaceAt(new Vector3(bay.x - 1.9f, 0.05f, bay.z - 0.6f), 75f, 22f);
            yield return null;
            HoldLeftButton(true);
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(hands.Flowing, "fuel flows while the button's held");
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "fuel-nozzle.png");
            HoldLeftButton(false);
            yield return null;
            for (int i = 0; i < 40 && car.FuelLiters < car.FuelCapacity; i++) hands.Flow(car, 1f);
            Assert.AreEqual(car.FuelCapacity, car.FuelLiters, 1e-9, "full tank");
            Assert.AreEqual(bank, game.Economy.Bank.Balance, "paid when you hang up");
            StringAssert.StartsWith("Hang up and pay", pump.Prompt);
            pump.Interact();
            Assert.AreEqual(FuelHands.Tool.None, hands.Held);
            Assert.AreEqual(bank - Trading.Money.RoundCents((decimal)(car.FuelCapacity - before) * FuelStation.PricePerLiter), game.Economy.Bank.Balance);

            // A jerry can: bought empty, filled at the pump, poured into a dry tank.
            UnityEngine.Object.FindAnyObjectByType<JerryCanRack>().Interact();
            Assert.AreEqual(FuelHands.Tool.Can, hands.Held);
            Assert.AreEqual(0, game.Vehicles.JerryCan, 1e-9);
            StringAssert.StartsWith("Fill the jerry can", pump.Prompt);
            pump.Interact();
            Assert.AreEqual(FuelHands.CanCapacity, game.Vehicles.JerryCan, 1e-9);
            game.Vehicles.SetFuel(car, 0);
            hands.Flow(car, 30f);
            Assert.AreEqual(FuelHands.CanCapacity, car.FuelLiters, 1e-9, "the can's ten litres in the tank");
            Assert.AreEqual(0, game.Vehicles.JerryCan, 1e-9);
            hands.PutAwayCan();

            // Stranded: Tidewater's van brings ten litres twenty minutes after the call.
            game.Vehicles.SetFuel(car, 0);
            decimal beforeCall = game.Economy.Bank.Balance;
            StringAssert.Contains("van", hands.CallTidewater());
            Assert.AreEqual(beforeCall - FuelHands.RoadsideFee - Trading.Money.RoundCents(10m * FuelStation.PricePerLiter), game.Economy.Bank.Balance);
            StringAssert.Contains("on its way", hands.CallTidewater(), "one van at a time");
            game.SkipTo(game.Clock.Now.AddMinutes(FuelHands.RoadsideMinutes + 1));
            yield return null;
            yield return null;
            Assert.AreEqual(FuelHands.RoadsideLiters, car.FuelLiters, 1e-9, "delivered");
            game.Vehicles.SetFuel(car, car.FuelCapacity);

            // Saved and restored exactly where it was left.
            game.Save();
            yield return SceneManager.LoadSceneAsync("Main");
            yield return null;
            yield return null;
            GameBootstrap loaded = Find<GameBootstrap>();
            OwnedVehicle again = loaded.Vehicles.Vehicles.Single();
            Assert.AreEqual(car.Id, again.Id);
            Assert.AreEqual(car.FuelCapacity, again.FuelLiters, 1e-6);
            GameObject back = Find<CityBuilder>().Fleet.Shown(again);
            Assert.NotNull(back);
            Assert.Less(Vector3.Distance(new Vector3(back.transform.position.x, 0f, back.transform.position.z), new Vector3(bay.x, 0f, bay.z)), 0.05f);
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
            ElevatorButton up = UnityEngine.Object.FindObjectsByType<ElevatorButton>(FindObjectsSortMode.None).First(b => b.Prompt.StartsWith("Floor 2") && b.transform.position.x < 200f);
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
