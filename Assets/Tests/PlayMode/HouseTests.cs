using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>
    /// The town's houses are walkable: each tier has a clear way in through the front door and rooms inside.
    /// Screenshots of one of each, outside, inside and upstairs, land in TestResults/house-*.png.
    /// </summary>
    public class HouseTests : SceneTestBase
    {
        private FirstPersonController _player;

        private IEnumerator Shot(Transform house, Vector3 local, float localYaw, float pitch, string file)
        {
            _player.PlaceAt(house.TransformPoint(local), house.eulerAngles.y + localYaw, pitch);
            for (int i = 0; i < 4; i++) yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), file);
        }

        [UnityTest]
        public IEnumerator Houses_AreBuilt_AndWalkable()
        {
            yield return LoadMain();
            _player = Find<FirstPersonController>();
            GameBootstrap game = Find<GameBootstrap>();
            game.SkipTo(game.Clock.Now.Date.AddHours(12.5));
            for (int i = 0; i < 10; i++) yield return null;

            Transform[] houses = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name.StartsWith("House ")).ToArray();
            Assert.Greater(houses.Length, 40, "the town has houses");
            foreach (HouseTier tier in new[] { HouseTier.Starter, HouseTier.Family, HouseTier.Mansion })
            {
                Transform house = houses.Where(h => h.name.EndsWith(" " + tier)).OrderBy(h => h.position.sqrMagnitude).FirstOrDefault();
                Assert.IsNotNull(house, $"a {tier} house");
                Vector2 size = HouseBuilder.Footprint(tier);
                float doorX = tier == HouseTier.Starter ? -2f : tier == HouseTier.Family ? 3.2f : 0f;

                // Open the front door, then check nothing solid blocks the way in at chest height.
                Door door = Object.FindObjectsByType<Door>(FindObjectsSortMode.None)
                    .OrderBy(d => (d.transform.position - house.TransformPoint(new Vector3(doorX, 0f, -size.y / 2f))).sqrMagnitude).First();
                door.Open();
                yield return new WaitForSeconds(1f);
                Vector3 outside = house.TransformPoint(new Vector3(doorX, 1.2f, -size.y / 2f - 1.2f));
                Vector3 inside = house.TransformPoint(new Vector3(doorX, 1.2f, -size.y / 2f + 1.6f));
                Assert.IsFalse(Physics.Linecast(outside, inside, ~0, QueryTriggerInteraction.Ignore), $"{tier}: the doorway is clear once the door is open");

                string name = tier.ToString().ToLowerInvariant();
                yield return Shot(house, new Vector3(doorX - 2f, 0f, -size.y / 2f - 9f), 12f, -6f, $"house-{name}-outside.png");
                yield return Shot(house, new Vector3(doorX, 0f, -size.y / 2f + 1.2f), -35f, 12f, $"house-{name}-inside.png");
                if (tier != HouseTier.Starter)
                {
                    float upper = tier == HouseTier.Family ? HouseBuilder.Floor + HouseBuilder.Storey : HouseBuilder.Floor + 3.4f;
                    yield return Shot(house, new Vector3(tier == HouseTier.Family ? 5f : -1.5f, upper + 0.05f, 2.8f), -110f, 10f, $"house-{name}-upstairs.png");
                }
            }
        }
    }
}
