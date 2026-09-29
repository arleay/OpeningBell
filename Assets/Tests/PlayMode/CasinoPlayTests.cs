using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Casino;
using OpeningBell.City;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OpeningBell.Tests
{
    /// <summary>
    /// CASINO_SPEC vertical slice: walk in through the doors, sign up and buy $500 of chips at the cage, sit at
    /// blackjack, play a hand paid exactly, save mid-hand and reload to the same cards, cash out, and leave.
    /// </summary>
    public class CasinoPlayTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator VerticalSlice_Cage_Blackjack_SaveMidHand_CashOut()
        {
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var city = Find<CityBuilder>();
            var player = Find<FirstPersonController>();
            var hud = Find<InteractionHud>();
            game.SkipTo(game.Clock.Now.Date.AddHours(21));

            // Walk in off the forecourt: the automatic doors open and nothing blocks the way to the lobby.
            UseSimulatedInput();
            player.PlaceAt(city.Anchors["casino_front"], 0f, 0f);
            yield return null;
            HoldKeys(Key.W);
            float lobbyZ = city.Anchors["casino_lobby"].z;
            yield return WaitUntil(() => player.transform.position.z > lobbyZ, 20f, "walking in through the doors");
            HoldKeys();
            yield return new WaitForSeconds(0.3f);
            yield return CaptureCamera(Camera.main, "casino-lobby.png");
            var spectator = new GameObject("Spectator").AddComponent<Camera>();
            spectator.fieldOfView = 70f;
            yield return Shot(spectator, Meridian.Origin + new Vector3(-2f, 4.6f, 2f), Meridian.Origin + new Vector3(0f, 0.5f, 20f), "casino-floor.png");
            yield return Shot(spectator, Meridian.Origin + new Vector3(0f, 2.6f, 16.5f), Meridian.Origin + new Vector3(-6f, 0.8f, 20.5f), "casino-pit.png");
            yield return Shot(spectator, Meridian.Origin + new Vector3(14f, 2.4f, 14.5f), Meridian.Origin + new Vector3(26f, 1.2f, 26f), "casino-bar.png");
            yield return Shot(spectator, Meridian.Origin + new Vector3(10.5f, 1.9f, 1.5f), Meridian.Origin + new Vector3(22f, 1.1f, 8f), "casino-slots.png");
            yield return Shot(spectator, Meridian.Origin + new Vector3(0f, 7f, -24f), Meridian.Origin + new Vector3(0f, 5f, 0f), "casino-outside.png");
            Object.Destroy(spectator.gameObject);

            // The cage: sign up, buy $500 from the bank.
            decimal bank = game.Economy.Bank.Balance;
            var cage = Find<CasinoCage>();
            player.PlaceAt(city.Anchors["casino_cage"], 270f, 5f);
            yield return null;
            cage.Interact();
            Assert.IsTrue(cage.IsOpen);
            Assert.IsFalse(player.ControlEnabled, "the cage has the mouse");
            Assert.IsNotNull(cage.Buy(500m), "players' card first");
            Click(hud.Root.Q("cage-join"));
            Assert.IsTrue(game.Casino.Member);
            Click(hud.Root.Q("cage-buy-500"));
            Assert.AreEqual(500m, game.Casino.Account.Chips);
            Assert.AreEqual(bank - 500m, game.Economy.Bank.Balance);
            Assert.IsNotNull(cage.Buy(1_000_000m), "the bank declines what it doesn't have");
            Assert.AreEqual(500m, game.Casino.Account.Chips, "a declined buy changes nothing");
            yield return CaptureWithHud(player, hud, "casino-cage.png");
            cage.Close();
            Assert.IsTrue(player.ControlEnabled);

            // Blackjack at the $5 table.
            BlackjackTable table = Object.FindObjectsByType<BlackjackTable>(FindObjectsSortMode.None).Single(t => t.Id == "meridian-bj-1");
            BlackjackSeat seat = table.GetComponentsInChildren<BlackjackSeat>().ElementAt(2);
            Assert.IsTrue(seat.CanInteract);
            seat.Interact();
            yield return WaitUntil(() => table.CanDeal, 5f, "seated at the table");
            Assert.IsFalse(player.ControlEnabled);
            Click(hud.Root.Q("bj-add-25"));
            Assert.AreEqual(25m, table.Bet);
            Click(hud.Root.Q("bj-deal"));
            Assert.AreEqual(475m, game.Casino.Account.Chips, "the stake comes off when the cards go out");
            yield return PlayOut(table);
            BlackjackRound round = table.Round;
            Assert.AreEqual(BlackjackPhase.Settled, round.Phase);
            Assert.AreEqual(500m - round.TotalWagered + round.TotalReturned, game.Casino.Account.Chips, "paid exactly what the hands won");
            Assert.AreEqual(round.TotalReturned - round.TotalWagered, game.Casino.Account.NetResult);
            yield return CaptureWithHud(player, hud, "casino-blackjack.png");

            // A hand left mid-play: can't stand up, and it survives save and reload with the same cards.
            int deals = 0;
            do
            {
                Assert.IsNull(table.Deal(), "deal");
                yield return WaitUntil(() => table.Revealed, 10f, "cards out");
                deals++;
            } while (table.Round.Phase != BlackjackPhase.PlayerTurn && deals < 20);
            Assert.AreEqual(BlackjackPhase.PlayerTurn, table.Round.Phase, "a hand that needs a decision");
            table.Leave();
            Assert.IsTrue(table.Seated, "can't leave mid-hand");
            decimal chips = game.Casino.Account.Chips;
            string cards = string.Join(",", table.Round.Hands[0].Cards);
            string dealerUp = table.Round.Dealer[0].ToString();
            game.Save();

            yield return SceneManager.LoadSceneAsync("Main");
            yield return null;
            game = Find<GameBootstrap>();
            hud = Find<InteractionHud>();
            player = Find<FirstPersonController>();
            table = Object.FindObjectsByType<BlackjackTable>(FindObjectsSortMode.None).Single(t => t.Id == "meridian-bj-1");
            Assert.AreEqual(chips, game.Casino.Account.Chips, "no refund, no double charge");
            Assert.IsTrue(game.Casino.Member);
            Assert.AreEqual(BlackjackPhase.PlayerTurn, table.Round.Phase, "the hand is still in play");
            Assert.AreEqual(cards, string.Join(",", table.Round.Hands[0].Cards));
            Assert.AreEqual(dealerUp, table.Round.Dealer[0].ToString());
            BlackjackTable other = Object.FindObjectsByType<BlackjackTable>(FindObjectsSortMode.None).Single(t => t.Id == "meridian-bj-2");
            Assert.IsFalse(other.CanSit, "your hand waits at its own table");
            Assert.IsNotNull(Find<CasinoCage>().CashOut(chips), "no cashing out with a hand in play");

            table.GetComponentsInChildren<BlackjackSeat>().First().Interact();
            yield return WaitUntil(() => table.Seated && table.Revealed, 5f, "back at the table");
            yield return new WaitForSeconds(0.7f);
            table.Stand();
            while (table.Round.Phase == BlackjackPhase.PlayerTurn)
            {
                yield return WaitUntil(() => table.Revealed, 10f, "next hand");
                table.Stand();
            }
            yield return WaitUntil(() => table.Revealed, 10f, "dealer's cards");
            Assert.AreEqual(chips + table.Round.TotalReturned, game.Casino.Account.Chips, "the restored hand settles once");
            table.Leave();
            yield return WaitUntil(() => !table.Seated && player.ControlEnabled, 5f, "stood up");

            // Cash out everything at the cage; the bank gets it all back.
            var cageAgain = Find<CasinoCage>();
            decimal before = game.Economy.Bank.Balance, cashing = game.Casino.Account.Chips;
            Assert.IsNull(cageAgain.CashOutAll());
            Assert.AreEqual(0m, game.Casino.Account.Chips);
            Assert.AreEqual(before + cashing, game.Economy.Bank.Balance);
            Assert.AreEqual(game.Casino.Account.TotalBought - game.Casino.Account.TotalCashedOut, -game.Casino.Account.NetResult,
                "every chip bought is either cashed out or lost at the tables");
        }

        private static IEnumerator PlayOut(BlackjackTable table)
        {
            yield return WaitUntil(() => table.Revealed, 10f, "cards dealt");
            while (table.Round.Phase == BlackjackPhase.PlayerTurn)
            {
                if (table.Round.Current.Total < 17) table.Hit();
                else table.Stand();
                yield return WaitUntil(() => table.Revealed, 10f, "cards dealt");
            }
        }

        private static IEnumerator Shot(Camera camera, Vector3 eye, Vector3 at, string file)
        {
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            yield return CaptureCamera(camera, file);
        }
    }
}
