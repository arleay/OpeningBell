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
    /// The player has a body (seen in first person) and can punch: people react, get knocked down and get up,
    /// run or fight back, and fighters give up once the player is far enough away. Screenshots: combat-*.png.
    /// </summary>
    public class CombatPlayTests : SceneTestBase
    {
        private FirstPersonController _player;
        private Camera _camera;

        private IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator Player_HasBody_Punches_AndPeopleReact()
        {
            yield return LoadMain();
            _player = Find<FirstPersonController>();
            _camera = _player.GetComponentInChildren<Camera>();
            var game = Find<GameBootstrap>();
            var city = Find<CityBuilder>();
            var body = _player.GetComponent<PlayerBody>();
            var fists = _player.GetComponent<PlayerFists>();
            game.SkipTo(game.Clock.Now.Date.AddHours(12.4));
            yield return Frames(20);

            Assert.IsNotNull(body, "the player has a body");
            Assert.IsTrue(body.Visible, "and it shows while walking about");

            // Looking down in the street: legs and feet.
            _player.PlaceAt(new Vector3(20f, 0f, -7.25f), 90f, 70f);
            yield return Frames(4);
            yield return CaptureCamera(_camera, "combat-body.png");

            // Mid-punch: the arm comes into view.
            _player.PlaceAt(new Vector3(20f, 0f, -7.25f), 90f, 5f);
            yield return Frames(2);
            body.Punch(cross: true);
            yield return new WaitForSeconds(0.18f);
            yield return CaptureCamera(_camera, "combat-punch.png");
            yield return new WaitForSeconds(0.5f);
            body.Punch(cross: false);
            yield return new WaitForSeconds(0.07f);
            yield return CaptureCamera(_camera, "combat-jab.png");

            // Walk up to someone and hit them until they go down.
            PedestrianSimulation.Walker walker = city.Pedestrians.Simulation.Walkers
                .Where(w => w.State == PedestrianSimulation.WalkerState.Walking).First();
            NpcBody target = null;
            NpcFighter fighter = null;
            for (int i = 0; i < 8 && (fighter == null || !fighter.IsDown); i++)
            {
                Vector3 at = target != null ? target.transform.position : new Vector3(walker.Position.x, 0f, walker.Position.y);
                Vector3 toward = target != null ? target.transform.forward : new Vector3(walker.Heading.x, 0f, walker.Heading.y);
                // Stand a metre in front of them, facing them.
                Vector3 stand = at + toward.normalized * 1f;
                _player.PlaceAt(new Vector3(stand.x, 0f, stand.z), Quaternion.LookRotation(-toward).eulerAngles.y, 12f);
                yield return Frames(1);
                NpcBody hit = fists.Strike();
                Assert.IsNotNull(hit, $"punch {i} connects");
                target = hit;
                fighter = target.GetComponent<NpcFighter>();
                Assert.IsNotNull(fighter, "the person reacts");
                Assert.IsTrue(target.Overridden, "the fight drives the body, not the sidewalk simulation");
                yield return new WaitForSeconds(0.2f);
            }
            Assert.IsTrue(fighter.IsDown, "enough punches knock them down");
            yield return new WaitForSeconds(0.6f);
            yield return CaptureCamera(_camera, "combat-knockdown.png");

            // Someone who fights back chases, then gives up once the player is far away. Staged in the open (the
            // middle of Maple St) so nothing blocks their view by chance.
            NpcBody brawlerBody = Object.FindObjectsByType<NpcBody>(FindObjectsSortMode.None)
                .Where(b => b.gameObject.activeInHierarchy && !b.Overridden && b.gameObject.layer == CityLayers.Pedestrian).First();
            NpcFighter brawler = city.Pedestrians.Provoke(brawlerBody, _player, brave: true);
            Assert.IsNotNull(brawler);
            brawlerBody.transform.position = new Vector3(25f, -0.15f, -14f);
            _player.PlaceAt(new Vector3(27f, 0f, -14f), 270f);
            yield return Frames(1);
            brawler.TakeHit(10f, _player.transform.position);
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(NpcFighter.Mood.Fight, brawler.Current, "a brave one fights back");
            _player.PlaceAt(new Vector3(35f, 0f, -14f), 270f);
            Vector3 start = brawlerBody.transform.position;
            yield return new WaitForSeconds(1f);
            Assert.Less((brawlerBody.transform.position - _player.transform.position).magnitude,
                (start - _player.transform.position).magnitude - 1.5f, "and chases");

            _player.PlaceAt(_player.transform.position + new Vector3(60f, 0f, 0f), 270f);
            yield return new WaitForSeconds(0.6f);
            Assert.AreNotEqual(NpcFighter.Mood.Fight, brawler.Current, "gives up once the player is far away");
        }
    }
}
