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
        }
    }
}
