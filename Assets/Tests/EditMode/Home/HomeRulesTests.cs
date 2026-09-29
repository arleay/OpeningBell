using System;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Home;
using UnityEngine;

namespace OpeningBell.Tests
{
    /// <summary>TOWN_SPEC Part B rules: the catalog, desk and arm limits, what stands on what, resale, the loaner, homes.</summary>
    public class HomeRulesTests
    {
        private static HomeItem I(string id) => HomeCatalog.Find(id);

        [Test]
        public void Catalog_IsComplete()
        {
            var items = HomeCatalog.Items;
            Assert.AreEqual(items.Count, items.Select(i => i.Id).Distinct().Count(), "ids unique");
            Assert.IsTrue(items.All(i => (i.Price > 0m || i.IsBox) && i.Variants.Length > 0 && i.Width > 0f && i.Height > 0f), "the moving box is the only free thing");
            CollectionAssert.AreEquivalent(new[] { 2, 3, 4, 6 }, items.Where(i => i.IsDesk).Select(i => i.MonitorSlots).Distinct());
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5, 6 }, items.Where(i => i.IsArm).Select(i => i.Arms));
            foreach (string dept in new[] { "Living", "Bedroom", "Office", "Dining", "Entry", "Decor" })
                Assert.IsTrue(items.Any(i => i.Store == HomeStore.Furniture && i.Department == dept), dept);
            Assert.IsTrue(items.Where(i => i.Store == HomeStore.Tech).All(i => i.Boxed), "tech comes boxed");
            Assert.IsTrue(items.Where(i => i.Store == HomeStore.Furniture).Select(i => i.Tier).Distinct().Count() == 3, "three tiers");
        }

        [Test]
        public void OfficeReadyKits_OneToSixScreens_AllInOneCrate()
        {
            for (int n = 1; n <= 6; n++)
            {
                HomeItem kit = I("kit_" + n);
                Assert.IsTrue(kit.IsKit && kit.Boxed && kit.Store == HomeStore.Tech);
                Assert.AreEqual(50000m, kit.Price);
                string[] parts = HomeItem.KitParts(n);
                Assert.IsTrue(I(parts[0]).IsDesk, "the desk comes first");
                Assert.AreEqual(n, I(parts[1]).Arms, "an arm for exactly the screens");
                Assert.AreEqual(n, parts.Count(id => I(id).IsMonitor));
                CollectionAssert.IsSubsetOf(new[] { "pc_tower", "keyboard", "mouse", "speakers" }, parts);
            }
        }

        [Test]
        public void MovingBox_LastPackedComesOutFirst_AndOnlyWhenOpen()
        {
            var b = new Belongings();
            OwnedItem box = b.Add("moving_box", 0, ItemState.Placed);
            OwnedItem desk = b.Add("desk_compact", 0, ItemState.Carried);
            OwnedItem sofa = b.Add("sofa_mid", 0, ItemState.Carried);
            Assert.IsNull(b.CanPack(desk, box));
            b.Pack(desk, box);
            b.Pack(sofa, box);
            Assert.AreEqual(ItemState.Packed, desk.State);
            CollectionAssert.AreEqual(new[] { desk, sofa }, b.Contents(box));
            StringAssert.Contains("in boxes", b.CanPack(b.Add("moving_box", 0, ItemState.Carried), box));

            b.SetClosed(box, true, "yard");
            Assert.IsTrue(box.Closed);
            Assert.AreEqual("yard", box.ClosedAt);
            StringAssert.Contains("Open", b.CanPack(b.Add("armchair", 0, ItemState.Carried), box));
            b.SetClosed(box, false, null);
            Assert.IsFalse(box.Closed);

            Assert.AreSame(sofa, b.Unpack(box), "the couch went in last, so it comes out first");
            Assert.AreEqual(ItemState.Carried, sofa.State);
            Assert.AreEqual(0, sofa.InBox);
            Assert.AreSame(desk, b.Unpack(box));
            Assert.IsNull(b.Unpack(box), "empty");

            b.Pack(desk, box);
            b.Remove(box);
            Assert.IsNull(b.Get(desk.Uid), "a box goes with what's in it");
        }

        [Test]
        public void MovingBox_DeskWithScreensStaysOut()
        {
            var b = new Belongings();
            OwnedItem box = b.Add("moving_box", 0, ItemState.Placed);
            OwnedItem desk = b.Add("desk_compact", 0, ItemState.Carried);
            b.Add("mon_27", 0, ItemState.Placed).MountedOn = desk.Uid;
            StringAssert.Contains("Take everything off", b.CanPack(desk, box));
        }

        [Test]
        public void Desks_TakeTheirSlots_ArmsAddMore_NeverOverSix()
        {
            var b = new Belongings();
            // A laptop on a desk is a screen of its own (it shows charts, and the desk becomes somewhere to trade).
            var lb = new Belongings();
            OwnedItem work = lb.Add("desk_compact", 0, ItemState.Placed);
            lb.Add("laptop", 0, ItemState.Placed).MountedOn = work.Uid;
            Assert.AreEqual(1, lb.MonitorsOn(work), "a laptop counts as a screen");
            OwnedItem desk = b.Add("desk_compact", 0, ItemState.Placed);
            OwnedItem Monitor() => b.Add("mon_27", 0, ItemState.Placed);
            for (int i = 0; i < 2; i++)
            {
                OwnedItem m = Monitor();
                Assert.IsNull(b.CanMountMonitor(m, desk));
                m.MountedOn = desk.Uid;
            }
            StringAssert.Contains("monitor arm", b.CanMountMonitor(Monitor(), desk), "full: the message points at an arm");

            OwnedItem loose = b.Add("arm_4", 0, ItemState.Placed);
            StringAssert.Contains("desk first", b.CanMountMonitor(Monitor(), loose));
            Assert.IsNull(b.CanMountArm(loose, desk));
            loose.MountedOn = desk.Uid;
            StringAssert.Contains("already", b.CanMountArm(b.Add("arm_2", 0, ItemState.Placed), desk), "one arm per desk");
            for (int i = 0; i < 4; i++)
            {
                OwnedItem m = Monitor();
                Assert.IsNull(b.CanMountMonitor(m, loose), $"arm screen {i}");
                m.MountedOn = loose.Uid;
            }
            Assert.AreEqual(6, b.MonitorsOn(desk), "two on the desk, four on the arm");
            StringAssert.Contains("6 screens", b.CanMountMonitor(Monitor(), desk));

            // Take the desk away: what hung from it comes loose.
            b.Remove(desk);
            Assert.AreEqual(0, loose.MountedOn);
            StringAssert.Contains("desk", b.CanMountArm(loose, b.Add("nightstand", 0, ItemState.Placed)));
        }

        [Test]
        public void Placement_RefusesNonsense()
        {
            Assert.IsNull(PlacementRules.Check(I("sofa_mid"), Under.Floor, null, true));
            StringAssert.Contains("floor", PlacementRules.Check(I("chair_office"), Under.Item, I("bed_double"), true), "chair on a bed");
            StringAssert.Contains("floor", PlacementRules.Check(I("desk_standard"), Under.Item, I("nightstand"), true), "desk on a nightstand");
            Assert.IsNull(PlacementRules.Check(I("mon_24"), Under.Item, I("desk_compact"), true), "monitor on a desk");
            Assert.IsNull(PlacementRules.Check(I("mon_24"), Under.Item, I("arm_3"), true), "monitor on an arm");
            Assert.IsNotNull(PlacementRules.Check(I("desk_lamp"), Under.Item, I("arm_3"), true), "only screens hang on arms");
            Assert.IsNull(PlacementRules.Check(I("desk_lamp"), Under.Item, I("nightstand"), true), "lamp on a nightstand");
            Assert.IsNotNull(PlacementRules.Check(I("mon_24"), Under.Floor, null, true), "monitor on the floor");
            Assert.IsNotNull(PlacementRules.Check(I("mon_24"), Under.Item, I("sofa_mid"), true), "monitor on a sofa");
            Assert.IsNull(PlacementRules.Check(I("pc_tower"), Under.Floor, null, true), "tower on the floor");
            Assert.IsNull(PlacementRules.Check(I("pc_tower"), Under.Item, I("desk_standard"), true), "tower on a desk");
            Assert.IsNotNull(PlacementRules.Check(I("pc_tower"), Under.Item, I("sofa_mid"), true), "tower on a sofa");
            Assert.IsNotNull(PlacementRules.Check(I("wall_art"), Under.Floor, null, true));
            Assert.IsNull(PlacementRules.Check(I("wall_art"), Under.Wall, null, true));
            Assert.IsNull(PlacementRules.Check(I("ceiling_lamp"), Under.Ceiling, null, true));
            Assert.IsNotNull(PlacementRules.Check(I("arm_2"), Under.Item, I("dining_table"), true), "arms clamp to desks only");
            Assert.IsNull(PlacementRules.Check(I("arm_2"), Under.Item, I("desk_trading"), true));
            StringAssert.Contains("indoors", PlacementRules.Check(I("sofa_budget"), Under.Ground, null, false));
            Assert.IsNull(PlacementRules.Check(I("plant"), Under.Ground, null, false), "plants go outside");
            Assert.IsNotNull(PlacementRules.Check(I("sofa_budget"), Under.Nothing, null, true), "floating");
        }

        [Test]
        public void Resale_IsFortyToSeventyPercent()
        {
            var b = new Belongings();
            OwnedItem sofa = b.Add("sofa_premium", 1, ItemState.Placed);
            Assert.AreEqual(sofa.Item.Price * 0.7m, Belongings.ResaleValue(sofa));
            sofa.Condition = 0;
            Assert.AreEqual(sofa.Item.Price * 0.4m, Belongings.ResaleValue(sofa));
            Assert.AreEqual(3, b.Add("sofa_premium", 99, ItemState.Placed).Variant, "variant clamped to the colours on offer");
        }

        [Test]
        public void Loaner_RemindsLateFeesAndDamage()
        {
            var r = new Rental();
            var now = new DateTime(2026, 3, 2, 12, 0, 0);
            r.Start(now, 1.0);
            Assert.AreEqual(now + Rental.Loan, r.Due);
            CollectionAssert.IsEmpty(r.RemindersDue(now.AddMinutes(30)));
            CollectionAssert.AreEqual(new[] { 60 }, r.RemindersDue(now.AddMinutes(61)));
            CollectionAssert.IsEmpty(r.RemindersDue(now.AddMinutes(62)), "each reminder once");
            CollectionAssert.AreEqual(new[] { 30, 10 }, r.RemindersDue(now.AddMinutes(115)), "a sleep past two of them");

            Assert.AreEqual(0m, Rental.LateFee(r.Due, r.Due + Rental.Grace), "grace");
            Assert.AreEqual(20m, Rental.LateFee(r.Due, r.Due + Rental.Grace + TimeSpan.FromMinutes(1)));
            Assert.AreEqual(60m, Rental.LateFee(r.Due, r.Due + Rental.Grace + TimeSpan.FromMinutes(61)));

            var onTime = r.Return(now.AddMinutes(100), 1.0);
            Assert.AreEqual(Rental.DepositAmount, onTime.Refund, "on time and clean: the whole deposit back");
            Assert.IsFalse(r.Active);

            r.Start(now, 0.9);
            var late = r.Return(r.Due + TimeSpan.FromHours(2), 0.8);
            Assert.AreEqual(80m, late.Late, "110 min past the grace: four half hours");
            Assert.AreEqual(250m, late.Damage);
            Assert.AreEqual(0m, late.Refund);
            Assert.AreEqual(180m, late.Extra, "what the deposit didn't cover");
        }

        [Test]
        public void Homes_SeveralOwned_LocksAndSaves()
        {
            var e = new Estate();
            Assert.IsTrue(e.Acquire("house_12"));
            Assert.IsTrue(e.Acquire("penthouse"));
            Assert.IsFalse(e.Acquire("penthouse"), "already yours");
            Assert.IsFalse(e.Locked("house_12"), "new keys: open");
            e.SetLocked("house_12", true);
            e.SetGarage("penthouse", true);

            var game = new SaveGame { HasHome = true };
            var b = new Belongings();
            OwnedItem mon = b.Add("mon_32", 1, ItemState.Placed);
            mon.View = MonitorView.Portfolio;
            mon.Symbol = "APEX";
            mon.Portrait = true;
            mon.Power = false;
            game.Home.Belongings = b.CaptureState();
            game.Home.Estate = e.CaptureState();
            var rental = new Rental();
            rental.Start(new DateTime(2026, 3, 2, 9, 0, 0), 1.0);
            game.Home.Rental = rental.CaptureState();
            var cart = new MovingCart();
            cart.TakeOut();
            cart.SetPose(260.5, 88.02, 115.25, 90);
            game.Home.Cart = cart.CaptureState();

            SaveGame back = JsonUtility.FromJson<SaveGame>(JsonUtility.ToJson(game));
            var e2 = new Estate();
            e2.RestoreState(back.Home.Estate);
            CollectionAssert.AreEquivalent(new[] { "house_12", "penthouse" }, e2.Owned);
            Assert.IsTrue(e2.Locked("house_12"));
            Assert.IsTrue(e2.GarageOpen("penthouse"));
            var b2 = new Belongings();
            b2.RestoreState(back.Home.Belongings);
            OwnedItem m2 = b2.Items.Single();
            Assert.AreEqual((MonitorView.Portfolio, "APEX", true, false, 1), (m2.View, m2.Symbol, m2.Portrait, m2.Power, m2.Variant));
            Assert.AreEqual(2, b2.Add("mouse", 0, ItemState.Carried).Uid, "uids carry on");
            var r2 = new Rental();
            r2.RestoreState(back.Home.Rental);
            Assert.IsTrue(r2.Active);
            Assert.AreEqual(rental.Due, r2.Due);
            var c2 = new MovingCart();
            c2.RestoreState(back.Home.Cart);
            Assert.IsTrue(c2.Out, "the cart stays where it was left, paid for");
            Assert.AreEqual((260.5, 88.02, 115.25, 90.0), c2.Pose);
            c2.Return();
            Assert.IsFalse(c2.Out, "sent back: the next use is paid again");
        }

        [Test]
        public void Shop_CartCheckoutAndDeliveryTimes()
        {
            var shop = new HomeShop();
            var b = new Belongings();
            var now = new DateTime(2026, 3, 2, 14, 0, 0);
            shop.Add(HomeStore.Tech, "mon_27", 0, 2);
            shop.Add(HomeStore.Tech, "mon_27", 0);
            shop.Add(HomeStore.Tech, "sofa_mid", 0);
            Assert.AreEqual(3, shop.CartCount(HomeStore.Tech), "same item merges; furniture stays out of the tech cart");

            // Half an hour for one thing, never over an hour however much or with the move-in.
            Assert.AreEqual(30, HomeShop.DeliveryMinutes(1, false));
            Assert.AreEqual(60, HomeShop.DeliveryMinutes(40, true));
            ShopQuote q = HomeShop.Quote(shop.Cart(HomeStore.Tech), Fulfilment.Delivery, true, now);
            Assert.AreEqual(3 * 329m, q.Goods);
            Assert.AreEqual(HomeShop.DeliveryFee + HomeShop.ServiceFee(3), q.Delivery + q.Service);
            Assert.That((q.Due - now).TotalMinutes, Is.InRange(30, 60));

            decimal paid = 0m;
            var (order, error) = shop.Place(HomeStore.Tech, shop.Cart(HomeStore.Tech), Fulfilment.Delivery, "penthouse", "Harborview", true, now, b,
                (amount, _) => { paid = amount; return null; });
            Assert.IsNull(error);
            Assert.AreEqual(q.Total, paid);
            Assert.AreEqual(3, b.Items.Count(i => i.Order == order.Id && i.State == ItemState.Delivering && i.Property == "penthouse"));
            Assert.AreEqual(0, b.AtCounter().Count, "online goods never wait at the till's counter");

            var (pickup, _) = shop.Place(HomeStore.Furniture, new[] { new CartLine { ItemId = "sofa_mid", Qty = 1 } }, Fulfilment.Pickup, "", "", true, now, b, (_, __) => null);
            Assert.AreEqual(0m, pickup.DeliveryFee + pickup.ServiceFee, "pickup is free; no move-in without delivery");
            Assert.AreEqual(2, pickup.Items.Count, "furniture picked up comes with a moving box");
            Assert.AreEqual(now.AddMinutes(HomeShop.PickupMinutes).Ticks, pickup.Due);

            var (none, declined) = shop.Place(HomeStore.Tech, new[] { new CartLine { ItemId = "mouse" } }, Fulfilment.Pickup, "", "", false, now, b, (_, __) => "Insufficient funds.");
            Assert.IsNull(none);
            Assert.AreEqual("Insufficient funds.", declined);
        }
    }
}
