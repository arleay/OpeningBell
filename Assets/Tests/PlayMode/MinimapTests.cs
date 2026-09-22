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

            yield return CaptureWithHud(player, hud, "minimap.png");
            player.PlaceAt(new Vector3(-100f, 0f, -7.5f), 180f);
            for (int i = 0; i < 4; i++) yield return null;
            yield return CaptureWithHud(player, hud, "minimap-west.png");

            string dir = Path.Combine(Application.dataPath, "..", "TestResults");
            var painted = (Texture2D)frame.Children().First().resolvedStyle.backgroundImage.texture;
            File.WriteAllBytes(Path.Combine(dir, "map-town.png"), painted.EncodeToPNG());
        }

    }
}
