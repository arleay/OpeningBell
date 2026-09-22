using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OpeningBell.Tests
{
    /// <summary>The HUD minimap shows up with its places marked; screenshots land in TestResults/minimap*.png.</summary>
    public class MinimapTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator Minimap_ShowsTown_AndMarksPlaces()
        {
            yield return LoadMain();
            var city = Find<CityBuilder>();
            var player = Find<FirstPersonController>();
            var hud = Find<InteractionHud>();
            for (int i = 0; i < 10; i++) yield return null;

            Minimap map = city.Minimap;
            Assert.IsTrue(map.Visible, "minimap on screen while walking");
            MapIcon[] marked = map.Places.Select(p => p.Icon).ToArray();
            CollectionAssert.IsSubsetOf(new[] { MapIcon.Home, MapIcon.Office, MapIcon.Coffee, MapIcon.Mart, MapIcon.Fuel }, marked);

            // From home, the home icon is near the middle and the office is pinned to the rim, off towards it.
            VisualElement frame = hud.Root.Q("minimap");
            Assert.IsNotNull(frame);
            player.PlaceAt(new Vector3(6f, 0f, -7.5f), 90f);
            for (int i = 0; i < 4; i++) yield return null;
            VisualElement home = frame.Q("map-icon-Home"), office = frame.Q("map-icon-Office");
            Vector2 centre = new Vector2(105f, 105f);
            Vector2 Middle(VisualElement e) => new Vector2(e.resolvedStyle.left + 13f, e.resolvedStyle.top + 13f);
            Assert.Less(Vector2.Distance(Middle(home), centre), 20f, "home is right here");
            Assert.Greater(Vector2.Distance(Middle(office), centre), 80f, "the office is far: on the rim");
            Assert.Less(Middle(office).y, centre.y, "and ahead (east, the way the player faces)");

            yield return ShotWithHud(player, hud, "minimap.png");
            player.PlaceAt(new Vector3(-100f, 0f, -7.5f), 180f);
            for (int i = 0; i < 4; i++) yield return null;
            yield return ShotWithHud(player, hud, "minimap-west.png");

            string dir = Path.Combine(Application.dataPath, "..", "TestResults");
            var painted = (Texture2D)frame.Children().First().resolvedStyle.backgroundImage.texture;
            File.WriteAllBytes(Path.Combine(dir, "map-town.png"), painted.EncodeToPNG());
        }

        /// <summary>
        /// The camera view with the HUD on top. Screen capture never completes in batch mode (no frame is presented),
        /// so the HUD panel renders into its own transparent texture and is laid over the camera image here.
        /// </summary>
        private static IEnumerator ShotWithHud(FirstPersonController player, InteractionHud hud, string file)
        {
            const int w = 1600, h = 900;
            Camera camera = player.GetComponentInChildren<Camera>();
            PanelSettings panel = hud.GetComponent<UIDocument>().panelSettings;
            var world = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var ui = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousTarget = panel.targetTexture;
            bool previousClear = panel.clearColor;
            Color previousClearValue = panel.colorClearValue;
            camera.targetTexture = world;
            panel.targetTexture = ui;
            panel.clearColor = true;
            panel.colorClearValue = new Color(0f, 0f, 0f, 0f);
            for (int i = 0; i < 3; i++) yield return null;

            Color[] bottom = Read(world), top = Read(ui);
            for (int i = 0; i < bottom.Length; i++) bottom[i] = Color.Lerp(bottom[i], top[i], top[i].a);
            var shot = new Texture2D(w, h, TextureFormat.RGBA32, false);
            shot.SetPixels(bottom);
            shot.Apply();
            string dir = Path.Combine(Application.dataPath, "..", "TestResults");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, file), shot.EncodeToPNG());

            camera.targetTexture = null;
            panel.targetTexture = previousTarget;
            panel.clearColor = previousClear;
            panel.colorClearValue = previousClearValue;
            Object.Destroy(shot);
            world.Release();
            ui.Release();
        }

        private static Color[] Read(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            var t = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            t.Apply();
            RenderTexture.active = previous;
            Color[] pixels = t.GetPixels();
            Object.Destroy(t);
            return pixels;
        }
    }
}
