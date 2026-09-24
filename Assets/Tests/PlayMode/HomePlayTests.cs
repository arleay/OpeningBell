using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using OpeningBell.Home;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>TOWN_SPEC Part B in the real town: the stores, the loaner truck, carrying and placing, desks and screens, homes.</summary>
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

        /// <summary>The rented apartment is a home like any other: furniture goes down on its floor.</summary>
        [UnityTest]
        public IEnumerator Apartment_TakesFurniture()
        {
            yield return Setup();
            HomeSpec apartment = W.Find(HomeSpec.ApartmentId);
            Assert.IsTrue(W.Owns(apartment));
            _player.PlaceAt(apartment.Root.TransformPoint(new Vector3(0.5f, 0.1f, -1.5f)), 0f, 10f);
            yield return null;
            Hold("nightstand");
            // As the player aims: from eye height in the middle of the room, at floor spots all round it.
            Vector3 eye = apartment.Root.TransformPoint(new Vector3(0.3f, 1.62f, -0.8f));
            var refused = new System.Collections.Generic.List<string>();
            for (float x = -2.4f; x <= 2.4f; x += 0.8f)
            for (float z = -2f; z <= 2f; z += 0.8f)
            {
                Vector3 floor = apartment.Root.TransformPoint(new Vector3(x, 0f, z));
                Carrier.Target aimed = W.Hands.Evaluate(new Ray(eye, floor - eye));
                if (aimed.Home == null) refused.Add($"({x:0.0}, {z:0.0}): {aimed.Why}");
            }
            Assert.IsEmpty(refused, "aimed at the apartment floor but not 'at home': " + string.Join("; ", refused));
            Carrier.Target t = PlaceSomewhere(apartment);
            Assert.AreSame(apartment, t.Home, Why(t));
            Assert.AreEqual(ItemState.Placed, _game.Belongings.Items.Single().State);

            // A home on the market (open house) says it isn't yours, rather than "set it down at home".
            HomeSpec penthouse = W.Homes.First(h => h.Kind == HomeKind.Penthouse);
            Assert.IsFalse(W.Owns(penthouse));
            Hold("nightstand");
            Carrier.Target there = Aim(penthouse.Root.TransformPoint(new Vector3(3f, 0.3f, -2f)));
            StringAssert.Contains("isn't yours", Why(there));
        }

        [UnityTest]
        public IEnumerator Sofa_Bought_Collected_Trucked_Home_AndSaved()
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
            OwnedItem bought = _game.Belongings.Items.Single(i => !i.IsBox);
            Assert.IsTrue(_game.Belongings.Items.Any(i => i.IsBox && i.State == ItemState.AtPickup), "a free moving box comes with the order");
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
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate(); // let the truck settle on its springs
            CargoBed box = W.Bed(Loaner.TruckBed);
            Assert.IsNotNull(box);
            Transform truck = W.Loaner.Truck.transform;

            // Into the truck's box: the back door's down, so first it refuses.
            W.Hands.PickUp(W.View(bought.Uid));
            StringAssert.Contains("door", Why(Aim(box.transform.position + Vector3.up * 0.3f)));
            W.Loaner.Door.Set(true);
            Carrier.Target inBox = Aim(box.transform.position + Vector3.up * 0.3f);
            Assert.IsTrue(inBox.Valid, inBox.Why);
            W.Hands.Place(inBox);
            Assert.AreEqual(ItemState.Loaded, bought.State);
            Assert.AreEqual(Loaner.TruckBed, bought.Vehicle);
            _player.PlaceAt(truck.TransformPoint(new Vector3(-2.2f, 0.1f, -7f)), truck.eulerAngles.y + 15f, 8f);
            yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "home-truck.png");
            _player.PlaceAt(truck.TransformPoint(new Vector3(-7f, 0.1f, 1f)), truck.eulerAngles.y + 90f, 4f);
            yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "home-truck-side.png");

            // Save with the sofa in the truck: it's all there after loading.
            yield return Reload();
            OwnedItem again = _game.Belongings.Items.Single(i => !i.IsBox);
            Assert.AreEqual(ItemState.Loaded, again.State, "still in the truck");
            Assert.IsTrue(W.Loaner.Out, "the loaner came back with the save");
            yield return null;
            Assert.AreEqual(W.Loaner.Truck.transform, W.View(again.Uid).transform.parent, "riding in the truck");

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
            OwnedItem third = _game.Belongings.Items.Single(i => !i.IsBox);
            Assert.AreEqual(ItemState.Placed, third.State);
            Assert.AreEqual(house.Id, third.Property);
            yield return null;
            Assert.Less(Vector3.Distance(W.View(third.Uid).transform.position, at), 0.01f, "exactly where it was put");
            Assert.IsTrue(_game.Estate.Owns(house.Id));
        }

        [UnityTest]
        public IEnumerator MovingBox_PackAtTheYard_CarryHome_UnpackLastInFirstOut()
        {
            yield return Setup();
            // A box out in the pickup yard, flaps open.
            OwnedItem box = _game.Belongings.Add("moving_box", 0, ItemState.Placed);
            Vector3 bay = HomeStores.PickupSlot(0);
            box.Property = "yard";
            box.X = bay.x; box.Y = bay.y; box.Z = bay.z;
            _game.Belongings.Touch();
            _player.PlaceAt(bay + new Vector3(0f, 0.1f, -2.2f), 0f, 20f);
            yield return null;
            Assert.IsNotNull(W.View(box.Uid), "the box stands in the yard");

            // Desk in first, then the sofa (packed by aiming at the box, as the click does).
            OwnedItem desk = Hold("desk_compact").Item;
            W.Hands.PackInto(box);
            Assert.AreEqual(ItemState.Packed, desk.State);
            Assert.IsFalse(W.Hands.Holding);
            OwnedItem sofa = Hold("sofa_mid").Item;
            Carrier.Target atBox = Aim(W.View(box.Uid).transform.position + Vector3.up * 0.3f);
            Assert.AreSame(W.View(box.Uid), atBox.Onto, "aiming at the box: the click packs");
            W.Hands.PackInto(atBox.Onto.Item);
            yield return null;
            Assert.IsNull(W.View(sofa.Uid), "packed things aren't shown");
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "home-box-packed.png");

            // Flaps shut in the yard; [E] carries it from here.
            W.Hands.CloseBox(box);
            Assert.IsTrue(box.Closed);
            Assert.AreEqual("yard", box.ClosedAt);
            // Shut by mistake: a click opens it again, and a click takes the top thing back out, right there.
            W.Hands.OpenBox(box);
            Assert.IsFalse(box.Closed);
            W.Hands.TakeOut(box);
            Assert.AreEqual("sofa_mid", W.Hands.Held.Item.ItemId);
            W.Hands.PackInto(box);
            W.Hands.CloseBox(box);
            yield return null;
            Assert.AreEqual("Carry the box", W.View(box.Uid).Prompt);

            // It all survives a save.
            yield return Reload();
            box = _game.Belongings.Items.Single(i => i.IsBox);
            Assert.AreEqual(2, _game.Belongings.Contents(box).Count);
            yield return null;

            // To the apartment, set down, [E] opens it there.
            HomeSpec apartment = W.Find(HomeSpec.ApartmentId);
            W.Hands.PickUp(W.View(box.Uid));
            _player.PlaceAt(apartment.Root.TransformPoint(new Vector3(0.5f, 0.1f, -1.5f)), 0f, 10f);
            yield return null;
            PlaceSomewhere(apartment);
            Assert.AreEqual(apartment.Id, box.Property);
            yield return null;
            ItemView boxView = W.View(box.Uid);
            Assert.AreEqual("Open the box", boxView.Prompt);
            boxView.Interact();
            Assert.IsFalse(box.Closed);
            yield return null;

            // The sofa went in last, so it comes out first; then the desk.
            W.Hands.TakeOut(box);
            Assert.AreEqual("sofa_mid", W.Hands.Held.Item.ItemId);
            PlaceSomewhere(apartment);
            W.Hands.TakeOut(box);
            Assert.AreEqual("desk_compact", W.Hands.Held.Item.ItemId);
            PlaceSomewhere(apartment);
            Assert.IsTrue(_game.Belongings.Items.Where(i => !i.IsBox).All(i => i.State == ItemState.Placed && i.Property == apartment.Id));

            // Empty: [F] throws it away.
            Assert.IsTrue(W.Hands.Discard(box));
            Assert.IsFalse(_game.Belongings.Items.Any(i => i.IsBox));
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

            // Delivery: to the newest home, next morning.
            ShowroomItem lamp = Object.FindObjectsByType<ShowroomItem>(FindObjectsSortMode.None).First(s => s.Item.Id == "floor_lamp");
            lamp.Interact();
            lamp.Interact();
            DeliveryDesk delivery = Object.FindAnyObjectByType<DeliveryDesk>();
            delivery.Interact();
            delivery.Interact();
            OwnedItem item = _game.Belongings.Items.Single(i => i.ItemId == "floor_lamp" && i.State == ItemState.Delivering);
            Assert.AreEqual(ItemState.Delivering, item.State);
            _game.SkipTo(new System.DateTime(item.DeliverAt).AddMinutes(1));
            yield return new WaitForSecondsRealtime(0.7f);
            Assert.AreEqual(ItemState.Placed, item.State);
            Assert.AreEqual(W.MainHome.Id, item.Property);

            yield return Reload();
            Assert.AreEqual(3, _game.Estate.Owned.Count, "all three homes kept");
            Assert.IsTrue(_game.Estate.Locked(starter.Id), "still locked");
        }

        /// <summary>
        /// Harborview: residents-only lift out of hours, buy it, ride the lift from the lobby to the top, the furniture
        /// that comes with it, the rooms and the terrace, and the view by day, at dusk and at night (home-penthouse-*.png).
        /// </summary>
        [UnityTest]
        public IEnumerator Penthouse_LiftToTheTop_FurnishedWithAView()
        {
            yield return Setup();
            HomeSpec ph = W.Find(HomeSales.PenthouseId);
            Vector3 lobby = _city.Anchors["penthouse_lobby"];
            ElevatorButton call = Object.FindObjectsByType<ElevatorButton>(FindObjectsSortMode.None)
                .Where(b => b.name == "Call").OrderBy(b => Vector3.Distance(b.transform.position, lobby)).First();
            Elevator lift = call.GetComponentInParent<Elevator>();
            Assert.AreEqual(0, call.Floor, "the lobby's call button");
            _game.SkipTo(_game.Clock.Now.Date.AddHours(20));
            Assert.AreEqual("residents only", call.LockReason(), "out of hours, not yours");

            int before = _game.Belongings.Items.Count;
            Assert.IsNull(W.Buy(ph));
            Assert.AreEqual(ph.Staging.Count, _game.Belongings.Items.Count - before, "it comes furnished");
            Assert.IsTrue(_game.Belongings.Items.Skip(before).All(i => i.State == ItemState.Placed && i.Property == ph.Id));
            Assert.IsNull(call.LockReason(), "yours now");
            _game.SkipTo(_game.Clock.Now.Date.AddDays(1).AddHours(12));

            // The lobby, then up in the lift.
            _player.PlaceAt(lobby + new Vector3(0f, 0.05f, -9f), 0f, -4f);
            for (int i = 0; i < 4; i++) yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "home-penthouse-lobby.png");
            call.Interact();
            yield return WaitUntil(() => lift.Logic.DoorOpen > 0.95f, 15f, "the lift at the lobby");
            _player.PlaceAt(lift.transform.position + Vector3.up * 0.05f, 180f, 0f);
            yield return null;
            lift.GetComponentsInChildren<ElevatorButton>().First(b => b.name == "Button PH" && b.transform.position.y < lift.transform.position.y + 3f).Interact();
            yield return WaitUntil(() => _player.transform.position.y > HarborviewTower.Grade + 80f, 40f, "the ride up");
            Assert.AreEqual(1, lift.CarFloor);

            Vector3 inside = _city.Anchors["penthouse_inside"];
            Assert.AreEqual(ph, W.HomeAt(inside));
            Assert.IsTrue(ph.Indoors(inside + Vector3.up * 3.6f), "the ceiling's indoors");
            Vector3 terrace = _city.Anchors["penthouse_terrace"];
            Assert.IsFalse(ph.Indoors(terrace), "the roof terrace is outdoors");
            Assert.IsTrue(ph.Contains(terrace), "but it's yours");

            Camera cam = _player.GetComponentInChildren<Camera>();
            IEnumerator Shot(Vector3 at, float yaw, float pitch, string name)
            {
                _player.PlaceAt(at + Vector3.up * 0.05f, yaw, pitch);
                for (int i = 0; i < 6; i++) yield return null;
                yield return CaptureCamera(cam, name);
            }
            float fy = HarborviewTower.Grade + HarborviewTower.PenthouseFloor;
            Vector3 P(float x, float y, float z) => new Vector3(HarborviewTower.X + x, fy + y, HarborviewTower.Z + z);
            yield return Shot(P(-1f, 0f, 3.5f), 180f, 6f, "home-penthouse.png");
            Assert.Greater(RenderSettings.fogEndDistance, 700f, "the air's clear up here");
            yield return Shot(P(3f, 0f, -2f), 225f, 4f, "home-penthouse-living.png");
            yield return Shot(P(4f, 0f, -9.3f), 200f, 14f, "home-penthouse-window.png");
            yield return Shot(P(8f, 0f, 3f), 30f, 8f, "home-penthouse-kitchen.png");
            yield return Shot(P(-9.5f, 0f, -9f), 10f, 4f, "home-penthouse-bedroom.png");
            yield return Shot(P(-10.5f, 0f, 3.2f), 20f, 8f, "home-penthouse-bath.png");
            yield return Shot(P(10f, 0f, -8.5f), 160f, 10f, "home-penthouse-office.png");
            yield return Shot(P(1.5f, RoofDeck(), -3.5f), 200f, 8f, "home-penthouse-terrace.png");

            // Walk up the stairs to the roof (no bumping your head), then try to jump off the edge.
            UseSimulatedInput();
            _player.PlaceAt(P(3.8f, 0.05f, 4.9f), 0f, 0f);
            yield return null;
            HoldKeys(Key.W);
            yield return WaitUntil(() => _player.transform.position.y > fy + RoofDeck() - 0.1f, 10f, "the climb to the roof");
            HoldKeys();
            _player.PlaceAt(P(1f, RoofDeck() + 0.05f, -9f), 180f, 0f);
            yield return null;
            for (int i = 0; i < 6; i++)
            {
                HoldKeys(Key.W, Key.Space);
                yield return new WaitForSeconds(0.25f);
            }
            HoldKeys();
            Assert.Greater(_player.transform.position.z, HarborviewTower.Z - 12f, "can't get over the edge");
            Assert.Greater(_player.transform.position.y, fy + RoofDeck() - 0.2f, "still on the roof");

            _game.SkipTo(_game.Clock.Now.Date.AddHours(19).AddMinutes(20));
            yield return new WaitForSeconds(0.3f);
            yield return Shot(P(-1f, 0f, 3.5f), 215f, 6f, "home-penthouse-dusk.png");
            _game.SkipTo(_game.Clock.Now.Date.AddHours(22));
            yield return new WaitForSeconds(0.3f);
            yield return Shot(P(-1f, 0f, 3.5f), 180f, 6f, "home-penthouse-night.png");
            yield return Shot(P(4f, 0f, -9.3f), 200f, 14f, "home-penthouse-window-night.png");
            yield return Shot(P(-6f, RoofDeck(), 3f), 160f, 12f, "home-penthouse-pool.png");
            _player.PlaceAt(new Vector3(HarborviewTower.X - 30f, HarborviewTower.Grade + 0.05f, 55f), 20f, -32f);
            for (int i = 0; i < 6; i++) yield return null;
            yield return CaptureCamera(cam, "home-penthouse-tower.png");
        }

        private static float RoofDeck() => HarborviewTower.RoofDeck;
    }
}
