using System;
using System.Collections.Generic;

namespace OpeningBell.Home
{
    /// <summary>How an online order reaches you.</summary>
    public enum Fulfilment { Pickup, Delivery }

    /// <summary>
    /// Where an order is. Pickup: Preparing, then Ready (set out in the bays behind the stores). Delivery: Preparing,
    /// OnTheWay (the truck's driving over), Unloading (parked at your door, the crew at work), Delivered.
    /// </summary>
    public enum ShopOrderStatus { Preparing, Ready, OnTheWay, Unloading, Delivered }

    [Serializable]
    public sealed class CartLine
    {
        public string ItemId = "";
        public int Variant;
        public int Qty = 1;

        public HomeItem Item => HomeCatalog.Find(ItemId);
        public decimal Total => (Item?.Price ?? 0m) * Qty;
    }

    [Serializable]
    public sealed class ShopOrder
    {
        public int Id;
        /// <summary>Which store (<see cref="HomeStore"/>): each store's orders are its own.</summary>
        public int Store;
        public int Fulfilment;
        public long Placed;
        /// <summary>Pickup: when it's set out in the bays. Delivery: when the truck pulls up.</summary>
        public long Due;
        /// <summary>Delivery: the home it goes to (id and name when ordered).</summary>
        public string Destination = "", DestinationName = "";
        /// <summary>The move-in service: the crew brings it inside (furniture boxed, tech on a cart).</summary>
        public bool MoveIn;
        public decimal Goods, DeliveryFee, ServiceFee;
        public int Status;
        /// <summary>The belongings it made (uids), and what's in it for the order list.</summary>
        public List<int> Items = new List<int>();
        public string Summary = "";

        public HomeStore StoreKind => (HomeStore)Store;
        public Fulfilment How => (Fulfilment)Fulfilment;
        public ShopOrderStatus State => (ShopOrderStatus)Status;
        public decimal Total => Goods + DeliveryFee + ServiceFee;
    }

    [Serializable]
    public sealed class ShopSaveData
    {
        public int NextId = 1;
        public List<ShopOrder> Orders = new List<ShopOrder>();
        public List<CartLine> FurnitureCart = new List<CartLine>();
        public List<CartLine> TechCart = new List<CartLine>();
    }

    /// <summary>What an order would cost and when it would come.</summary>
    public readonly struct ShopQuote
    {
        public readonly decimal Goods, Delivery, Service;
        public readonly int Items;
        public readonly DateTime Due;

        public ShopQuote(decimal goods, decimal delivery, decimal service, int items, DateTime due)
        {
            Goods = goods;
            Delivery = delivery;
            Service = service;
            Items = items;
            Due = due;
        }

        public decimal Total => Goods + Delivery + Service;
    }

    /// <summary>
    /// Timberline Home's and Circuit Stop's online stores (the websites and the phone apps): a cart per store, checkout
    /// with pickup or delivery, the move-in add-on, and the orders you've placed. Paying and making the goods happen
    /// here; the world (City) sets pickups out in the bays and runs the delivery trucks from the orders' times.
    /// </summary>
    public sealed class HomeShop
    {
        /// <summary>The truck to your door; the same as booking it at the store's delivery desk.</summary>
        public const decimal DeliveryFee = 79m;
        /// <summary>Move-in: a call-out charge and so much a piece (the crew boxes furniture, carts tech, carries it up).</summary>
        public const decimal ServiceBase = 60m, ServicePerItem = 15m;
        /// <summary>Game minutes to pick and set out an order for collection.</summary>
        public const int PickupMinutes = 20;
        public const int MaxQty = 20;

        private ShopSaveData _s = new ShopSaveData();

        public IReadOnlyList<ShopOrder> Orders => _s.Orders;
        public int Version { get; private set; }

        public List<CartLine> Cart(HomeStore store) => store == HomeStore.Tech ? _s.TechCart : _s.FurnitureCart;

        public int CartCount(HomeStore store)
        {
            int n = 0;
            foreach (CartLine l in Cart(store)) n += l.Qty;
            return n;
        }

        /// <summary>Adds to the cart (merging with the same item in the same colour).</summary>
        public void Add(HomeStore store, string itemId, int variant, int qty = 1)
        {
            HomeItem item = HomeCatalog.Find(itemId);
            if (item == null || item.Store != store || item.IsBox || qty <= 0) return;
            List<CartLine> cart = Cart(store);
            CartLine line = cart.Find(l => l.ItemId == itemId && l.Variant == variant);
            if (line == null) cart.Add(line = new CartLine { ItemId = itemId, Variant = variant, Qty = 0 });
            line.Qty = Math.Min(MaxQty, line.Qty + qty);
            Version++;
        }

