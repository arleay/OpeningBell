using OpeningBell.Home;
using OpeningBell.UI;

namespace OpeningBell.City
{
    /// <summary>Timberline Home's or Circuit Stop's app: the same store as the website, laid out for the phone.</summary>
    internal sealed class ShopApp : PhoneScreen
    {
        private readonly PhoneAppId _id;
        private readonly ShopView _view;

        public override PhoneAppId Id => _id;

        public ShopApp(Phone phone, HomeStore store) : base(phone)
        {
            bool tech = store == HomeStore.Tech;
            _id = tech ? PhoneAppId.CircuitStop : PhoneAppId.Timberline;
            // Room for the status bar above the store's own bar.
            Root.style.paddingTop = 50f;
            Root.style.paddingBottom = 26f;
            _view = new ShopView(phone.Game, store, tech ? "Circuit Stop" : "Timberline Home", "",
                tech ? ShopView.TechBrand : ShopView.FurnitureBrand, compact: true);
            Root.Add(_view.Root);
        }

        public override void Refresh() => _view.Refresh();
    }
}
