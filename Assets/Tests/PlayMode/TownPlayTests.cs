using System.Collections;
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
