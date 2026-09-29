using System.Collections;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>[C]'s third-person view: the camera sits behind the shoulder, the whole body shows, and back again.</summary>
    public class ThirdPersonTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator ThirdPerson_BehindTheShoulder_AndBack()
        {
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var player = Find<FirstPersonController>();
            var city = Find<CityBuilder>();
            game.SkipTo(game.Clock.Now.Date.AddDays(1).AddHours(12));
            player.PlaceAt(city.Anchors["mart_front_out"] + new Vector3(0f, 0f, -3f), 90f, 10f);
            yield return null;
            Camera cam = player.GetComponentInChildren<Camera>();

            player.ThirdPerson = true;
            for (int i = 0; i < 40; i++) yield return null;
            Vector3 head = player.CameraPivot.position;
            float back = Vector3.Dot(head - cam.transform.position, player.transform.forward);
            Assert.Greater(back, 1.5f, "the camera is behind the head");
            Assert.Greater(player.CameraBack, 1.5f);
            var body = player.GetComponent<PlayerBody>();
            Transform headBone = body.Model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            Assert.AreEqual(1f, headBone.localScale.x, 1e-3f, "the head shows in third person");
            yield return CaptureCamera(cam, "third-person.png");

            player.ThirdPerson = false;
            for (int i = 0; i < 3; i++) yield return null;
            Assert.Less(Vector3.Distance(cam.transform.position, player.CameraPivot.position), 0.2f, "back in the head");
            Assert.Less(headBone.localScale.x, 0.01f, "the head hides again");
        }
    }
}
