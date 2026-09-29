using System.Collections;
using NUnit.Framework;
using OpeningBell.Gameplay;
using OpeningBell.Trading;
using OpeningBell.UI;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OpeningBell.Tests
{
    /// <summary>PROP_SPEC PF2/PF3: buy on a firm's website, trade it from the terminal, copy to a second account.</summary>
    public class PropFirmPlayTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator BuyOnTheWeb_TradeInTheTerminal_CopyToASecondAccount()
        {
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var terminal = Find<TradingTerminal>();
            yield return SitDown(Find<WorkstationController>());
            RenderTerminalOffscreen(terminal);
            game.SkipTo(game.Clock.Now.Date.AddHours(10));
            game.IsPaused = true;
            game.Economy.Receive(1_000m, "Test money", game.Market.Now);
            decimal bank = game.Economy.Bank.Balance;
            yield return null;
            VisualElement root = terminal.Root;

            // Profit Harbor's site: plans, then buy a 50K Test (second click confirms).
            OpenApp(terminal, "browser");
            Press(root.Q<Button>("bookmark-harbor"));
            Assert.AreEqual("profitharbor.com", terminal.Browser.Url);
            terminal.RefreshAll();
            yield return null;
            yield return null;
            SaveTerminalScreenshot("prop-site.png");
            Press(root.Q<Button>("buy-harbor-50k"));
            Assert.AreEqual(0, game.Prop.Accounts.Count, "first click only arms the purchase");
            Press(root.Q<Button>("buy-harbor-50k"));
            Assert.AreEqual(1, game.Prop.Accounts.Count);
            PropAccount harbor = game.Prop.Accounts[0];
            Assert.AreEqual(bank - 170m, game.Economy.Bank.Balance, "paid from the bank");
            Assert.AreEqual("profitharbor.com/dashboard", terminal.Browser.Url);
            terminal.RefreshAll();
            yield return null;
            yield return null;
            SaveTerminalScreenshot("prop-dashboard.png");

            Assert.IsNull(game.Prop.Buy(PropFirms.Ridgeback, 50_000, out PropAccount ridgeback));

            OpenApp(terminal, "broker");
            terminal.RefreshAll();

            // Account menu: make Harbor the active account, tick Ridgeback to copy it.
            Press(root.Q<Button>("account-select"));
            terminal.RefreshAll();
            yield return null;
            Click(root.Q("account-row-" + harbor.Id).Q(className: "account-row-text"));
            Assert.AreSame(harbor, terminal.Context.Active.Prop);
            Press(root.Q<Button>("account-select"));
            terminal.RefreshAll();
            yield return null;
            root.Q<Toggle>("copy-" + ridgeback.Id).value = true;
            Assert.AreEqual(1, terminal.Context.FollowerCount);
            terminal.RefreshAll();
            yield return null;
            yield return null;
            SaveTerminalScreenshot("prop-accounts.png");
            Press(root.Q<Button>("account-select")); // close the menu

            Click(root.Q("watch-APEX"));
            Press(root.Q<Button>("side-buy"));
            Press(root.Q<Button>("type-market"));
            root.Q<TextField>("qty").value = "2";
            Press(root.Q<Button>("submit-order"));
            Assert.AreEqual(2, harbor.OpenContracts);
            Assert.AreEqual(2, ridgeback.OpenContracts, "copied 1:1");
            StringAssert.Contains("Copied to 1 account", root.Q<Label>("order-status").text);

            // Ridgeback's 50K Challenge allows 5 contracts: a 4-lot more fills on Harbor (6 max) but not on Ridgeback.
            root.Q<TextField>("qty").value = "4";
            Press(root.Q<Button>("submit-order"));
            Assert.AreEqual(6, harbor.OpenContracts);
            Assert.AreEqual(2, ridgeback.OpenContracts);
            StringAssert.Contains("Not copied", root.Q<Label>("order-status").text);
            terminal.RefreshAll();
            yield return null;
            yield return null;
            SaveTerminalScreenshot("prop-terminal.png");
        }
    }
}