        public void SetQty(HomeStore store, CartLine line, int qty)
        {
            if (qty <= 0) Cart(store).Remove(line);
            else line.Qty = Math.Min(MaxQty, qty);
            Version++;
        }

        public void ClearCart(HomeStore store)
        {
            Cart(store).Clear();
            Version++;
        }

        public static decimal ServiceFee(int items) => ServiceBase + ServicePerItem * items;

        /// <summary>
        /// Game minutes from ordering to the truck at your door: half an hour for one thing, a few minutes more a piece
        /// to load, ten for the crew to pack for a move-in, never more than an hour.
        /// </summary>
        public static int DeliveryMinutes(int items, bool moveIn) => Math.Max(30, Math.Min(60, 30 + 3 * (items - 1) + (moveIn ? 10 : 0)));

        public static ShopQuote Quote(IReadOnlyList<CartLine> lines, Fulfilment how, bool moveIn, DateTime now)
        {
            decimal goods = 0m;
            int items = 0;
            foreach (CartLine l in lines)
            {
                goods += l.Total;
                items += l.Qty;
            }
            bool delivery = how == Fulfilment.Delivery;
            decimal fee = delivery ? DeliveryFee : 0m, service = delivery && moveIn ? ServiceFee(items) : 0m;
            DateTime due = now.AddMinutes(delivery ? DeliveryMinutes(items, moveIn) : PickupMinutes);
            return new ShopQuote(goods, fee, service, items, due);
        }

        /// <summary>
        /// Places an order: pays through <paramref name="pay"/> (amount, description → error or null), makes the goods
        /// (waiting at the store for pickup, or on the truck for delivery) and returns it, or an error. Furniture picked
        /// up comes with a moving box, like at the till.
        /// </summary>
        public (ShopOrder Order, string Error) Place(HomeStore store, IReadOnlyList<CartLine> lines, Fulfilment how, string destination,
            string destinationName, bool moveIn, DateTime now, Belongings belongings, Func<decimal, string, string> pay)
        {
            if (lines == null || lines.Count == 0) return (null, "Your cart is empty.");
            if (how == Fulfilment.Delivery && string.IsNullOrEmpty(destination)) return (null, "Choose where it's going.");
            ShopQuote q = Quote(lines, how, moveIn && how == Fulfilment.Delivery, now);
            string storeName = store == HomeStore.Tech ? "Circuit Stop" : "Timberline Home";
            string error = pay(q.Total, $"{storeName} online order");
            if (error != null) return (null, error);

            var order = new ShopOrder
            {
                Id = _s.NextId++, Store = (int)store, Fulfilment = (int)how, Placed = now.Ticks, Due = q.Due.Ticks,
                Destination = how == Fulfilment.Delivery ? destination : "", DestinationName = how == Fulfilment.Delivery ? destinationName : "",
                MoveIn = moveIn && how == Fulfilment.Delivery, Goods = q.Goods, DeliveryFee = q.Delivery, ServiceFee = q.Service,
                Status = (int)ShopOrderStatus.Preparing,
            };
            var names = new List<string>();
            foreach (CartLine l in lines)
            {
                HomeItem item = l.Item;
                if (item == null) continue;
                names.Add(l.Qty > 1 ? $"{l.Qty} × {item.Name}" : item.Name);
                for (int n = 0; n < l.Qty; n++)
                {
                    OwnedItem owned = belongings.Add(item.Id, l.Variant, how == Fulfilment.Pickup ? ItemState.AtPickup : ItemState.Delivering);
                    owned.Order = order.Id;
                    owned.DeliverAt = order.Due;
                    owned.Property = order.Destination;
                    order.Items.Add(owned.Uid);
                }
            }
            if (how == Fulfilment.Pickup && store == HomeStore.Furniture)
            {
                OwnedItem box = belongings.Add("moving_box", 0, ItemState.AtPickup);
                box.Order = order.Id;
                box.DeliverAt = order.Due;
                order.Items.Add(box.Uid);
            }
            order.Summary = string.Join(", ", names);
            _s.Orders.Add(order);
            belongings.Touch();
            Version++;
            return (order, null);
        }

        public void SetStatus(ShopOrder order, ShopOrderStatus status)
        {
            if (order.Status == (int)status) return;
            order.Status = (int)status;
            Version++;
        }

        public ShopOrder Find(int id) => _s.Orders.Find(o => o.Id == id);

        public ShopSaveData CaptureState() => _s;

        public void RestoreState(ShopSaveData data)
        {
            _s = data ?? new ShopSaveData();
            _s.Orders ??= new List<ShopOrder>();
            _s.FurnitureCart ??= new List<CartLine>();
            _s.TechCart ??= new List<CartLine>();
            Version++;
        }
    }
}
