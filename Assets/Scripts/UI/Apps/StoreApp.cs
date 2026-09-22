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
        private readonly List<(OpeningBell.Vehicles.UsedListing Listing, Button Buy, VisualElement Root, Label Note)> _used =
            new List<(OpeningBell.Vehicles.UsedListing, Button, VisualElement, Label)>();
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

            // Classifieds: used cars from private sellers, delivered to the curb outside home.
            OpeningBell.Vehicles.Fleet fleet = context.Game.Vehicles;
            if (fleet == null || fleet.Catalog.Listings.Count == 0) return;
            Ui.Label("panel-title store-section", Root, "USED CARS · PRIVATE SELLERS");
            var used = Ui.Box("store-grid", Root);
            foreach (OpeningBell.Vehicles.UsedListing listing in fleet.Catalog.Listings)
            {
                if (!fleet.Catalog.TryGetModel(listing.ModelId, out OpeningBell.Vehicles.VehicleModel model)) continue;
                VisualElement card = Ui.Box("app-card store-card", used);
                Ui.Label("store-name", card, model.Name);
                Ui.Label("muted store-category", card, $"USED · {listing.OdometerKm:N0} KM · CONDITION {listing.Condition:P0} · {listing.Seller.ToUpperInvariant()}");
                Ui.Label("store-description", card, listing.Description);
                Ui.Label("store-price", card, Fmt.Money((decimal)listing.Price));
                Label note = Ui.Label("muted app-note", card);
                OpeningBell.Vehicles.UsedListing chosen = listing;
                Button buy = Ui.Button("BUY", () => BuyUsed(chosen), "store-buy", card, "buy-" + listing.Id);
                _used.Add((listing, buy, card, note));
            }
        }

        private void BuyUsed(OpeningBell.Vehicles.UsedListing listing)
        {
            string error = Context.BuyUsedCar == null ? "Cars can't be delivered here." : Context.BuyUsedCar(listing);
            Ui.SetText(_status, error ?? "Bought. The seller left it at the curb on Maple St, outside your building. Keys are in the mailbox.");
            _status.EnableInClassList("error", error != null);
            _status.EnableInClassList("ok", error == null);
            Refresh();
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
            foreach (var (listing, buy, root, note) in _used)
            {
                bool sold = Context.Game.Vehicles.IsSold(listing.Id);
                decimal price = (decimal)listing.Price;
                Ui.SetText(buy, sold ? "SOLD" : "BUY");
                buy.SetEnabled(!sold && balance >= price);
                root.EnableInClassList("owned", sold);
                Ui.SetText(note, sold || balance >= price ? "" : $"Need {Fmt.Money(price - balance)} more in the bank.");
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
