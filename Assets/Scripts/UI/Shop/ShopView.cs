using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Home;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// An online store for Timberline Home or Circuit Stop, shared by the website (in the browser) and the phone app:
    /// the catalog with pictures and prices, a product page (colour, quantity), the cart, checkout (pickup or delivery
    /// to one of your homes, the move-in add-on, the estimated time) and your orders. Styled inline so it looks the
    /// same in both places; <c>compact</c> lays it out for the phone's narrow screen.
    /// </summary>
    public sealed class ShopView
    {
        private enum Page { Catalog, Product, Cart, Checkout, Orders, Placed }

        /// <summary>The stores' colours (their signs, trucks and crews wear them too).</summary>
        public static readonly Color FurnitureBrand = new Color(0.2f, 0.4f, 0.28f), TechBrand = new Color(0.15f, 0.35f, 0.7f);
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private static readonly Color Ink = new Color(0.1f, 0.11f, 0.13f), Muted = new Color(0.42f, 0.45f, 0.5f), Line = new Color(0.88f, 0.89f, 0.91f);
        private static readonly Color Paper = new Color(0.97f, 0.97f, 0.96f), Card = Color.white, Good = new Color(0.1f, 0.55f, 0.28f), Bad = new Color(0.78f, 0.2f, 0.15f);

        private readonly GameBootstrap _game;
        private readonly HomeStore _store;
        private readonly string _name;
        private readonly Color _brand;
        private readonly bool _compact;
        private readonly ScrollView _body;
        private readonly Label _cartLink, _status;

        private Page _page = Page.Catalog;
        private string _department = "";
        private HomeItem _product;
        private int _variant, _qty = 1;
        /// <summary>Checkout from "Buy now": this one line instead of the cart.</summary>
        private CartLine _buyNow;
        private Fulfilment _how = Fulfilment.Delivery;
        private string _destination = "";
        private bool _moveIn;
        /// <summary>Paying with the fund's company card (the goods are the company's).</summary>
        private bool _company;
        private ShopOrder _placed;
        private string _signature = "";
        /// <summary>Values kept fresh between rebuilds (the trucks' tracking lines).</summary>
        private readonly List<Action> _live = new List<Action>();

        public VisualElement Root { get; }

        public ShopView(GameBootstrap game, HomeStore store, string name, string tagline, Color brand, bool compact)
        {
            _game = game;
            _store = store;
            _name = name;
            _brand = brand;
            _compact = compact;
            Root = Box();
            Root.style.flexGrow = 1;
            Root.style.backgroundColor = Paper;

            // The store's bar: name, tagline, and the links to the shop, the cart and your orders.
            var bar = Box(Root);
            bar.style.backgroundColor = brand;
            Pad(bar, compact ? 14f : 22f, compact ? 10f : 14f);
            // The phone stacks the links under the name: side by side they run off its narrow screen.
            var top = compact ? Box(bar) : Row(bar);
            var titles = Box(top);
            Text(titles, name.ToUpperInvariant(), compact ? 17f : 22f, Color.white, true);
            if (!compact) Text(titles, tagline, 12f, new Color(1f, 1f, 1f, 0.8f));
            var links = Row(top, Justify.FlexStart);
            links.style.alignItems = Align.Center;
            if (compact) links.style.marginTop = 4f;
            Link(links, "Shop", () => Go(Page.Catalog));
            _cartLink = Link(links, "Cart", () => Go(Page.Cart));
            Link(links, "Orders", () => Go(Page.Orders));
            _status = Text(Root, "", 12f, Bad);
            _status.style.marginLeft = _status.style.marginRight = compact ? 14f : 22f;
            _status.style.whiteSpace = WhiteSpace.Normal;

            _body = new ScrollView(ScrollViewMode.Vertical);
            _body.style.flexGrow = 1;
            _body.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            Root.Add(_body);
            Build();
        }

        private HomeShop Shop => _game.Shop;
        private DateTime Now => _game.Clock.Now;

        /// <summary>Keeps the cart count and live order states fresh; rebuilds the page only when what it shows changed.</summary>
        public void Refresh()
        {
            _cartLink.text = $"Cart ({Shop.CartCount(_store)})";
            foreach (Action a in _live) a();
            string sig = _page + "|" + Shop.Version;
            if (sig != _signature) Build();
        }

        private void Go(Page page)
        {
            _page = page;
            _status.text = "";
            Build();
            _body.scrollOffset = Vector2.zero;
        }

        private void Build()
        {
            _signature = _page + "|" + Shop.Version;
            _cartLink.text = $"Cart ({Shop.CartCount(_store)})";
            VisualElement c = _body.contentContainer;
            c.Clear();
            _live.Clear();
            var page = Box(c);
            Pad(page, _compact ? 12f : 22f, _compact ? 10f : 16f);
            switch (_page)
            {
                case Page.Catalog: Catalog(page); break;
                case Page.Product: Product(page); break;
                case Page.Cart: CartPage(page); break;
                case Page.Checkout: Checkout(page); break;
                case Page.Orders: Orders(page); break;
                case Page.Placed: Placed(page); break;
            }
        }

        // ------------------------------------------------------------------ catalog

        private List<HomeItem> Items()
        {
            var list = new List<HomeItem>();
            foreach (HomeItem i in HomeCatalog.Items)
                if (i.Store == _store && !i.IsBox) list.Add(i);
            return list;
        }

        private void Catalog(VisualElement page)
        {
            // Departments as chips.
            var chips = Row(page, Justify.FlexStart);
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.marginBottom = 10f;
            var departments = new List<string> { "" };
            foreach (HomeItem i in Items()) if (!departments.Contains(i.Department)) departments.Add(i.Department);
            foreach (string d in departments)
            {
                string dept = d;
                Chip(chips, d.Length == 0 ? "All" : d, _department == d, () => { _department = dept; Build(); });
            }

            var grid = Row(page, Justify.FlexStart);
            grid.style.flexWrap = Wrap.Wrap;
            foreach (HomeItem item in Items())
            {
                if (_department.Length > 0 && item.Department != _department) continue;
                HomeItem it = item;
                // Phone: one full-width row per product, the picture beside the details. Wider: a grid of cards.
                var card = _compact ? Row(grid, Justify.FlexStart) : Box(grid);
                card.style.width = _compact ? Length.Percent(100) : 208f;
                card.style.marginRight = _compact ? 0f : 12f;
                card.style.marginBottom = _compact ? 8f : 12f;
                card.style.backgroundColor = Card;
                Round(card, 10f);
                Border(card, Line);
                card.style.overflow = Overflow.Hidden;
                VisualElement pic = Preview(card, it, 0, _compact ? 96f : 140f);
                pic.RegisterCallback<ClickEvent>(_ => Open(it));
                if (_compact) pic.style.width = 96f;
                var info = Box(card);
                info.style.flexGrow = 1;
                info.style.flexShrink = 1;
                info.style.minWidth = 0f;
                Pad(info, 10f, 8f);
                var title = Text(info, it.Name, 14f, Ink, true);
                title.style.whiteSpace = WhiteSpace.Normal;
                title.RegisterCallback<ClickEvent>(_ => Open(it));
                Text(info, $"{it.Department} · {it.Tier}{(it.Brand != null ? " · " + it.Brand : "")}", 11f, Muted).style.whiteSpace = WhiteSpace.Normal;
                Text(info, Money(it.Price), 16f, Ink, true).style.marginTop = 4f;
                var buttons = Row(info, Justify.FlexStart);
                buttons.style.marginTop = 6f;
                buttons.style.flexWrap = Wrap.Wrap;
                Button(buttons, "Add to cart", false, () => { Shop.Add(_store, it.Id, 0); Say($"Added {it.Name} to your cart.", false); });
                Button(buttons, "Buy now", true, () => BuyNow(it, 0, 1));
            }
        }

        private void Open(HomeItem item)
        {
            _product = item;
            _variant = 0;
            _qty = 1;
            Go(Page.Product);
        }

        // ------------------------------------------------------------------ product

        private void Product(VisualElement page)
        {
            if (_product == null) { Go(Page.Catalog); return; }
            HomeItem it = _product;
            Link(page, "‹ Back to the shop", () => Go(Page.Catalog), Muted);
            var layout = _compact ? Box(page) : Row(page, Justify.FlexStart);
            layout.style.marginTop = 8f;
            var pic = Box(layout);
            pic.style.width = _compact ? Length.Percent(100) : 380f;
            pic.style.backgroundColor = Card;
            Round(pic, 12f);
            Border(pic, Line);
            pic.style.overflow = Overflow.Hidden;
            Preview(pic, it, _variant, _compact ? 220f : 300f);

            var info = Box(layout);
            info.style.flexGrow = 1;
            info.style.marginLeft = _compact ? 0f : 24f;
            info.style.marginTop = _compact ? 12f : 0f;
            Text(info, it.Name, _compact ? 20f : 26f, Ink, true);
            Text(info, $"{it.Department} · {it.Tier}{(it.Brand != null ? " · by " + it.Brand : "")}", 13f, Muted);
            Text(info, Money(it.Price), 24f, Ink, true).style.marginTop = 8f;
            var spec = Text(info, Describe(it), 13f, Ink);
            spec.style.whiteSpace = WhiteSpace.Normal;
            spec.style.marginTop = 8f;
            Text(info, it.Boxed ? "Ships in its box. Unbox it where it goes." : "Ships assembled.", 12f, Muted).style.marginTop = 4f;

            if (it.Variants.Length > 1)
            {
                Text(info, "Colour: " + it.Variants[_variant], 13f, Ink, true).style.marginTop = 12f;
                var colours = Row(info, Justify.FlexStart);
                colours.style.flexWrap = Wrap.Wrap;
                for (int v = 0; v < it.Variants.Length; v++)
                {
                    int variant = v;
                    Chip(colours, it.Variants[v], v == _variant, () => { _variant = variant; Build(); });
                }
            }

            var qty = Row(info, Justify.FlexStart);
            qty.style.alignItems = Align.Center;
            qty.style.marginTop = 12f;
            Text(qty, "Quantity", 13f, Ink, true).style.marginRight = 10f;
            Stepper(qty, _qty, n => { _qty = Math.Max(1, Math.Min(HomeShop.MaxQty, n)); Build(); });

            var buttons = Row(info, Justify.FlexStart);
            buttons.style.marginTop = 14f;
            Button(buttons, "Add to cart", false, () => { Shop.Add(_store, it.Id, _variant, _qty); Say($"Added {_qty} × {it.Name} to your cart.", false); }, big: true);
            Button(buttons, "Buy now", true, () => BuyNow(it, _variant, _qty), big: true);
            Text(info, $"Delivery {Money(HomeShop.DeliveryFee)}, usually 30 to 60 minutes · free pickup in {HomeShop.PickupMinutes} minutes", 12f, Muted).style.marginTop = 10f;
        }

        private static string Describe(HomeItem it)
        {
            string size = string.Format(C, "{0:0.0#} × {1:0.0#} × {2:0.0#} m", it.Width, it.Depth, it.Height);
            string extra = it.IsDesk ? $". Takes {it.MonitorSlots} monitors standing; arms add more, six a desk."
                : it.IsArm ? $". Holds {it.Arms} monitor{(it.Arms == 1 ? "" : "s")}; clamps to the back of a desk."
                : it.IsMonitor ? $". {it.Inches}\" screen: charts, watchlists, positions, news."
                : it.IsLaptop ? ". Computer and screen in one: charts, watchlists, positions, news."
                : it.IsKit ? $". A whole trading desk, set up: {it.KitScreens} × 27\" monitor{(it.KitScreens == 1 ? "" : "s")} on a {it.KitScreens}-screen arm, a tower PC, keyboard, mouse and speaker. Ships in one crate and unpacks assembled wherever you set it down."
                : it.Seat ? ". Seats one." : "";
            return $"{size}{(it.Outdoor ? ", indoor or outdoor" : "")}{extra}";
        }

        // ------------------------------------------------------------------ cart

        private void CartPage(VisualElement page)
        {
            List<CartLine> cart = Shop.Cart(_store);
            Text(page, "Your cart", _compact ? 20f : 24f, Ink, true).style.marginBottom = 8f;
            if (cart.Count == 0)
            {
                Text(page, "Your cart is empty.", 14f, Muted);
                Button(page, "Start shopping", true, () => Go(Page.Catalog)).style.alignSelf = Align.FlexStart;
                return;
            }
            foreach (CartLine line in cart.ToArray())
            {
                CartLine l = line;
                HomeItem it = l.Item;
                if (it == null) continue;
                var row = Row(page, Justify.FlexStart);
                row.style.alignItems = Align.Center;
                row.style.backgroundColor = Card;
                Round(row, 10f);
                Border(row, Line);
                row.style.marginBottom = 8f;
                row.style.overflow = Overflow.Hidden;
                var thumb = Preview(row, it, l.Variant, 64f);
                thumb.style.width = 84f;
                var text = Box(row);
                text.style.flexGrow = 1;
                text.style.flexShrink = 1;
                Pad(text, 10f, 6f);
                Text(text, it.Name, 14f, Ink, true);
                Text(text, (it.Variants.Length > 1 ? it.Variants[l.Variant] + " · " : "") + Money(it.Price) + " each", 12f, Muted);
                var controls = Row(text, Justify.FlexStart);
                controls.style.alignItems = Align.Center;
                controls.style.marginTop = 4f;
                controls.style.flexWrap = Wrap.Wrap;
                Stepper(controls, l.Qty, n => Shop.SetQty(_store, l, n));
                var remove = Link(controls, "Remove", () => Shop.SetQty(_store, l, 0), Bad);
                remove.style.marginLeft = 10f;
                remove.style.marginTop = 4f;
                remove.style.whiteSpace = WhiteSpace.NoWrap;
                remove.style.flexShrink = 0;
                // The line total: under the details on the phone (no room for a column), at the right end otherwise.
                var total = Text(_compact ? text : row, Money(l.Total), 15f, Ink, true);
                total.style.whiteSpace = WhiteSpace.NoWrap;
                total.style.flexShrink = 0;
                if (_compact) total.style.marginTop = 4f;
                else total.style.marginRight = 12f;
            }
            ShopQuote q = HomeShop.Quote(cart, Fulfilment.Pickup, false, Now);
            var sum = Row(page);
            sum.style.marginTop = 6f;
            Text(sum, $"Subtotal ({q.Items} item{(q.Items == 1 ? "" : "s")})", 15f, Ink, true);
            Text(sum, Money(q.Goods), 18f, Ink, true);
            var buttons = Row(page, Justify.FlexStart);
            buttons.style.marginTop = 10f;
            Button(buttons, "Checkout", true, () => { _buyNow = null; Go(Page.Checkout); }, big: true);
            Button(buttons, "Keep shopping", false, () => Go(Page.Catalog), big: true);
        }

        private void BuyNow(HomeItem item, int variant, int qty)
        {
            _buyNow = new CartLine { ItemId = item.Id, Variant = variant, Qty = qty };
            Go(Page.Checkout);
        }

        // ------------------------------------------------------------------ checkout

        private IReadOnlyList<CartLine> Lines => _buyNow != null ? new List<CartLine> { _buyNow } : Shop.Cart(_store);

        private void Checkout(VisualElement page)
        {
            IReadOnlyList<CartLine> lines = Lines;
            if (lines.Count == 0) { Go(Page.Cart); return; }
            Link(page, _buyNow != null ? "‹ Back to the shop" : "‹ Back to your cart", () => Go(_buyNow != null ? Page.Catalog : Page.Cart), Muted);
            Text(page, "Checkout", _compact ? 20f : 24f, Ink, true).style.marginBottom = 6f;
            foreach (CartLine l in lines)
                if (l.Item != null) Text(page, $"{l.Qty} × {l.Item.Name}{(l.Item.Variants.Length > 1 ? " (" + l.Item.Variants[l.Variant] + ")" : "")} · {Money(l.Total)}", 13f, Ink);

            Section(page, "How would you like it?");
            var ways = _compact ? Box(page) : Row(page, Justify.FlexStart);
            Option(ways, _how == Fulfilment.Pickup, "Pick up · free",
                $"Ready in about {HomeShop.PickupMinutes} minutes, set out in the pickup bays behind the stores off Grove St. Bring a vehicle (or borrow Timberline's truck).",
                () => { _how = Fulfilment.Pickup; Build(); });
            Option(ways, _how == Fulfilment.Delivery, $"Delivery · {Money(HomeShop.DeliveryFee)}",
                "Our truck brings it to one of your homes and leaves it at your door.", () => { _how = Fulfilment.Delivery; Build(); });

            if (_how == Fulfilment.Delivery)
            {
                Section(page, "Deliver to");
                IReadOnlyList<(string Id, string Name)> homes = _game.ShopDestinations?.Invoke() ?? Array.Empty<(string, string)>();
                if (homes.Count == 0) Text(page, "No homes to deliver to.", 13f, Bad);
                bool known = false;
                foreach (var h in homes) if (h.Id == _destination) known = true;
                if (!known) _destination = homes.Count > 0 ? homes[0].Id : "";
                var list = Row(page, Justify.FlexStart);
                list.style.flexWrap = Wrap.Wrap;
                foreach (var h in homes)
                {
                    string id = h.Id;
                    Chip(list, h.Name, id == _destination, () => { _destination = id; Build(); });
                }

                Section(page, "Add-ons");
                bool furniture = _store == HomeStore.Furniture;
                int count = 0;
                foreach (CartLine l in lines) count += l.Qty;
                Option(page, _moveIn, $"Move-in service · +{Money(HomeShop.ServiceFee(count))}",
                    furniture
                        ? "The crew packs it in a moving box and carries it in for you: inside the front door, or at Harborview up the lift and set down in front of it on your floor."
                        : "The crew loads it on a cart and brings it in for you: inside the front door, or at Harborview up the lift on the building's cart, left by the lift on your floor.",
                    () => { _moveIn = !_moveIn; Build(); }, checkbox: true);
            }

            ShopQuote q = HomeShop.Quote(lines, _how, _moveIn, Now);
            Section(page, "Summary");
            Sum(page, "Items", Money(q.Goods));
            if (_how == Fulfilment.Delivery) Sum(page, "Delivery", Money(q.Delivery));
            if (q.Service > 0m) Sum(page, "Move-in service", Money(q.Service));
            Sum(page, "Total", Money(q.Total), bold: true);
            int minutes = (int)Math.Round((q.Due - Now).TotalMinutes);
            var eta = Text(page, _how == Fulfilment.Pickup
                ? $"Ready for pickup around {q.Due.ToString("h:mm tt", C)} (about {minutes} min)."
                : $"Estimated delivery {q.Due.ToString("h:mm tt", C)} (about {minutes} min).", 14f, Good, true);
            eta.style.marginTop = 6f;
            // Pay with: your bank, or the fund's company card once there's a fund (its goods are the company's).
            var fund = _game.Fund;
            bool company = fund != null && fund.Exists;
            if (!company) _company = false;
            if (company)
            {
                Section(page, "Pay with");
                var pay = Row(page, Justify.FlexStart);
                pay.style.flexWrap = Wrap.Wrap;
                Chip(pay, $"Your bank · {Money(_game.Economy.Bank.Balance)}", !_company, () => { _company = false; Build(); });
                Chip(pay, $"{fund.Name} card · {Money(fund.Ledger.Cash)}", _company, () => { _company = true; Build(); });
            }
            else Text(page, $"Paid from your bank account ({Money(_game.Economy.Bank.Balance)} available).", 12f, Muted);
            Button(page, $"Place order · {Money(q.Total)}", true, Place, big: true).style.alignSelf = Align.FlexStart;
        }

        private void Place()
        {
            var homes = _game.ShopDestinations?.Invoke() ?? Array.Empty<(string, string)>();
            string name = "";
            foreach (var h in homes) if (h.Id == _destination) name = h.Name;
            var lines = new List<CartLine>(Lines);
            var (order, error) = Shop.Place(_store, lines, _how, _destination, name, _moveIn, Now, _game.Belongings,
                (amount, what) => _company ? _game.Fund.BuyEquipment(amount, what) : _game.Economy.Spend(amount, what, Now),
                owner: _company ? "fund" : "");
            if (error != null) { Say(error, true); return; }
            if (_buyNow == null) Shop.ClearCart(_store);
            _buyNow = null;
            _placed = order;
            Go(Page.Placed);
        }

        private void Placed(VisualElement page)
        {
            if (_placed == null) { Go(Page.Orders); return; }
            Text(page, "Thanks! Your order is in.", _compact ? 20f : 24f, Good, true);
            Text(page, $"Order #{_placed.Id} · {Money(_placed.Total)}", 14f, Ink, true).style.marginTop = 6f;
            var what = Text(page, _placed.Summary, 13f, Ink);
            what.style.whiteSpace = WhiteSpace.Normal;
            var state = Text(page, StatusText(_placed), 14f, Good, true);
            state.style.marginTop = 8f;
            state.style.whiteSpace = WhiteSpace.Normal;
            var buttons = Row(page, Justify.FlexStart);
            buttons.style.marginTop = 12f;
            Button(buttons, "Track your orders", true, () => Go(Page.Orders), big: true);
            Button(buttons, "Keep shopping", false, () => Go(Page.Catalog), big: true);
        }

        // ------------------------------------------------------------------ orders

        private void Orders(VisualElement page)
        {
            Text(page, "Your orders", _compact ? 20f : 24f, Ink, true).style.marginBottom = 8f;
            bool any = false;
            for (int k = Shop.Orders.Count - 1; k >= 0; k--)
            {
                ShopOrder o = Shop.Orders[k];
                if (o.StoreKind != _store) continue;
                any = true;
                var card = Box(page);
                card.style.backgroundColor = Card;
                Round(card, 10f);
                Border(card, Line);
                Pad(card, 12f, 10f);
                card.style.marginBottom = 8f;
                var head = Row(card);
                Text(head, $"Order #{o.Id} · {new DateTime(o.Placed).ToString("ddd h:mm tt", C)}", 13f, Muted, true);
                Text(head, Money(o.Total), 14f, Ink, true);
                var what = Text(card, o.Summary, 14f, Ink);
                what.style.whiteSpace = WhiteSpace.Normal;
                what.style.marginTop = 2f;
                string how = o.How == Fulfilment.Pickup ? "Pickup" : $"Delivery to {o.DestinationName}{(o.MoveIn ? " · move-in service" : "")}";
                Text(card, how, 12f, Muted).style.marginTop = 2f;
                var state = Text(card, StatusText(o), 13f, o.State == ShopOrderStatus.Delivered || o.State == ShopOrderStatus.Ready ? Good : _brand, true);
                state.style.marginTop = 4f;
                state.style.whiteSpace = WhiteSpace.Normal;
                if (o.How == Fulfilment.Delivery && (o.State == ShopOrderStatus.OnTheWay || o.State == ShopOrderStatus.Unloading))
                {
                    // Where the truck is now, and a way to watch it on the map.
                    int id = o.Id;
                    var where = Text(card, "", 12f, Muted);
                    where.style.marginTop = 2f;
                    _live.Add(() => where.text = _game.OrderTracking?.Invoke(id) ?? "");
                    _live[_live.Count - 1]();
                    if (_game.TrackOrder != null) Button(card, "Track on map", true, () => _game.TrackOrder(id)).style.alignSelf = Align.FlexStart;
                }
            }
            if (!any) Text(page, "No orders yet.", 14f, Muted);
        }

        /// <summary>Where an order is, in words, with its estimated time.</summary>
        public static string StatusText(ShopOrder o)
        {
            var due = new DateTime(o.Due);
            string at = due.ToString("h:mm tt", C);
            return o.State switch
            {
                ShopOrderStatus.Preparing when o.How == Fulfilment.Pickup => $"Preparing · ready for pickup around {at}",
                ShopOrderStatus.Preparing => $"Preparing your order · estimated delivery {at}",
                ShopOrderStatus.Ready => "Ready for pickup in the bays behind the stores (off Grove St)",
                ShopOrderStatus.OnTheWay => $"Out for delivery · arriving around {at}",
                ShopOrderStatus.Unloading => o.MoveIn ? $"At {o.DestinationName}: the crew's bringing it in" : $"At {o.DestinationName}: unloading at your door",
                _ => o.MoveIn ? $"Delivered and moved in at {o.DestinationName}" : $"Delivered to {o.DestinationName}",
            };
        }

        // ------------------------------------------------------------------ building blocks

        private VisualElement Preview(VisualElement parent, HomeItem item, int variant, float height)
        {
            var frame = Box(parent);
            frame.style.height = height;
            frame.style.backgroundColor = new Color(0.93f, 0.93f, 0.92f);
            frame.style.justifyContent = Justify.Center;
            frame.style.alignItems = Align.Center;
            var img = new Image { scaleMode = ScaleMode.ScaleToFit };
            img.style.width = img.style.height = Length.Percent(100);
            frame.Add(img);
            _game.ProductPreview?.Invoke(item.Id, variant, tex => img.image = tex);
            return frame;
        }

        private void Say(string text, bool bad)
        {
            _status.text = text;
            _status.style.color = bad ? Bad : Good;
            _status.style.marginTop = 6f;
            _cartLink.text = $"Cart ({Shop.CartCount(_store)})";
        }

        private static string Money(decimal d) => "$" + d.ToString("N2", C);

        private static VisualElement Box(VisualElement parent = null)
        {
            var e = new VisualElement();
            parent?.Add(e);
            return e;
        }

        private static VisualElement Row(VisualElement parent, Justify justify = Justify.SpaceBetween)
        {
            var e = Box(parent);
            e.style.flexDirection = FlexDirection.Row;
            e.style.justifyContent = justify;
            return e;
        }

        private static Label Text(VisualElement parent, string text, float size, Color color, bool bold = false)
        {
            var l = new Label(text);
            // Wrap rather than run off a narrow screen; rows keep their own widths.
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.flexShrink = 1;
            l.style.fontSize = size;
            l.style.color = color;
            l.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0f;
            l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0f;
            parent.Add(l);
            return l;
        }

        private static void Pad(VisualElement e, float x, float y)
        {
            e.style.paddingLeft = e.style.paddingRight = x;
            e.style.paddingTop = e.style.paddingBottom = y;
        }

        private static void Round(VisualElement e, float r) =>
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;

        private static void Border(VisualElement e, Color c, float w = 1f)
        {
            e.style.borderLeftWidth = e.style.borderRightWidth = e.style.borderTopWidth = e.style.borderBottomWidth = w;
            e.style.borderLeftColor = e.style.borderRightColor = e.style.borderTopColor = e.style.borderBottomColor = c;
        }

        private Button Button(VisualElement parent, string text, bool primary, Action click, bool big = false)
        {
            var b = new Button(click) { text = text };
            b.style.flexShrink = 0;
            b.style.whiteSpace = WhiteSpace.NoWrap;
            b.style.backgroundColor = primary ? _brand : Card;
            b.style.color = primary ? Color.white : Ink;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.fontSize = big ? 14f : 12f;
            Border(b, primary ? _brand : Line);
            Round(b, 8f);
            Pad(b, big ? 16f : 9f, big ? 8f : 5f);
            b.style.marginLeft = 0f;
            b.style.marginRight = 6f;
            b.style.marginTop = 4f;
            parent.Add(b);
            return b;
        }

        private Label Link(VisualElement parent, string text, Action click, Color? color = null)
        {
            var l = Text(parent, text, 13f, color ?? Color.white, true);
            l.style.marginLeft = 14f;
            if (color != null) l.style.marginLeft = 0f;
            l.RegisterCallback<ClickEvent>(_ => click());
            return l;
        }

        private void Chip(VisualElement parent, string text, bool on, Action click)
        {
            var b = Button(parent, text, on, click);
            Round(b, 14f);
        }

        private void Stepper(VisualElement parent, int value, Action<int> set)
        {
            var row = Row(parent, Justify.FlexStart);
            row.style.alignItems = Align.Center;
            Button(row, "−", false, () => set(value - 1));
            row.style.flexShrink = 0;
            var n = Text(row, value.ToString(C), 14f, Ink, true);
            n.style.flexShrink = 0;
            n.style.minWidth = 24f;
            n.style.unityTextAlign = TextAnchor.MiddleCenter;
            Button(row, "+", false, () => set(value + 1));
        }

        private void Option(VisualElement parent, bool on, string title, string detail, Action click, bool checkbox = false)
        {
            var card = Box(parent);
            card.style.flexGrow = 1;
            card.style.flexBasis = 0f;
            card.style.backgroundColor = on ? new Color(_brand.r, _brand.g, _brand.b, 0.08f) : Card;
            Round(card, 10f);
            Border(card, on ? _brand : Line, on ? 2f : 1f);
            Pad(card, 12f, 10f);
            card.style.marginRight = 8f;
            card.style.marginBottom = 8f;
            if (_compact) card.style.flexBasis = StyleKeyword.Auto;
            Text(card, (checkbox ? (on ? "☑ " : "☐ ") : (on ? "◉ " : "○ ")) + title, 14f, Ink, true);
            var d = Text(card, detail, 12f, Muted);
            d.style.whiteSpace = WhiteSpace.Normal;
            d.style.marginTop = 3f;
            card.RegisterCallback<ClickEvent>(_ => click());
        }

        private static void Section(VisualElement parent, string title)
        {
            var t = Text(parent, title, 15f, Ink, true);
            t.style.marginTop = 14f;
            t.style.marginBottom = 6f;
        }

        private static void Sum(VisualElement parent, string label, string value, bool bold = false)
        {
            var row = Row(parent);
            row.style.maxWidth = 420f;
            Text(row, label, bold ? 15f : 13f, Ink, bold);
            Text(row, value, bold ? 15f : 13f, Ink, bold);
        }
    }
}
