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
            Assert.IsTrue(Physics.Raycast(new Vector3(315f, 20f, 40f), Vector3.down, out RaycastHit canal, 40f));
            Assert.Less(canal.point.y, TownTerrain.CanalWater - 0.5f, "the canal bed is under water");

            // Walk into the canal: climb back out onto the bank.
            player.PlaceAt(new Vector3(296f, 0.2f, 40f), 90f, 0f);
            yield return new WaitForSeconds(1.3f); // a safe spot is remembered each second
            player.PlaceAt(new Vector3(315f, TownTerrain.CanalFloor + 0.1f, 40f), 90f, 0f);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.Less(player.transform.position.x, 300f, "back on the bank");
            Assert.IsNotNull(city.Anchors["station_platform"], "the station is built");
        }
    }
}
