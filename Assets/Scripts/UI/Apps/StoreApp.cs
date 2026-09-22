using System.Collections.Generic;
using System.Linq;
using OpeningBell.Economy;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>Equipment and services, paid from the bank account. Purchases show up in the apartment.</summary>
    public sealed class StoreApp : TerminalPanel
    {
        private sealed class Card
        {
            public StoreItem Item;
            public Button Buy;
            public Label Note;
            public VisualElement Root;
        }

        private readonly List<Card> _cards = new List<Card>();
        private readonly Label _funds, _status;

        public StoreApp(TerminalContext context) : base(context, "store-app")
        {
            var header = Ui.Box("store-header", Root);
            Ui.Label("panel-title store-title", header, "STORE");
            _funds = Ui.Label("muted", header);
            _status = Ui.Label("ticket-status store-status", Root);
            _status.name = "store-status";

            var grid = Ui.Box("store-grid", Root);
            foreach (StoreItem item in context.Economy.Catalog)
            {
                var card = new Card { Item = item, Root = Ui.Box("app-card store-card", grid) };
                Ui.Label("store-name", card.Root, item.Name);
                Ui.Label("muted store-category", card.Root, item.Category switch
                {
                    StoreCategory.Service => "SERVICE",
                    StoreCategory.Lease => "LEASE",
                    _ => "EQUIPMENT",
                });
                Ui.Label("store-description", card.Root, item.Description);
                Ui.Label("store-price", card.Root, PriceText(item));
                card.Note = Ui.Label("muted app-note", card.Root);
                string id = item.Id;
                card.Buy = Ui.Button("BUY", () => Buy(id), "store-buy", card.Root, "buy-" + id);
                _cards.Add(card);
            }
        }

        public override void Refresh()
        {
            decimal balance = Context.Economy.Bank.Balance;
            Ui.SetText(_funds, $"Paid from your bank account · balance {Fmt.Money(balance)}");
            foreach (Card card in _cards)
            {
                bool owned = Context.Economy.Owns(card.Item.Id);
                decimal price = (decimal)card.Item.Price;
                bool lease = card.Item.Category == StoreCategory.Lease;
                Ui.SetText(card.Buy, owned ? (lease ? "LEASED" : "OWNED") : (lease ? "SIGN LEASE" : "BUY"));
                card.Buy.SetEnabled(!owned && balance >= price);
                card.Root.EnableInClassList("owned", owned);
                Ui.SetText(card.Note, owned || balance >= price ? "" : $"Need {Fmt.Money(price - balance)} more in the bank.");
            }
        }

        private void Buy(string itemId)
        {
            string error = Context.Economy.Buy(itemId, Context.Clock.Now);
            bool lease = error == null && Context.Economy.Catalog.Any(i => i.Id == itemId && i.Category == StoreCategory.Lease);
            Ui.SetText(_status, error ?? (lease ? "Lease signed. Your key card works at the building from now on."
                : "Purchased. It's already in your apartment."));
            _status.EnableInClassList("error", error != null);
            _status.EnableInClassList("ok", error == null);
            Refresh();
        }

        private static string PriceText(StoreItem item)
        {
            string price = Fmt.Money((decimal)item.Price);
            return item.MonthlyCost > 0 ? $"{price} + {Fmt.Money((decimal)item.MonthlyCost)}/mo" : price;
        }
    }
}
