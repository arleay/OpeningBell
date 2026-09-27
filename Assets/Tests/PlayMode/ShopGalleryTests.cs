using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>Every business's interior at midday, from just inside the door: `TestResults/shop-*.png` for review.</summary>
    public class ShopGalleryTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator EveryShop_Interior()
        {
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var player = Find<FirstPersonController>();
            game.SkipTo(game.Clock.Now.Date.AddDays(1).AddHours(12));
            yield return null;
            Camera cam = player.GetComponentInChildren<Camera>();
            int shots = 0;
            foreach (Business b in BusinessPlan.All().Where(x => x.Trade != Trade.Vacant))
            {
                Transform root = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                    .FirstOrDefault(t => t.name == b.Name && t.Find("Interior") != null);
                if (root == null) continue;
                // Inside the door, a little to one side, looking toward the back corner.
                Vector3 at = root.TransformPoint(new Vector3(-b.Width * 0.3f, 0f, 1.2f));
                player.PlaceAt(at, root.eulerAngles.y + 15f, 12f);
                for (int i = 0; i < 3; i++) yield return null;
                string file = "shop-" + new string(b.Name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant() + ".png";
                yield return CaptureCamera(cam, file);
                shots++;
            }
            Assert.Greater(shots, 30, "most businesses photographed");

            // The hand-built places: a free camera from a fixed spot near each.
            var city = Find<CityBuilder>();
            var free = new GameObject("Gallery camera").AddComponent<Camera>();
            free.fieldOfView = 60f;
            foreach (var (anchor, offset, look, file) in new[]
            {
                ("coffee_counter", new Vector3(0f, 1.6f, -3f), new Vector3(0f, 1.1f, 4f), "shop-halfpastnine.png"),
                ("mart_counter", new Vector3(2f, 1.7f, -1f), new Vector3(5f, 1f, 6f), "shop-cornermart.png"),
                ("fuel_pump_west", new Vector3(3f, 1.7f, -3f), new Vector3(0f, 1f, 0f), "shop-fuel.png"),
                ("mechanic_bay1", new Vector3(0f, 1.8f, -4f), new Vector3(0f, 1f, 8f), "shop-mechanic.png"),
            })
            {
                if (!city.Anchors.TryGetValue(anchor, out Vector3 at)) continue;
                free.transform.position = at + offset;
                free.transform.LookAt(at + look);
                yield return CaptureCamera(free, file);
            }
            Object.Destroy(free.gameObject);
        }
    }
}
