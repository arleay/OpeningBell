using System.Collections;
using NUnit.Framework;
using OpeningBell.Gameplay;
using OpeningBell.Market;
using OpeningBell.UI;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OpeningBell.Tests
{
    /// <summary>The computer boots to a desktop; News, Mail, the browser (search, bank, marketplace, stock pages) and the trading terminal are apps.</summary>
    public class ComputerTests : SceneTestBase
    {
        [Test]
        public void Browser_ResolvesAddressesAndSearches()
        {
            Assert.AreEqual("kvcu.com", BrowserApp.Resolve("https://www.KVCU.com/"));
            Assert.AreEqual("tickerpage.com/APEX", BrowserApp.Resolve("tickerpage.com/APEX"));
            Assert.AreEqual("kestrel.com/search?q=apex corp", BrowserApp.Resolve(" apex corp "));
            Assert.AreEqual("kestrel.com/search?q=bank", BrowserApp.Resolve("bank"));
            Assert.AreEqual(BrowserApp.Home, BrowserApp.Resolve(""));
        }

        [UnityTest]
        public IEnumerator Computer_BootsToDesktop_AndRunsApps()
        {
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var terminal = Find<TradingTerminal>();
            for (int i = 0; i < 5; i++) yield return null;
            SavePng(terminal.WorldTexture, "computer-monitor.png"); // what the desk monitor shows while standing
            yield return SitDown(Find<WorkstationController>());
            RenderTerminalOffscreen(terminal);
            game.SkipTo(game.Clock.Now.Date.AddHours(10.5)); // a morning of prices and headlines
            game.IsPaused = true;
            terminal.RefreshAll();
            yield return null;
            yield return null;
            VisualElement root = terminal.Root;

            // Desktop first, like a real computer.
            Assert.AreEqual(TerminalApp.Desktop, terminal.Context.App);
            Assert.AreEqual(DisplayStyle.Flex, root.Q(className: "desktop").resolvedStyle.display);
            Assert.IsNotEmpty(root.Q<Label>("desktop-time").text);
            SaveTerminalScreenshot("computer-desktop.png");

            // News app opens on the latest story.
            Press(root.Q<Button>("icon-news"));
            Assert.AreEqual(TerminalApp.News, terminal.Context.App);
            terminal.RefreshAll();
            yield return null;
            yield return null;
            var news = game.Market.News;
            Assert.Greater(news.Count, 0, "headlines by 10:30");
            Assert.AreEqual(news[news.Count - 1].Headline, root.Q<Label>("article-headline").text);
            SaveTerminalScreenshot("computer-news.png");

            // Browser: home page is the search engine; plain words search.
            OpenApp(terminal, "browser");
            BrowserApp browser = terminal.Browser;
            Assert.AreEqual(BrowserApp.Home, browser.Url);
            yield return null;
            SaveTerminalScreenshot("computer-browser-home.png");

            browser.Navigate("apex");
            Assert.AreEqual("kestrel.com/search?q=apex", browser.Url);
            yield return null;
            yield return null;
            SaveTerminalScreenshot("computer-search.png");
            Click(root.Q("result-APEX"));
            Assert.AreEqual("tickerpage.com/APEX", browser.Url);
            game.Market.TryGetSecurity("APEX", out SecurityRuntimeState apex);
            Assert.AreEqual(apex.Spec.CompanyName, root.Q<Label>("tp-company").text);
            terminal.RefreshAll();
            yield return null;
            yield return null;
            SaveTerminalScreenshot("computer-tickerpage.png");

            // History.
            Press(root.Q<Button>("nav-back"));
            Assert.AreEqual("kestrel.com/search?q=apex", browser.Url);
            Press(root.Q<Button>("nav-forward"));
            Assert.AreEqual("tickerpage.com/APEX", browser.Url);

            // Nowhere else exists.
            browser.Navigate("https://www.example.com/page");
            StringAssert.Contains("example.com", root.Q<Label>("not-found-detail").text);

            // Bookmarks: the market list, then the bank site.
            Press(root.Q<Button>("bookmark-tickerpage"));
            Assert.AreEqual("tickerpage.com", browser.Url);
            Assert.NotNull(root.Q("tp-row-APEX"), "every stock listed");
            terminal.RefreshAll();
            yield return null;
            yield return null;
            SaveTerminalScreenshot("computer-markets.png");
            Press(root.Q<Button>("bookmark-bank"));
            Assert.NotNull(root.Q<TextField>("transfer-amount"), "bank site shows the account");

            // A stock page hands off to the trading terminal.
            browser.Navigate("tickerpage.com/apex");
            Press(root.Q<Button>("tp-trade"));
            Assert.AreEqual(TerminalApp.Broker, terminal.Context.App);
            Assert.AreEqual("APEX", terminal.Context.SelectedTicker);

            Press(root.Q<Button>("app-desktop"));
            Assert.AreEqual(TerminalApp.Desktop, terminal.Context.App);
        }
    }
}
