using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using OpeningBell.Home;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>TOWN_SPEC Part B in the real town: the stores, the loaner and trailer, carrying and placing, desks and screens, homes.</summary>
    public class HomePlayTests : SceneTestBase
    {
        private CityBuilder _city;
        private GameBootstrap _game;
        private FirstPersonController _player;
        private HomeWorld W => _city.Home;

        private IEnumerator Setup()
        {
            yield return LoadMain();
            _city = Find<CityBuilder>();
            _game = Find<GameBootstrap>();
            _player = Find<FirstPersonController>();
            _game.SkipTo(_game.Clock.Now.Date.AddDays(1).AddHours(11));
            _game.Economy.DevDeposit(3000000m, _game.Clock.Now);
            for (int i = 0; i < 3; i++) yield return null;
        }

        private IEnumerator Reload()
        {
            _game.Save();
            yield return SceneManager.LoadSceneAsync("Main");
            yield return null;
            yield return null;
            _city = Find<CityBuilder>();
            _game = Find<GameBootstrap>();
            _player = Find<FirstPersonController>();
            yield return null;
        }

        private HomeSpec Buy(HomeKind kind)
        {
            HomeSpec h = W.Homes.First(x => x.Kind == kind && !W.Owns(x));
            Assert.IsNull(W.Buy(h), "bought " + h.Name);
            return h;
        }

        /// <summary>Straight into the hands (as a purchase is).</summary>
        private ItemView Hold(string id, int variant = 0)
        {
            OwnedItem i = _game.Belongings.Add(id, variant, ItemState.Carried);
            return W.Hands.TakeNew(i);
        }

        private Carrier.Target Aim(Vector3 at, float above = 1.4f) => W.Hands.Evaluate(new Ray(at + Vector3.up * above, Vector3.down));

        /// <summary>The refusal, or a description of what it would have done (so a wrong "yes" reads clearly).</summary>
        private static string Why(Carrier.Target t) => t.Why ?? $"(accepted: {t.Under} onto {(t.Onto != null ? t.Onto.name : "-")} at {t.At})";

        /// <summary>A spot on a home's ground floor where the held item fits (searched on a grid), placed there.</summary>
        private Carrier.Target PlaceSomewhere(HomeSpec h)
        {
            for (float x = -h.Size.x / 2f + 1f; x < h.Size.x / 2f - 1f; x += 0.5f)
            for (float z = -h.Size.y / 2f + 1.2f; z < h.Size.y / 2f - 1f; z += 0.5f)
            {
                Carrier.Target t = Aim(h.Root.TransformPoint(new Vector3(x, 0.3f, z)));
                if (!t.Valid) continue;
                W.Hands.Place(t);
                return t;
            }
            Assert.Fail("no room anywhere in " + h.Name + " for " + W.Hands.Held.Spec.Name);
            return null;
        }

        [UnityTest]
        public IEnumerator Sofa_Bought_Collected_Trailered_Home_AndSaved()
        {
            yield return Setup();
            ShowroomItem sofa = Object.FindObjectsByType<ShowroomItem>(FindObjectsSortMode.None).First(s => s.Item.Id == "sofa_budget");
            _player.PlaceAt(sofa.transform.position + new Vector3(0f, 0.1f, -2.5f), 0f, 10f);
            yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "home-showroom.png");
            decimal bank = _game.Economy.Bank.Balance;
            sofa.NextColour();
            sofa.Interact();
            Assert.AreEqual(bank, _game.Economy.Bank.Balance, "the first press asks");
            sofa.Interact();
            Assert.AreEqual(bank - sofa.Item.Price, _game.Economy.Bank.Balance);
            OwnedItem bought = _game.Belongings.Items.Single();
            Assert.AreEqual(ItemState.AtPickup, bought.State);
            Assert.AreEqual(1, bought.Variant, "the colour on the tag");

            Object.FindAnyObjectByType<PickupCounter>().Interact();
            Assert.AreEqual(ItemState.Placed, bought.State);
            yield return null;
            Assert.IsNotNull(W.View(bought.Uid), "out in the pickup bay");

            RentalDesk rental = Object.FindAnyObjectByType<RentalDesk>();
            rental.Interact();
            rental.Interact();
            Assert.IsTrue(W.Loaner.Out, "loaner lent");
            Assert.IsTrue(_game.Rental.Active);
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate(); // let the trailer settle on its wheels
            CargoBed trailer = W.Bed(Loaner.TrailerBed);
            Assert.IsNotNull(trailer);

            // Onto the trailer: the gate's up, so first it refuses.
            W.Hands.PickUp(W.View(bought.Uid));
            Assert.AreEqual(Carrier.LargeSpeed, _player.SpeedFactor, "a sofa is slow going");
            StringAssert.Contains("gate", Why(Aim(trailer.transform.position + Vector3.up * 0.1f)));
            W.Loaner.Trailer.GetComponentInChildren<TrailerGate>().Set(true);
            Carrier.Target onTrailer = Aim(trailer.transform.position + Vector3.up * 0.1f);
            Assert.IsTrue(onTrailer.Valid, onTrailer.Why);
            W.Hands.Place(onTrailer);
            Assert.AreEqual(ItemState.Loaded, bought.State);
            Assert.AreEqual(Loaner.TrailerBed, bought.Vehicle);
            _player.PlaceAt(trailer.transform.position + new Vector3(-4f, 0.5f, -5f), 40f, 12f);
            yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "home-trailer.png");

            // Save with the sofa on the trailer: it's all there after loading.
            yield return Reload();
            OwnedItem again = _game.Belongings.Items.Single();
            Assert.AreEqual(ItemState.Loaded, again.State, "still on the trailer");
            Assert.IsTrue(W.Loaner.Out, "the loaner came back with the save");
            yield return null;
            Assert.AreEqual(W.Loaner.Trailer.transform, W.View(again.Uid).transform.parent, "riding on the trailer");

            // Home: a starter house, the sofa into the living room.
            _game.Economy.DevDeposit(300000m, _game.Clock.Now);
            HomeSpec house = Buy(HomeKind.Starter);
            W.Hands.PickUp(W.View(again.Uid));
            _player.PlaceAt(house.Root.TransformPoint(house.DoorLocal + new Vector3(0f, 0.2f, 1.5f)), house.Root.eulerAngles.y, 0f);
            yield return null;
            Carrier.Target spot = PlaceSomewhere(house);
            Assert.AreEqual(ItemState.Placed, again.State);
            Assert.AreEqual(house.Id, again.Property);
            Vector3 at = new Vector3(again.X, again.Y, again.Z);
            _player.PlaceAt(spot.At + (house.Root.rotation * new Vector3(0f, 0.1f, -2.4f)), house.Root.eulerAngles.y, 12f);
            yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "home-sofa.png");

            yield return Reload();
            OwnedItem third = _game.Belongings.Items.Single();
            Assert.AreEqual(ItemState.Placed, third.State);
            Assert.AreEqual(house.Id, third.Property);
            yield return null;
            Assert.Less(Vector3.Distance(W.View(third.Uid).transform.position, at), 0.01f, "exactly where it was put");
            Assert.IsTrue(_game.Estate.Owns(house.Id));
        }

        [UnityTest]
        public IEnumerator Placement_RefusesNonsense_InAHouse()
        {
            yield return Setup();
            HomeSpec house = Buy(HomeKind.Family);
            _player.PlaceAt(house.Root.TransformPoint(house.DoorLocal + new Vector3(0f, 0.2f, 1.5f)), house.Root.eulerAngles.y, 0f);
            yield return null;

            Hold("bed_double");
            PlaceSomewhere(house);
            ItemView bed = W.Views.First(v => v.Spec.Id == "bed_double");
            Hold("chair_office");
            StringAssert.Contains("floor", Why(Aim(bed.transform.position + Vector3.up * 0.3f)), "no chair on the bed");
            PlaceSomewhere(house);

            Hold("desk_compact");
            PlaceSomewhere(house);
            ItemView desk = W.Views.First(v => v.Spec.IsDesk);
            Hold("desk_lamp");
            StringAssert.Contains("desk or a table", Why(Aim(house.Root.TransformPoint(new Vector3(0f, 0.3f, 0f)))), "a lamp needs a top");
            Carrier.Target onDesk = W.Hands.Evaluate(new Ray(desk.transform.position + Vector3.up * 1.2f, Vector3.down));
            Assert.IsTrue(onDesk.Valid, onDesk.Why);
            W.Hands.Place(onDesk);

            // Across the doorway; outside anyone's home; a desk on the nightstand; a ceiling lamp on the floor.
            Hold("desk_standard");
            Assert.IsFalse(Aim(house.Root.TransformPoint(house.DoorLocal + new Vector3(0.5f, 0.3f, 0.8f))).Valid, "not across the doorway");
            StringAssert.Contains("at home", Why(Aim(new Vector3(5f, 0.5f, -12f))));
            W.Hands.Stow(house);
            Assert.AreEqual(ItemState.Stored, _game.Belongings.Items.First(i => i.ItemId == "desk_standard").State, "put away");
            Hold("nightstand");
            PlaceSomewhere(house);
            ItemView stand = W.Views.First(v => v.Spec.Id == "nightstand");
            StorageCupboard cupboard = Object.FindObjectsByType<StorageCupboard>(FindObjectsSortMode.None)
                .OrderBy(x => Vector3.Distance(x.transform.position, house.Root.position)).First();
            cupboard.Interact();
            Assert.AreEqual("desk_standard", W.Hands.Held.Spec.Id, "out of storage");
            StringAssert.Contains("floor", Why(W.Hands.Evaluate(new Ray(stand.transform.position + Vector3.up * 1.2f, Vector3.down))), "no desk on a nightstand");
            W.Hands.Stow(house);
            Hold("ceiling_lamp");
            Assert.IsFalse(Aim(house.Root.TransformPoint(new Vector3(0f, 0.3f, 0f))).Valid, "hangs from the ceiling");
            Carrier.Target up = W.Hands.Evaluate(new Ray(house.Root.TransformPoint(new Vector3(0f, 1.5f, 0f)), Vector3.up));
            Assert.AreEqual(Under.Ceiling, up.Under);
            W.Hands.Stow(house);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Desk_TakesScreens_OnArms_WithLiveCharts_AndYouCanTradeThere()
        {
            yield return Setup();
            HomeSpec house = Buy(HomeKind.Family);
            _player.PlaceAt(house.Root.TransformPoint(house.DoorLocal + new Vector3(0f, 0.2f, 1.5f)), house.Root.eulerAngles.y, 0f);
            yield return null;
            Hold("desk_compact");
            PlaceSomewhere(house);
            ItemView desk = W.Views.First(v => v.Spec.IsDesk);
            Vector3 top = desk.transform.position + Vector3.up * 1.2f;

            // Two standing screens, then it's full.
            for (int i = 0; i < 2; i++)
            {
                Hold("mon_27");
                Carrier.Target t = W.Hands.Evaluate(new Ray(top + desk.transform.right * (i == 0 ? -0.3f : 0.3f), Vector3.down));
                Assert.IsTrue(t.Valid, $"standing screen {i}: {t.Why} ({t.Under} onto {(t.Onto != null ? t.Onto.name : "-")})");
                W.Hands.Place(t);
            }
            Hold("mon_27");
            StringAssert.Contains("monitor arm", Why(W.Hands.Evaluate(new Ray(top, Vector3.down))));
            W.Hands.Stow(house);

            // A quad arm on the back edge, then four more on it: six on the desk, and no seventh.
            Hold("arm_4");
            Carrier.Target arm = W.Hands.Evaluate(new Ray(top, Vector3.down));
            Assert.IsTrue(arm.Valid, arm.Why);
            W.Hands.Place(arm);
            ItemView armView = W.Views.First(v => v.Spec.IsArm);
            for (int i = 0; i < 4; i++)
            {
                if (i == 0) Object.FindObjectsByType<StorageCupboard>(FindObjectsSortMode.None)
                    .OrderBy(x => Vector3.Distance(x.transform.position, house.Root.position)).First().Interact();
                else Hold("mon_24");
                Carrier.Target t = W.Hands.Evaluate(new Ray(armView.transform.position + Vector3.up * 1.8f, Vector3.down));
                Assert.IsTrue(t.Valid, $"arm screen {i}: {t.Why}");
                Assert.AreEqual(armView, t.Onto, "onto the arm");
                W.Hands.Place(t);
            }
            Assert.AreEqual(6, _game.Belongings.MonitorsOn(desk.Item));
            Hold("mon_24");
            Carrier.Target seventh = W.Hands.Evaluate(new Ray(armView.transform.position + Vector3.up * 1.8f, Vector3.down));
            StringAssert.Contains("6 screens", Why(seventh), "no seventh screen");
            W.Hands.Stow(house);

            // Real data on the screens.
            var screens = W.Views.Where(v => v != null && v.Spec.IsMonitor && v.Item.MountedOn != 0).Select(v => v.GetComponent<MonitorScreen>()).ToArray();
            Assert.AreEqual(6, screens.Length);
            var sec = _game.Market.Securities[0];
            OwnedItem first = screens[0].GetComponent<ItemView>().Item;
            first.View = MonitorView.Chart;
            first.Symbol = sec.Ticker;
            screens[0].Draw(first);
            StringAssert.Contains(sec.Ticker, screens[0].Shown);
            StringAssert.Contains(sec.Last.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), screens[0].Shown, "the live last price");
            OwnedItem second = screens[1].GetComponent<ItemView>().Item;
            second.View = MonitorView.Watchlist;
            screens[1].Draw(second);
            foreach (var s in _game.Market.Securities) StringAssert.Contains(s.Ticker, screens[1].Shown);
            second.Power = false;
            second.Portrait = true;
            OwnedItem third2 = screens[2].GetComponent<ItemView>().Item;
            third2.View = MonitorView.Portfolio;
            _game.Belongings.Touch();
            _player.PlaceAt(desk.transform.TransformPoint(new Vector3(0f, 0.1f, -2.6f)), desk.transform.eulerAngles.y, 8f);
            for (int i = 0; i < 3; i++) yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "home-desk.png");

            // Sit down and trade from here.
            SeatInteractable seat = desk.GetComponentInChildren<SeatInteractable>();
            Assert.IsNotNull(seat, "a desk with screens is a trading seat");
            WorkstationController ws = Find<WorkstationController>();
            seat.Interact();
            yield return WaitUntil(() => ws.State == WorkstationState.Seated, 5f, "seated at the new desk");
            Assert.AreEqual(seat.GetComponent<Desk>(), ws.CurrentDesk);
            ws.StandUp();
            yield return WaitUntil(() => ws.State == WorkstationState.Standing, 5f, "stood up");

            // Power, view and orientation are kept.
            int powerUid = second.Uid, portfolioUid = third2.Uid;
            yield return Reload();
            OwnedItem p2 = _game.Belongings.Get(powerUid), f2 = _game.Belongings.Get(portfolioUid);
            Assert.IsFalse(p2.Power);
            Assert.IsTrue(p2.Portrait);
            Assert.AreEqual(MonitorView.Portfolio, f2.View);
            Assert.AreEqual(6, _game.Belongings.MonitorsOn(_game.Belongings.Items.First(i => i.Item.IsDesk)));
        }

        [UnityTest]
        public IEnumerator ThirtyScreens_StayCheap()
        {
            yield return Setup();
            HomeSpec house = Buy(HomeKind.Mansion);
            _player.PlaceAt(house.Root.TransformPoint(house.DoorLocal + new Vector3(0f, 0.2f, 2f)), house.Root.eulerAngles.y, 0f);
            yield return null;
            // Five trading desks with six screens each, set up directly (the rules are tested elsewhere).
            for (int d = 0; d < 5; d++)
            {
                OwnedItem desk = _game.Belongings.Add("desk_trading", 0, ItemState.Placed);
                Vector3 p = house.Root.TransformPoint(new Vector3(-5f + d * 2.6f, HouseBuilder.Floor, 2.5f));
                // Facing the player (a desk's user side and its screens are its -z).
                desk.X = p.x; desk.Y = p.y; desk.Z = p.z; desk.Yaw = house.Root.eulerAngles.y; desk.Property = house.Id;
                OwnedItem arm = _game.Belongings.Add("arm_6", 0, ItemState.Placed);
                arm.Boxed = false;
                arm.MountedOn = desk.Uid;
                Vector3 back = Quaternion.Euler(0f, desk.Yaw, 0f) * new Vector3(0f, 0.75f, 0.9f / 2f - 0.08f);
                arm.X = p.x + back.x; arm.Y = p.y + back.y; arm.Z = p.z + back.z; arm.Yaw = desk.Yaw;
                for (int m = 0; m < 6; m++)
                {
                    OwnedItem mon = _game.Belongings.Add("mon_24", 0, ItemState.Placed);
                    mon.Boxed = false;
                    mon.MountedOn = arm.Uid;
                    mon.View = (MonitorView)(m % 5);
                    Vector3 slot = Quaternion.Euler(0f, desk.Yaw, 0f) * HomeModels.ArmSlot(6, m);
                    mon.X = arm.X + slot.x; mon.Y = arm.Y + slot.y; mon.Z = arm.Z + slot.z; mon.Yaw = desk.Yaw;
                }
            }
            _game.Belongings.Touch();
            yield return null;
            Assert.AreEqual(30, W.Views.Count(v => v.Spec.IsMonitor));
            _player.PlaceAt(house.Root.TransformPoint(new Vector3(0f, HouseBuilder.Floor + 0.1f, -1.5f)), house.Root.eulerAngles.y, 5f);
            // Batch mode only renders a camera that has a target: give it one, so screens count as seen.
            Camera cam = _player.GetComponentInChildren<Camera>();
            var target = new RenderTexture(1280, 720, 24);
            cam.targetTexture = target;
            yield return null;
            int before = MonitorScreen.Redraws;
            float start = Time.realtimeSinceStartup;
            int frames = 0;
            while (Time.realtimeSinceStartup - start < 3f) { frames++; yield return null; }
            int redraws = MonitorScreen.Redraws - before;
            cam.targetTexture = null;
            target.Release();
            float ms = (Time.realtimeSinceStartup - start) * 1000f / frames;
            Debug.Log($"PERF 30 screens: {redraws} redraws in 3 s, {frames} frames, {ms:F1} ms/frame, {MonitorScreen.Live} textures live");
            Assert.Greater(redraws, 10, "the screens in view do redraw");
            Assert.LessOrEqual(redraws, 30 * 4, "each screen at most about once a second");
            Assert.LessOrEqual(MonitorScreen.Live, 30, "textures pooled, one per chart at most");
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "home-thirty-screens.png");
        }

        [UnityTest]
        public IEnumerator Loaner_Reminds_ThenLate_CostsTheDeposit()
        {
            yield return Setup();
            string said = null;
            W.Said += s => said = s;
            decimal bank = _game.Economy.Bank.Balance;
            RentalDesk desk = Object.FindAnyObjectByType<RentalDesk>();
            desk.Interact();
            desk.Interact();
            Assert.AreEqual(bank - Rental.DepositAmount, _game.Economy.Bank.Balance, "deposit");
            System.DateTime due = _game.Rental.Due;
            _game.SkipTo(due.AddMinutes(-50));
            yield return new WaitForSecondsRealtime(0.7f);
            StringAssert.Contains("an hour", said, "the hour's reminder");
            _game.SkipTo(due.AddMinutes(-5));
            yield return new WaitForSecondsRealtime(0.7f);
            StringAssert.Contains("10 minutes", said);

            // Out of the bay: refused. Parked in it, 70 minutes late: $60 of fees out of the deposit.
            desk.Interact();
            Assert.IsTrue(W.Loaner.Out, "not in the return bay yet");
            CarController truck = W.Loaner.Truck;
            Vector2 bay = HomeStores.ReturnBay.center;
            _game.Vehicles.Park(W.Loaner.Vehicle, bay.x, 0.05, bay.y, 0);
            truck.transform.position = new Vector3(bay.x, 0.05f, bay.y);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(W.Loaner.InReturnBay);
            _game.SkipTo(due.AddMinutes(70));
            decimal before = _game.Economy.Bank.Balance;
            desk.Interact();
            Assert.IsFalse(W.Loaner.Out, "handed back");
            Assert.AreEqual(before + Rental.DepositAmount - 40m, _game.Economy.Bank.Balance, "deposit less two half-hours late (an hour past the grace)");
        }

        [UnityTest]
        public IEnumerator Homes_SeveralOwned_Locked_Garage_Penthouse_Delivery()
        {
            yield return Setup();
            HomeSpec starter = Buy(HomeKind.Starter), family = Buy(HomeKind.Family);
            Assert.AreEqual(2, _game.Estate.Owned.Count);
            HomeKeypad pad = Object.FindObjectsByType<HomeKeypad>(FindObjectsSortMode.None)
                .OrderBy(k => Vector3.Distance(k.transform.position, starter.Root.TransformPoint(starter.DoorLocal))).First();
            pad.Interact();
            Assert.IsTrue(_game.Estate.Locked(starter.Id));
            Door door = Object.FindObjectsByType<Door>(FindObjectsSortMode.None)
                .OrderBy(d => Vector3.Distance(d.transform.position, starter.Root.TransformPoint(starter.DoorLocal))).First();
            Assert.IsTrue(door.IsLocked, "the keypad locks the front door");

            GarageButton garage = Object.FindObjectsByType<GarageButton>(FindObjectsSortMode.None)
                .FirstOrDefault(g => Vector3.Distance(g.transform.position, family.Plot.position) < family.LotDepth + 5f);
            if (garage != null)
            {
                garage.Interact();
                yield return new WaitForSeconds(3.5f);
                Assert.IsTrue(garage.IsOpen, "the roll-up door goes up");
            }

            HomeSpec penthouse = W.Find(HomeSales.PenthouseId);
            Assert.IsNotNull(penthouse);
            Assert.IsNull(W.Buy(penthouse));
            _player.PlaceAt(_city.Anchors["penthouse_inside"] + Vector3.up * 0.1f, 180f, 0f);
            for (int i = 0; i < 4; i++) yield return null;
            Assert.Greater(_player.transform.position.y, 60f, "standing in the penthouse");
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "home-penthouse.png");

            // Delivery: to the newest home, next morning.
            ShowroomItem lamp = Object.FindObjectsByType<ShowroomItem>(FindObjectsSortMode.None).First(s => s.Item.Id == "floor_lamp");
            lamp.Interact();
            lamp.Interact();
            DeliveryDesk delivery = Object.FindAnyObjectByType<DeliveryDesk>();
            delivery.Interact();
            delivery.Interact();
            OwnedItem item = _game.Belongings.Items.Single();
            Assert.AreEqual(ItemState.Delivering, item.State);
            _game.SkipTo(new System.DateTime(item.DeliverAt).AddMinutes(1));
            yield return new WaitForSecondsRealtime(0.7f);
            Assert.AreEqual(ItemState.Placed, item.State);
            Assert.AreEqual(W.MainHome.Id, item.Property);

            yield return Reload();
            Assert.AreEqual(3, _game.Estate.Owned.Count, "all three homes kept");
            Assert.IsTrue(_game.Estate.Locked(starter.Id), "still locked");
        }
    }
}
