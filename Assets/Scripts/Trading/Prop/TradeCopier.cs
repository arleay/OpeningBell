using System;
using System.Collections.Generic;

namespace OpeningBell.Trading
{
    /// <summary>An account that copies the leader: a label for messages and its order manager.</summary>
    public readonly struct CopyTarget
    {
        public readonly string Label;
        public readonly OrderManager Orders;

        public CopyTarget(string label, OrderManager orders)
        {
            Label = label;
            Orders = orders;
        }
    }

    /// <summary>
    /// Group trading (PROP_SPEC §3): what the trader does on the leader account is repeated 1:1 on every follower.
    /// New orders are copied only when the leader's was accepted; cancels and price moves follow the link from each
    /// leader order to its copies. Each follower's own rules still apply, and refusals are reported in
    /// <see cref="LastNote"/>.
    /// </summary>
    public sealed class TradeCopier
    {
        private readonly Dictionary<Order, List<(CopyTarget target, Order copy)>> _links =
            new Dictionary<Order, List<(CopyTarget, Order)>>();

        /// <summary>What happened to the copies of the last action ("Copied to 2 accounts.", refusals).</summary>
        public string LastNote { get; private set; } = "";

        public Order Place(OrderManager leader, IReadOnlyList<CopyTarget> followers, Func<OrderManager, Order> place) =>
            PlaceMany(leader, followers, om => new List<Order> { place(om) })[0];

        /// <summary>Places an order (or a bracket's legs) on the leader, then on each follower, linking copy i to leg i.</summary>
        public List<Order> PlaceMany(OrderManager leader, IReadOnlyList<CopyTarget> followers, Func<OrderManager, List<Order>> place)
        {
            List<Order> mine = place(leader);
            // An empty list means "nothing to do on this account" (e.g. no position to close).
            bool accepted = mine.Exists(o => o.Status != OrderStatus.Rejected);
            if (!accepted || followers.Count == 0)
            {
                LastNote = "";
                return mine;
            }

            var refused = new List<string>();
            int copied = 0;
            foreach (CopyTarget f in followers)
            {
                if (f.Orders == leader) continue;
                List<Order> copies = place(f.Orders);
                bool any = false;
                for (int i = 0; i < copies.Count; i++)
                {
                    Order copy = copies[i];
                    if (copy.Status == OrderStatus.Rejected)
                    {
                        refused.Add($"{f.Label}: {copy.StatusReason}");
                        continue;
                    }
                    any = true;
                    if (i < mine.Count) Link(mine[i], f, copy);
                }
                if (any) copied++;
            }
            LastNote = (copied > 0 ? $"Copied to {copied} account{(copied == 1 ? "" : "s")}." : "") +
                       (refused.Count > 0 ? " Not copied: " + string.Join(" ", refused) : "");
            LastNote = LastNote.Trim();
            return mine;
        }

        /// <summary>Cancels a leader order and every copy of it. False if the leader's order wasn't working.</summary>
        public bool Cancel(OrderManager leader, long orderId)
        {
            Order order = Find(leader, orderId);
            bool cancelled = leader.Cancel(orderId);
            if (order != null && _links.TryGetValue(order, out var copies))
                foreach (var (target, copy) in copies)
                    target.Orders.Cancel(copy.Id);
            return cancelled;
        }

        /// <summary>Moves a leader order's price and its copies'. Error from the leader, or null.</summary>
        public string Modify(OrderManager leader, long orderId, decimal price)
        {
            string error = leader.ModifyPrice(orderId, price);
            if (error != null) return error;
            Order order = Find(leader, orderId);
            if (order != null && _links.TryGetValue(order, out var copies))
                foreach (var (target, copy) in copies)
                    target.Orders.ModifyPrice(copy.Id, price);
            return null;
        }

        private void Link(Order leaderOrder, CopyTarget target, Order copy)
        {
            if (!_links.TryGetValue(leaderOrder, out var list)) _links[leaderOrder] = list = new List<(CopyTarget, Order)>();
            list.Add((target, copy));
            // Links to finished orders are no use; drop them so the map doesn't grow all game.
            if (_links.Count > 256)
            {
                var done = new List<Order>();
                foreach (Order o in _links.Keys)
                    if (!o.IsOpen) done.Add(o);
                foreach (Order o in done) _links.Remove(o);
            }
        }

        private static Order Find(OrderManager orders, long id)
        {
            foreach (Order o in orders.OpenOrders)
                if (o.Id == id) return o;
            return null;
        }
    }
}
