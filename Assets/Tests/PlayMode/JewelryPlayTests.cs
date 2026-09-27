using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>Lustre, the jeweller: buy a piece for every slot, see it on the body (mirror), read the watch.</summary>
    public class JewelryPlayTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator Jeweler_Sells_Wears_Mirrors_AndTheWatchTellsTime()
        {
            yield return LoadMain();
            var city = Find<CityBuilder>();
            var game = Find<GameBootstrap>();
            var player = Find<FirstPersonController>();
            game.SkipTo(game.Clock.Now.Date.AddDays(1).AddHours(11).AddMinutes(10));
            game.Economy.DevDeposit(200000m, game.Clock.Now);
            yield return null;

            var displays = Object.FindObjectsByType<JewelryDisplay>(FindObjectsSortMode.None);
            Assert.AreEqual(Jewelry.Catalog.Count, displays.Length, "every piece is on show");
            Camera cam = player.GetComponentInChildren<Camera>();

            {
                // Isolated: a brilliant and a gold ring, built out of the shop in daylight, then the same inside a case.
                CityContext ctx = displays[0].Context;
                var probeCam = new GameObject("Gem camera").AddComponent<Camera>();
                probeCam.nearClipPlane = 0.01f;
                probeCam.fieldOfView = 20f;
                Vector3 outside = player.transform.position + Vector3.up * 30f;
                var holder = new GameObject("Gem test").transform;
                holder.position = outside;
                var f = new Forge(2);
                f.Use(1).Gem(Vector3.zero, Vector3.up, 0.02f);
                f.Use(0).Torus(Vector3.down * 0.012f, Vector3.right, 0.012f, 0.002f);
                f.Build(holder, "Gem", Jewelry.MetalOf(ctx.P, Metal.YellowGold), ctx.P.Gem(new Color(0.92f, 0.95f, 1f)));
                probeCam.transform.position = outside + new Vector3(0f, 0.06f, -0.12f);
                probeCam.transform.LookAt(outside);
                yield return CaptureCamera(probeCam, "jewel-gem-daylight.png");
                Transform stand = displays.First(x => x.Item.Id == "pendant_diamond").transform.parent;
                holder.position = stand.TransformPoint(new Vector3(0.05f, 0.05f, -0.05f));
                probeCam.transform.position = holder.position + new Vector3(0f, 0.06f, 0f) - stand.forward * 0.12f;
                probeCam.transform.LookAt(holder.position);
                yield return CaptureCamera(probeCam, "jewel-gem-case.png");
                Object.Destroy(holder.gameObject);
                Object.Destroy(probeCam.gameObject);
            }
            // The cases, from a customer's eye.
            JewelryDisplay first = displays.First(d => d.Item.Id == "watch_diver");
            Transform caseT = first.transform.parent.parent;
            player.PlaceAt(caseT.TransformPoint(new Vector3(0f, 0f, -0.9f)), caseT.eulerAngles.y, 35f);
            for (int i = 0; i < 5; i++) yield return null;
            yield return CaptureCamera(cam, "jewel-case.png");
            // Close-ups, as if leaning over the glass.
            var macro = new GameObject("Macro camera").AddComponent<Camera>();
            macro.nearClipPlane = 0.02f;
            macro.fieldOfView = 35f;
            foreach (string id in new[] { "watch_diver", "watch_iced", "watch_chrono", "ring_solitaire", "ring_ruby", "chain_cuban", "pendant_diamond", "pearls", "bracelet_tennis", "glasses_aviator", "studs_diamond" })
            {
                Transform stand = displays.First(x => x.Item.Id == id).transform.parent;
                macro.transform.position = stand.TransformPoint(new Vector3(0f, 0.2f, -0.28f));
                macro.transform.LookAt(stand.TransformPoint(new Vector3(0f, 0.05f, 0f)));
                yield return CaptureCamera(macro, $"jewel-{id}.png");
            }
            Object.Destroy(macro.gameObject);

            // Buy one of each slot: owned, worn, and on the body.
            string[] buy = { "watch_diver", "chain_cuban", "bracelet_tennis", "ring_solitaire", "studs_diamond", "glasses_aviator" };
            decimal before = game.Economy.Bank.Balance;
            foreach (string id in buy) displays.First(d => d.Item.Id == id).Interact();
            Assert.AreEqual(before - buy.Sum(id => Jewelry.Get(id).Price), game.Economy.Bank.Balance, "paid for");
            CollectionAssert.AreEquivalent(buy, game.Look.Worn);
            yield return null;
            var body = player.GetComponent<PlayerBody>();
            int pieces = body.Model.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Jewellery");
            Assert.AreEqual(7, pieces, "six pieces, earrings in pairs");
            // Swapping within a slot takes the old one off.
            displays.First(d => d.Item.Id == "glasses_round").Interact();
            Assert.IsFalse(game.Look.Worn.Contains("glasses_aviator"));
            Assert.IsTrue(game.Look.Worn.Contains("glasses_round"));

            // The mirror shows the double wearing it all.
            Mirror standing = Object.FindObjectsByType<Mirror>(FindObjectsSortMode.None).First(m => m.name == "Glass");
            Vector3 normal = -standing.transform.forward;
            Vector3 spot = standing.transform.position + normal * 1.6f;
            spot.y = standing.transform.parent.position.y;
            player.PlaceAt(spot, Quaternion.LookRotation(-normal).eulerAngles.y, 5f);
            for (int i = 0; i < 6; i++) yield return null;
            yield return CaptureCamera(cam, "jewel-mirror.png");
            Assert.AreEqual(standing, Mirror.Current, "the mirror in front of you is live");
            Assert.IsNotNull(Mirror.Double, "a double stands in for you in the reflection");
            Assert.AreEqual(7, Mirror.Double.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Jewellery"));

            SaveTexture(Mirror.Current.Texture, "jewel-mirror-rt.png");
            // The double from four sides (a test camera; the game only sees it in mirrors).
            var around = new GameObject("Around camera").AddComponent<Camera>();
            around.cullingMask = 1 << Mirror.ReflectionLayer | 1;
            around.fieldOfView = 45f;
            Transform dRoot = Mirror.Double.transform;
            Vector3 chest = Mirror.Double.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Chest).position;
            foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
            {
                Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * dRoot.forward;
                around.transform.position = chest + dir * 1.3f + Vector3.up * 0.3f;
                around.transform.LookAt(chest + Vector3.up * 0.15f);
                yield return CaptureCamera(around, $"jewel-double-{yaw:0}.png");
            }
            Object.Destroy(around.gameObject);
            // A close look at the double from the front.
            var look = new GameObject("Test camera").AddComponent<Camera>();
            look.cullingMask = 1 << Mirror.ReflectionLayer | 1;
            Transform dbl = Mirror.Double.transform;
            Transform head = Mirror.Double.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            look.transform.position = head.position + dbl.forward * 0.9f - Vector3.up * 0.25f;
            look.transform.LookAt(head.position - Vector3.up * 0.3f);
            look.fieldOfView = 40f;
            yield return CaptureCamera(look, "jewel-body-front.png");
            Transform wrist = Mirror.Double.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftHand);
            look.transform.position = wrist.position + Vector3.up * 0.35f + dbl.forward * 0.2f;
            look.transform.LookAt(wrist.position);
            yield return CaptureCamera(look, "jewel-wrist.png");
            Object.Destroy(look.gameObject);

            // Checking the watch: the wrist comes up into view; its hands show the game's time.
            body.ForceWatch = true;
            player.PlaceAt(spot, Quaternion.LookRotation(normal).eulerAngles.y, 0f);
            for (int i = 0; i < 30; i++) yield return null;
            yield return CaptureCamera(cam, "jewel-check-watch.png");
            body.ForceWatch = false;
            WatchFace face = body.Model.GetComponentInChildren<WatchFace>();
            Assert.IsNotNull(face, "the watch is on the wrist");
            Transform minute = face.transform.Find("Minute hand");
            float expected = (game.Clock.Now.Minute + game.Clock.Now.Second / 60f) * 6f;
            Assert.AreEqual(0f, Mathf.DeltaAngle(minute.localEulerAngles.y, expected), 1.5f, "minute hand shows the time");

            // Saved and restored with the look.
            var copy = game.Look.Copy();
            copy.Worn.Clear();
            Assert.AreEqual(6, game.Look.Worn.Count, "a copy doesn't share the lists");
        }

        private static void SaveTexture(RenderTexture source, string file)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            var t = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            t.Apply();
            RenderTexture.active = previous;
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "TestResults");
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, file), t.EncodeToPNG());
            Object.Destroy(t);
        }
    }
}
