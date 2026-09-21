using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Economy;
using OpeningBell.Gameplay;
using OpeningBell.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OpeningBell.Tests
{
    /// <summary>Phase 7 acceptance: trading money pays for life. Transfer, buy an upgrade, see it, pay rent, keep it after a reload.</summary>
    public class EconomyPlayTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator Player_TransfersProfits_BuysUpgrade_AndPaysRent()
        {
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var terminal = Find<TradingTerminal>();
            var workstation = Find<WorkstationController>();
            var hud = Find<InteractionHud>();
            decimal startBank = game.Economy.Bank.Balance;
            Assert.AreEqual(1800m, startBank);

            yield return SitDown(workstation);
            RenderTerminalOffscreen(terminal);
            yield return null;
            VisualElement root = terminal.Root;

            // Bank app: move $1,000 from the brokerage.
            Press(root.Q<Button>("app-bank"));
            Assert.AreEqual(TerminalApp.Bank, terminal.Context.App);
            root.Q<TextField>("transfer-amount").value = "1000";
            Press(root.Q<Button>("transfer-from-broker"));
            Assert.AreEqual(startBank + 1000m, game.Economy.Bank.Balance);
            Assert.AreEqual(9000m, game.Account.Cash);
            terminal.RefreshAll();
            yield return null;
            yield return null;
            SaveTerminalScreenshot("bank-app.png");

            // Store app: buy the ergonomic chair; it replaces the cheap one in the room.
            Press(root.Q<Button>("app-store"));
            Press(root.Q<Button>("buy-ergonomic_chair"));
            Assert.IsTrue(game.Economy.Owns("ergonomic_chair"));
            Assert.IsTrue(GameObject.Find("Chair_Ergo").activeInHierarchy, "upgrade appears in the apartment");
            Assert.IsNull(GameObject.Find("Chair_Basic"), "cheap chair is gone");
            terminal.RefreshAll();
            yield return null;
            yield return null;
            SaveTerminalScreenshot("store-app.png");
            Press(root.Q<Button>("app-broker"));

            terminal.ShowOnScreen(true);
            workstation.StandUp();
            yield return WaitUntil(() => workstation.State == WorkstationState.Standing, 5f, "standing");
            yield return CaptureCamera(Find<FirstPersonController>().GetComponentInChildren<Camera>(), "apartment-upgraded.png");

            // Time passes: rent (the 15th) comes out of the bank and the HUD says so.
            DateTime rentDay = new DateTime(game.Clock.Now.Year, game.Clock.Now.Month, 15);
            game.SkipTo(rentDay.AddHours(9));
            yield return null;
            Assert.IsTrue(game.Economy.Bank.Transactions.Any(t => t.Kind == TransactionKind.Rent), "rent charged");
            decimal bankAfterRent = game.Economy.Bank.Balance;
            Assert.Less(bankAfterRent, startBank + 1000m - 320m);

            // Quit and continue: the chair and the bank balance survive.
            game.Save();
            yield return SceneManager.LoadSceneAsync("Main");
            var loaded = Find<GameBootstrap>();
            Assert.AreEqual(bankAfterRent, loaded.Economy.Bank.Balance);
            Assert.IsTrue(loaded.Economy.Owns("ergonomic_chair"));
            yield return null; // presenter applies visuals in Start
            Assert.IsTrue(GameObject.Find("Chair_Ergo").activeInHierarchy, "upgrade still there after loading");
        }
    }
}
