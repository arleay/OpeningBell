using System.Collections;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>
    /// The start-up menu and character creator: the chosen look becomes the player's body and is saved. (The title
    /// is skipped in batch mode, so the test opens it.) Screenshots: TestResults/title*.png.
    /// </summary>
    public class TitleScreenTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator Creator_ChoosesCharacter_AndStartsPlaying()
        {
            yield return LoadMain();
            var city = Find<CityBuilder>();
            var game = Find<GameBootstrap>();
            var player = Find<FirstPersonController>();
            var hud = Find<InteractionHud>();
            TitleScreen title = city.Title;
            Assert.IsNotNull(title);
            Assert.IsFalse(title.Visible, "not shown in batch mode");

            title.Open();
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsTrue(title.Visible);
            Assert.IsFalse(player.ControlEnabled, "the world waits behind the menu");
            yield return CaptureWithHud(player, hud, "title.png");

            title.ShowCreator();
            title.Step(0, 1); // the other body type
            title.Step(1, 2); // a different outfit
            title.Pick(0, 4);
            title.Pick(1, 5);
            title.Pick(2, 3);
            for (int i = 0; i < 5; i++) yield return null;

            // Turning by hand: the character shows its back, and the auto-spin holds off so the pose stays put.
            Transform turntable = title.transform.Find("Title stage/Turntable");
            float before = turntable.eulerAngles.y;
            title.Turn(180f);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(Mathf.Repeat(before + 180f, 360f), turntable.eulerAngles.y, 0.5f, "the manual turn holds");
            yield return CaptureWithHud(player, hud, "title-creator-back.png");
            title.Turn(-180f);
            yield return CaptureWithHud(player, hud, "title-creator.png");

            PlayerLook chosen = title.Look.Copy();
            title.StartGame();
            yield return null;
            Assert.IsFalse(title.Visible);
            Assert.IsTrue(player.ControlEnabled, "playing");
            Assert.IsNotNull(game.Look);
            Assert.AreEqual(chosen.Model, game.Look.Model);
            Assert.AreEqual(4, game.Look.Skin);
            Transform model = player.transform.Find("PlayerBody");
            Assert.IsNotNull(model, "the body was rebuilt");

            game.Save();
            Assert.IsTrue(SaveSystem.TryRead("slot1", out SaveGame save, out _), "saved");
            Assert.IsTrue(save.HasLook, "the look is saved");
            Assert.AreEqual(chosen.Model, save.Look.Model);
            Assert.AreEqual(chosen.Top, save.Look.Top);
        }
    }
}
