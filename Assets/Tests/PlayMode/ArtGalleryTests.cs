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
    /// Art pass check: the city dresses itself with the third-party models (people are animated characters, not
    /// boxes) and a tour of screenshots lands in TestResults/art-*.png for eyeballing.
    /// </summary>
    public class ArtGalleryTests : SceneTestBase
    {
        private FirstPersonController _player;
        private CityBuilder _city;

        private IEnumerator Shot(Vector3 at, float yaw, float pitch, string file)
        {
            _player.PlaceAt(at, yaw);
            _player.CameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            for (int i = 0; i < 4; i++) yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), file);
        }

        [UnityTest]
        public IEnumerator City_IsDressed_AndPhotographed()
        {
            yield return LoadMain();
            _player = Find<FirstPersonController>();
            _city = Find<CityBuilder>();
            GameBootstrap game = Find<GameBootstrap>();
            game.SkipTo(game.Clock.Now.Date.AddHours(12.4));
            for (int i = 0; i < 20; i++) yield return null;

            NpcBody[] people = Object.FindObjectsByType<NpcBody>(FindObjectsSortMode.None);
            Assert.Greater(people.Length, 5, "people about at lunchtime");
            Assert.IsTrue(people.All(p => p.IsCharacter), "everyone is an animated character");

            // A walker up close, from the front.
            PedestrianSimulation.Walker w = _city.Pedestrians.Simulation.Walkers
                .Where(x => x.State == PedestrianSimulation.WalkerState.Walking)
                .OrderBy(x => Vector2.Distance(x.Position, new Vector2(20f, -7f))).First();
            Vector2 ahead = w.Position + w.Heading * 3.2f;
            float face = Mathf.Atan2(-w.Heading.x, -w.Heading.y) * Mathf.Rad2Deg;
            yield return Shot(new Vector3(ahead.x, 0f, ahead.y), face, 6f, "art-people.png");

            yield return Shot(new Vector3(6f, 0f, -7.25f), 90f, 0f, "art-street.png");
            yield return Shot(new Vector3(4f, 0f, -12.8f), 10f, -8f, "art-home.png");
            yield return Shot(new Vector3(84f, 0f, -12.8f), -25f, -8f, "art-shops.png");
            yield return Shot(new Vector3(58f, 0f, -11.5f), 60f, -4f, "art-commercial.png");
            yield return Shot(new Vector3(138.5f, 0f, -11.5f), 40f, -8f, "art-downtown.png");
            yield return Shot(_city.Anchors["calder_lobby"] + new Vector3(0f, 0f, -1.5f), 0f, 4f, "art-lobby.png");
            yield return Shot(_city.Anchors["coffee_front_out"] + new Vector3(0f, 0f, 3f), 0f, 4f, "art-coffee.png");
            yield return Shot(new Vector3(-20f, 0f, 20f), 45f, 0f, "art-park.png");
            yield return Shot(new Vector3(60f, 45f, -70f), 20f, 32f, "art-aerial.png");

            game.SkipTo(game.Clock.Now.Date.AddHours(21.2));
            for (int i = 0; i < 10; i++) yield return null;
            yield return Shot(new Vector3(6f, 0f, -7.25f), 90f, 0f, "art-night.png");
        }
    }
}
