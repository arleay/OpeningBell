using System.Collections;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>
    /// What the player sees of their own body: level, glancing down and looking at their feet, for a realistic and
    /// a Tiny character. Screenshots: TestResults/fp-{body}-{pitch}.png.
    /// </summary>
    public class FirstPersonBodyTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator OwnBody_LooksRight_AtEveryPitch()
        {
            yield return LoadMain();
            var city = Find<CityBuilder>();
            var game = Find<GameBootstrap>();
            var player = Find<FirstPersonController>();
            var body = player.GetComponent<PlayerBody>();
            Camera cam = player.GetComponentInChildren<Camera>();

            foreach (string person in new[] { "Men/Casual2", "Tiny/Suit_Male", "Tiny/Wizard" })
            {
                int index = -1;
                for (int i = 0; i < city.Art.People.Count; i++)
                    if (city.Art.PeopleLabel(i) == person) index = i;
                Assert.GreaterOrEqual(index, 0, person);
                game.Look = new PlayerLook { Model = index, ModelLabel = person };
                body.SetLook(game.Look);
                string name = person.Replace('/', '-');
                yield return null;
                foreach (float pitch in new[] { -50f, 0f, 30f, 60f, 85f })
                {
                    player.PlaceAt(new Vector3(6f, 0f, -7.25f), 90f, pitch);
                    for (int i = 0; i < 8; i++) yield return null;
                    yield return CaptureCamera(cam, $"fp-{name}-{pitch:0}.png");
                }
                Transform head = player.transform.Find("PlayerBody").GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
                // Local scale: the realistic rigs carry a ×100 bone scale, so lossy scale says little.
                Assert.Less(head.localScale.x, 0.01f, $"{person}: the head is hidden from the eyes");
                // Moving: walk and jog lean and bob the body, which is when a head would swing into view.
                var controller = player.GetComponent<CharacterController>();
                foreach (float speed in new[] { 1.4f, 5f })
                foreach (float pitch in new[] { 10f, 60f, 85f })
                {
                    player.PlaceAt(new Vector3(6f, 0f, -7.25f), 90f, pitch);
                    float closest = float.MaxValue;
                    for (int i = 0; i < 40; i++)
                    {
                        controller.Move(player.transform.forward * speed * Time.deltaTime);
                        yield return null;
                        if (pitch > 50f)
                        {
                            // How far the cut-off neck sits behind the eyes (horizontally), over the stride.
                            Vector3 neck = player.transform.InverseTransformPoint(
                                player.transform.Find("PlayerBody").GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head).position);
                            Vector3 eyes = player.transform.InverseTransformPoint(player.CameraPivot.position);
                            closest = Mathf.Min(closest, eyes.z - neck.z);
                        }
                    }
                    if (pitch > 50f) Assert.Greater(closest, 0.2f, $"{person} at {speed} m/s looking down {pitch}°: the neck stays behind the eyes");
                    yield return CaptureCamera(cam, $"fp-{name}-move{speed:0}-{pitch:0}.png");
                }
            }
        }
    }
}
