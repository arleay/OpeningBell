using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>The redesigned town (TOWN_SPEC): the ground, the railway, the water, the landmarks.</summary>
    public class TownPlayTests : SceneTestBase
    {
        [TearDown]
        public void RestoreTime() => Time.timeScale = 1f;

        [UnityTest]
        public IEnumerator Train_RunsAndStopsAtMapleStation()
        {
            yield return LoadMain();
            var train = Find<RailTrain>();
            Time.timeScale = 20f;
            yield return WaitUntil(() => train.State == RailTrain.Phase.Dwelling, 20f, "the train at Maple Station");
            Time.timeScale = 1f;
            float middle = (CityPlan.StationWest + CityPlan.StationEast) / 2f;
            Assert.That(train.X, Is.InRange(middle, CityPlan.StationEast), "stopped with the train along the platform");
            Transform car = train.transform.GetChild(0);
            Assert.That(car.position.y, Is.EqualTo(CityPlan.RailDeck(car.position.x) + 0.42f).Within(0.05f), "on the rails");
        }

        /// <summary>Park in a bay, buy a turbo at the board (two presses), watch the lift, drive away faster.</summary>
        [UnityTest]
        public IEnumerator Mechanic_FitsATurbo_OnTheCarInTheBay()
        {
            yield return LoadMain();
            var city = Find<CityBuilder>();
            var game = Find<GameBootstrap>();
            var player = Find<FirstPersonController>();
            game.SkipTo(game.Clock.Now.Date.AddDays(1).AddHours(10));
            MechanicShop shop = city.Mechanic;
            yield return WaitUntil(() => shop.Open, 30f, "the mechanic at the desk");
            Vector3 bay = city.Anchors["mechanic_bay1"];
            OpeningBell.Vehicles.OwnedVehicle car = game.Vehicles.Add("car_sedan", 24000m, game.Clock.Now, bay.x, 0.05, bay.z, 270);
            game.Economy.DevDeposit(20000m, game.Clock.Now);
            yield return null;
            Assert.AreEqual(car, shop.CarInBay(out int which));
            Assert.AreEqual(0, which);
            float torqueBefore = (float)city.Fleet.Shown(car).GetComponent<CarController>().Spec.CurveTorque.Max();

            player.PlaceAt(city.Anchors["mechanic_board"], 90f, 5f);
            for (int i = 0; i < 4; i++) yield return null;
            yield return CaptureCamera(player.GetComponentInChildren<Camera>(), "mechanic-office.png");

            decimal bank = game.Economy.Bank.Balance;
            shop.Buy("car_turbo", "Turbo kit");
            Assert.AreEqual(bank, game.Economy.Bank.Balance, "first press asks");
            shop.Buy("car_turbo", "Turbo kit");
            Assert.AreEqual(bank - 4800m, game.Economy.Bank.Balance);
            Assert.IsTrue(shop.Working);
            yield return new WaitForSeconds(1.5f);
            Assert.Greater(city.Fleet.Shown(car).transform.position.y, 0.6f, "up on the lift");
            player.PlaceAt(bay + new Vector3(-6f, 0f, 3f), 120f, 8f);
            for (int i = 0; i < 3; i++) yield return null;
            yield return CaptureCamera(player.GetComponentInChildren<Camera>(), "mechanic-lift.png");
            yield return WaitUntil(() => !shop.Working, 15f, "the job done");
            yield return null;
            CarController tuned = city.Fleet.Shown(car).GetComponent<CarController>();
            Assert.AreEqual(torqueBefore * 1.28f, (float)tuned.Spec.CurveTorque.Max(), 0.5f, "the rebuilt car has the turbo");
            Assert.Less(tuned.transform.position.y, 0.4f, "back down");
        }

        [UnityTest]
        public IEnumerator ParkedCars_LineTheStreets_AroundThePlayer()
        {
            yield return LoadMain();
            var city = Find<CityBuilder>();
            var game = Find<GameBootstrap>();
            game.SkipTo(game.Clock.Now.Date.AddDays(1).AddHours(12));
            yield return new WaitForSeconds(2f);
            Assert.Greater(city.Parked.SpotCount, 500, "kerb and lot spots across town");
            Assert.Greater(city.Parked.ShownCount, 15, "cars parked around the apartment at midday");
            Assert.Less(city.Parked.ShownCount, 400, "only near the player");
            Assert.Greater(city.Traffic.CarCount, 10, "traffic around the player");
            Assert.IsNotNull(city.Woods);
            Assert.Greater(city.Woods.TreeCount, 2000, "the woods");
        }

        [UnityTest]
        public IEnumerator Ground_FollowsTheStreets_AndTheWaterIsDeep()
        {
            yield return LoadMain();
            var city = Find<CityBuilder>();
            var player = Find<FirstPersonController>();
            // The core stays flat; the residential slope climbs north; the canal is cut below the banks.
            Assert.That(Physics.Raycast(new Vector3(20f, 50f, -14f), Vector3.down, out RaycastHit maple, 100f) ? maple.point.y : 99f,
                Is.EqualTo(CityPlan.RoadY).Within(0.05f), "Maple St at the old level");
            StreetMap.Segment ridge = StreetMap.Plan.Nearest(new Vector2(-20f, 302f), out float s, out _);
            Assert.Greater(ridge.GradeAt(s), 10f, "Ridge Rd up the hill");
            Assert.IsTrue(Physics.Raycast(new Vector3(-20f, 60f, 302f), Vector3.down, out RaycastHit hill, 100f));
            Assert.That(hill.point.y, Is.EqualTo(ridge.GradeAt(s) + CityPlan.RoadY).Within(0.2f), "the road surface is where the plan says");
            Assert.IsTrue(Physics.Raycast(new Vector3(315f, 20f, 60f), Vector3.down, out RaycastHit canal, 40f));
            Assert.Less(canal.point.y, TownTerrain.CanalWater - 0.5f, "the canal bed is under water");

            // Walk into the canal: climb back out onto the bank.
            player.PlaceAt(new Vector3(296f, 0.2f, 40f), 90f, 0f);
            yield return new WaitForSeconds(1.3f); // a safe spot is remembered each second
            player.PlaceAt(new Vector3(315f, TownTerrain.CanalFloor + 0.1f, 60f), 90f, 0f);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.Less(player.transform.position.x, 300f, "back on the bank");
            Assert.IsNotNull(city.Anchors["station_platform"], "the station is built");
        }

        /// <summary>Up a service ladder onto a roof and back down; a car drives up the parkade's first ramp to deck 1.</summary>
        [UnityTest]
        public IEnumerator Ladders_ReachTheRoofs_AndTheParkadeRampsDrive()
        {
            yield return LoadMain();
            var player = Find<FirstPersonController>();
            Ladder[] ladders = Object.FindObjectsByType<Ladder>(FindObjectsSortMode.None);
            TestContext.WriteLine("Ladders at " + string.Join(", ", ladders.Select(l => l.Foot.ToString("F0"))));
            Assert.GreaterOrEqual(ladders.Length, 4, "ladders on alley walls and the walk-ups' fire escapes");
            Ladder ladder = ladders.Where(l => l.Foot.y < 1f).OrderBy(l => l.Foot.sqrMagnitude).First();
            player.PlaceAt(ladder.Foot, 0f);
            yield return null;
            Assert.AreEqual("Climb up", ladder.Prompt);
            ladder.Interact();
            yield return WaitUntil(() => !ladder.Climbing, 15f, "the climb up");
            for (int i = 0; i < 20; i++) yield return null; // no roof under the feet would drop the player now
            Assert.AreEqual(ladder.Roof.y, player.transform.position.y, 0.3f, "standing on the roof");
            player.PlaceAt(player.transform.position, player.transform.eulerAngles.y + 150f, 10f);
            yield return CaptureCamera(player.GetComponentInChildren<Camera>(), "rooftop.png");
            Assert.AreEqual("Climb down", ladder.Prompt);
            ladder.Interact();
            yield return WaitUntil(() => !ladder.Climbing, 15f, "the climb down");
            Assert.AreEqual(ladder.Foot.y, player.transform.position.y, 0.3f, "back in the alley");

            // The parkade: straight up the first ramp from the entrance.
            Rect r = Rooftops.Parkade;
            float y0 = StreetMap.Plan.StreetGrade(r.center);
            var library = UnityEditor.AssetDatabase.LoadAssetAtPath<VehicleLibrary>("Assets/ScriptableObjects/Vehicles/VehicleLibrary.asset");
            Assert.IsTrue(library.CreateCatalog().TryGetModel("car_sedan", out OpeningBell.Vehicles.VehicleModel m));
            CarController car = CarFactory.BuildDrivable(null, library.CarMesh(m.Mesh), m.Car.Clone(), "car_sedan");
            car.transform.SetPositionAndRotation(new Vector3(r.xMin + 27f, y0 + 0.4f, r.yMin + 2f), Quaternion.identity);
            car.SetParked(false);
            car.EngineOn = true;
            yield return new WaitForSeconds(1f);
            car.Throttle = 0.45f;
            float deadline = Time.time + 20f;
            while (car.transform.position.z < r.yMin + 38f && Time.time < deadline)
            {
                if (car.SpeedKmh > 20f) car.Throttle = 0f;
                else car.Throttle = 0.45f;
                yield return new WaitForFixedUpdate();
            }
            car.Throttle = 0f;
            car.Brake = 1f;
            player.PlaceAt(car.transform.position + new Vector3(-6f, 1f, 3f), 110f, 8f);
            yield return CaptureCamera(player.GetComponentInChildren<Camera>(), "parkade-deck1.png");
            Assert.Greater(car.transform.position.z, r.yMin + 36f, "drove the length of the ramp");
            Assert.AreEqual(y0 + Rooftops.Decks[1], car.transform.position.y, 0.6f, "on deck 1");
            Object.Destroy(car.gameObject);
        }

        /// <summary>Places go on the map once you've walked past them, districts once you're in them; the save keeps them.</summary>
        [UnityTest]
        public IEnumerator Map_FillsIn_AsYouExplore_AndRemembers()
        {
            yield return LoadMain();
            var city = Find<CityBuilder>();
            var game = Find<GameBootstrap>();
            var player = Find<FirstPersonController>();
            MapDiscovery map = city.Minimap.Discovery;
            Assert.Greater(map.Places.Count, 45, "storefronts, mechanic, dealers, fuel, casino, parkade");
            Assert.IsTrue(map.Knows(map.Places.First(p => p.Icon == MapIcon.Home)), "home is on the map from the start");
            MapPlace police = map.Places.First(p => p.Name == "Kell Valley Police");
            Assert.AreEqual(MapIcon.Police, police.Icon);
            Assert.IsFalse(map.Knows(police), "not found yet");
            Assert.IsFalse(city.Minimap.Places.Any(p => p.Icon == MapIcon.Police));

            player.PlaceAt(police.At + new Vector3(-7f, 0.1f, -6f), 60f, 2f);
            yield return new WaitForSeconds(0.8f);
            Assert.IsTrue(map.Knows(police), "found by walking past");
            Assert.IsTrue(map.KnowsDistrict("Downtown"));
            Assert.IsTrue(city.Minimap.Places.Any(p => p.Icon == MapIcon.Police), "on the minimap now");
            yield return CaptureWithHud(player, Find<InteractionHud>(), "map-police.png");

            // A taxi home: paid for, the clock moves on by the ride, and you're set down at the door.
            MapPlace home = map.Places.First(p => p.Icon == MapIcon.Home);
            float d = Taxi.Distance(player.transform.position, home);
            decimal bank = game.Economy.Bank.Balance;
            System.DateTime before = game.Clock.Now;
            Assert.IsNull(Taxi.Ride(game, player, city.Driver, home));
            Assert.AreEqual(bank - Taxi.Fare(d), game.Economy.Bank.Balance, "the fare");
            Assert.GreaterOrEqual((game.Clock.Now - before).TotalMinutes, Taxi.Minutes(d) - 0.01, "the ride took time");
            Assert.Less(Taxi.Distance(player.transform.position, home), 1f, "at home");
            Assert.IsNotNull(Taxi.Ride(game, player, city.Driver, home), "no taxi for a walk across the street");

            // Teleport back to the police station: $250, instantly, no time passes.
            bank = game.Economy.Bank.Balance;
            before = game.Clock.Now;
            Assert.IsNull(Teleport.Go(game, player, city.Driver, police));
            Assert.AreEqual(bank - Teleport.Price, game.Economy.Bank.Balance, "$250");
            Assert.Less(Taxi.Distance(player.transform.position, police), 1f, "in front of it");
            Assert.Less((game.Clock.Now - before).TotalMinutes, 1.0, "instant");

            game.Save();
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Main");
            yield return null;
            yield return null;
            MapDiscovery again = Find<CityBuilder>().Minimap.Discovery;
            Assert.IsTrue(again.Knows(again.Places.First(p => p.Name == "Kell Valley Police")), "remembered after loading");
            Assert.IsTrue(again.KnowsDistrict("Downtown"));
        }

        /// <summary>My Cars on the phone: bring a car parked across town to the street outside a shop, then drive it.</summary>
        [UnityTest]
        public IEnumerator MyCars_BringsYourCar_ToTheNearestFreeSpot()
        {
            yield return LoadMain();
            var city = Find<CityBuilder>();
            var game = Find<GameBootstrap>();
            var player = Find<FirstPersonController>();
            game.SkipTo(game.Clock.Now.Date.AddDays(1).AddHours(12)); // midday: the shopping streets are full
            Vector3 bay = city.Anchors["mechanic_bay1"];
            OpeningBell.Vehicles.OwnedVehicle car = game.Vehicles.Add("car_sedan", 24000m, game.Clock.Now, bay.x, 0.05, bay.z, 270);
            MapPlace shop = city.Minimap.Discovery.Places.Where(p => p.Icon == MapIcon.Shop)
                .OrderByDescending(p => Taxi.Distance(bay, p)).First();
            Taxi.SetDown(player, shop);
            yield return new WaitForSeconds(2f); // parked cars settle round the new spot

            var phone = city.Phone;
            phone.Open(PhoneAppId.Garage);
            yield return null;
            var app = (GarageApp)phone.App(PhoneAppId.Garage);
            Assert.IsNull(app.Bring(car), app.StatusText);
            Assert.IsFalse(phone.IsOpen, "the phone goes away");
            yield return null;
            GameObject shown = city.Fleet.Shown(car);
            Assert.IsNotNull(shown);
            Vector3 me = player.transform.position;
            Assert.Less(Vector2.Distance(new Vector2(shown.transform.position.x, shown.transform.position.z), new Vector2(me.x, me.z)), 40f, "parked near the shop");
            Assert.Less(shown.transform.position.y, 1.5f, "on the street");
            Physics.SyncTransforms();
            // The car's footprint, above the kerb stone.
            foreach (Collider other in Physics.OverlapBox(shown.transform.position + Vector3.up * 0.8f, new Vector3(0.85f, 0.5f, 2.2f), shown.transform.rotation, ~0, QueryTriggerInteraction.Ignore))
                Assert.IsTrue(other.transform.IsChildOf(shown.transform) || other is TerrainCollider || other is MeshCollider, "clear of " + other.name + " at " + (other.transform.position - shown.transform.position).ToString("F1"));
            Assert.IsNotNull(Valet.Bring(phone.City, car, shown.transform.position), "already here");
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(1, Physics.OverlapBox(shown.transform.position + Vector3.up * 0.9f, new Vector3(0.9f, 0.5f, 2.2f), shown.transform.rotation, ~0, QueryTriggerInteraction.Ignore)
                .Count(c => c.GetComponentInParent<CarController>() != null || c.name.StartsWith("Parked")), "no stranger parks on it");

            city.Driver.Enter(car);
            yield return null;
            Assert.IsTrue(city.Driver.IsDriving, "drive it away");
            Assert.AreEqual("You're driving it.", Valet.Bring(phone.City, car, me));
            yield return CaptureWithHud(player, Find<InteractionHud>(), "my-cars.png");
        }
    }
}
