using OpeningBell.Home;
using UnityEngine;

namespace OpeningBell.UI
{
    /// <summary>A store's website in the browser: the shared <see cref="ShopView"/> filling the page.</summary>
    internal sealed class ShopSite : BrowserPage
    {
        public const string FurnitureHost = "timberlinehome.com", TechHost = "circuitstop.com";

        private readonly ShopView _view;
        private readonly string _title;

        public ShopSite(BrowserApp browser, HomeStore store) : base(browser, "site-shop")
        {
            bool tech = store == HomeStore.Tech;
            _title = tech ? "Circuit Stop · Monitors, computers and desk tech" : "Timberline Home · Furniture for every room";
            _view = new ShopView(Context.Game, store, tech ? "Circuit Stop" : "Timberline Home",
                tech ? "Monitors, arms, computers and desk tech. Delivered in under an hour." : "Furniture for every room. Delivered in under an hour, or moved in for you.",
                tech ? ShopView.TechBrand : ShopView.FurnitureBrand, compact: false);
            _view.Root.style.flexGrow = 1;
            Root.Add(_view.Root);
        }

        public override string Show(string path) => _title;
        public override void Refresh() => _view.Refresh();
    }
}
