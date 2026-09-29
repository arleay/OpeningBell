using System;
using System.Collections.Generic;
using OpeningBell.Casino;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>
    /// The Meridian Hotel's front desk (CASINO_SPEC §48–51, §90): pick a room class and a number of nights, see the
    /// price with the weekend premium and your rewards discount, book (the bank pays), get the key card. Extend or
    /// check out here too. The rooms themselves are on the fifth floor and the penthouse is its own floor.
    /// </summary>
    public sealed class HotelDesk : Interactable
    {
        private GameBootstrap _game;
        private InteractionHud _hud;
        private StaffNpc _clerk;
        private CasinoControls _controls;
        private int _nights = 1;
        private RoomClass _choice = RoomClass.Standard;

        private VisualElement _panel;
        private Label _stay, _quote, _status, _tier;
        private readonly List<CasinoUi.Pill> _pills = new List<CasinoUi.Pill>();
        private readonly Dictionary<RoomClass, CasinoUi.Pill> _rooms = new Dictionary<RoomClass, CasinoUi.Pill>();

        public bool IsOpen { get; private set; }
        public string Status => _status?.text;

        public void Configure(CityContext c, StaffNpc clerk)
        {
            _game = c.Game;
            _hud = c.Hud;
            _clerk = clerk;
            _controls = new CasinoControls(c.Player);
        }

        private Hotel Hotel => _game.Casino.Hotel;
        private decimal Discount => _game.Casino.Rewards.HotelDiscount;

        public override string Prompt => "Front desk · Meridian Hotel";
        public override string Details => Hotel.Active(_game.Clock.Now) ? $"Your room: {Hotel.Stay.Room}, until {Hotel.Stay.CheckOut:ddd h tt}" : "Rooms from $189 a night";
        public override bool CanInteract => base.CanInteract && !IsOpen && (_clerk == null || _clerk.AtStation);

        public override void Interact() => Open();

        public void Open()
        {
            if (IsOpen) return;
            if (_panel == null)
            {
                if (_hud.Root == null) return;
                Build(_hud.Root);
            }
            IsOpen = true;
            _controls.Take();
            _panel.style.display = DisplayStyle.Flex;
            _status.text = "";
            Say(Hotel.Active(_game.Clock.Now) ? "Welcome back. Everything all right with the room?" : "Good evening. Staying with us tonight?");
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _panel.style.display = DisplayStyle.None;
            _controls.Release();
        }

        public void Choose(RoomClass c) => _choice = c;
        public void SetNights(int n) => _nights = Math.Max(1, Math.Min(Hotel.MaxNights, n));

        /// <summary>Books the chosen room. Null on success, else why not.</summary>
        public string Book()
        {
            DateTime now = _game.Clock.Now;
            decimal price = Hotel.Quote(_choice, now, _nights, Discount);
            // The room goes through the resort's books (points, spending history) as one charge.
            string error = Hotel.Book(_choice, _nights, now, Discount,
                (amount, what) => _game.Casino.Spend(SpendCategory.Hotel, amount, what, now, (a, w) => _game.Economy.Spend(a, w, now)));
            if (error != null)
            {
                _status.text = error;
                return error;
            }
            string where = _choice == RoomClass.Penthouse ? "the penthouse: the elevator takes your key to PH" : $"room {Hotel.Stay.Room} on the fifth floor";
            _status.text = $"Booked: {CasinoMoney.Cents(price)}. Your key card is for {where}. Check-out {Hotel.Stay.CheckOut:ddd h tt}.";
            Say("Here's your key card. The elevators are just behind me.");
            _hud.ShowToast($"Key card: {Hotel.NameOf(_choice)} ({Hotel.Stay.Room})", 4f);
            return null;
        }

        public string Extend()
        {
            DateTime now = _game.Clock.Now;
            string error = Hotel.Extend(1, now, Discount, (amount, what) => _game.Casino.Spend(SpendCategory.Hotel, amount, what, now, (a, w) => _game.Economy.Spend(a, w, now)));
            _status.text = error ?? $"One more night. Check-out is now {Hotel.Stay.CheckOut:ddd h tt}.";
            return error;
        }

        public void CheckOut()
        {
            if (!Hotel.Active(_game.Clock.Now)) return;
            Hotel.CheckOut();
            _status.text = "Checked out. Thanks for staying with us.";
            Say("Hope to see you again soon.");
        }

        private void Say(string line)
        {
            if (_clerk != null) _clerk.Say(line);
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (_controls.BackPressed)
            {
                Close();
                return;
            }
            DateTime now = _game.Clock.Now;
            _tier.text = $"Meridian Rewards: {_game.Casino.Rewards.Tier}" + (Discount > 0m ? $" · {Discount:P0} off rooms" : "");
            _stay.text = Hotel.Active(now) ? $"Your room: {Hotel.NameOf(Hotel.Stay.Class)} ({Hotel.Stay.Room}) · check-out {Hotel.Stay.CheckOut:ddd MMM d, h tt}" : "No room booked.";
            decimal price = Hotel.Quote(_choice, now, _nights, Discount);
            _quote.text = $"{Hotel.NameOf(_choice)} · {_nights} night{(_nights > 1 ? "s" : "")} from tonight = {CasinoMoney.Cents(price)}  (Fri/Sat +20%)";
            foreach (var r in _rooms) r.Value.Root.style.backgroundColor = r.Key == _choice ? CasinoUi.Primary : CasinoUi.Button;
            _pills.RefreshAll();
        }

        private void Build(VisualElement root)
        {
            _panel = CasinoUi.Frame(root, "hotel-panel", 520f);
            PhoneKit.Absolute(_panel, top: 90f);
            _panel.style.left = new Length(50f, LengthUnit.Percent);
            _panel.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f);
            VisualElement head = PhoneKit.Row(_panel);
            CasinoUi.Heading(head, "THE MERIDIAN HOTEL · FRONT DESK");
            new CasinoUi.Pill(head, "hotel-close", "Close (Esc)", Close);
            _tier = PhoneKit.Label(_panel, "", 12f, PhoneKit.Muted);
            _stay = PhoneKit.Label(_panel, "", 15f, Color.white, true);
            _stay.name = "hotel-stay";
            _stay.style.marginTop = 6f;
            VisualElement rooms = CasinoUi.Wrap(_panel);
            foreach (RoomClass c in new[] { RoomClass.Standard, RoomClass.Suite, RoomClass.LuxurySuite, RoomClass.Penthouse })
            {
                RoomClass rc = c;
                _rooms[rc] = new CasinoUi.Pill(rooms, "hotel-room-" + rc.ToString().ToLowerInvariant(), $"{Hotel.NameOf(rc)} {CasinoMoney.Whole(Hotel.Rate(rc))}", () => Choose(rc), size: 13f);
            }
            VisualElement nights = CasinoUi.Wrap(_panel);
            _pills.Add(new CasinoUi.Pill(nights, "hotel-nights-down", "− night", () => SetNights(_nights - 1), () => _nights > 1));
            _pills.Add(new CasinoUi.Pill(nights, "hotel-nights-up", "+ night", () => SetNights(_nights + 1), () => _nights < Hotel.MaxNights));
            _quote = PhoneKit.Label(_panel, "", 14f, CasinoUi.Gold, true);
            _quote.name = "hotel-quote";
            _quote.style.marginTop = 4f;
            VisualElement actions = CasinoUi.Wrap(_panel);
            _pills.Add(new CasinoUi.Pill(actions, "hotel-book", "Book & check in", () => Book(), () => !Hotel.Active(_game.Clock.Now), primary: true));
            _pills.Add(new CasinoUi.Pill(actions, "hotel-extend", "Extend a night", () => Extend(), () => Hotel.Active(_game.Clock.Now)));
            _pills.Add(new CasinoUi.Pill(actions, "hotel-checkout", "Check out", CheckOut, () => Hotel.Active(_game.Clock.Now)));
            _status = PhoneKit.Label(_panel, "", 14f, Color.white);
            _status.name = "hotel-status";
            _status.style.marginTop = 6f;
        }
    }

    /// <summary>A hotel bed: sleep here (after the evening, like at home) and wake in the room.</summary>
    public sealed class HotelBed : Interactable
    {
        private GameBootstrap _game;
        private string _room;
        private Transform _wake;
        private SleepController _sleep;

        public void Configure(GameBootstrap game, string room, Transform wake)
        {
            _game = game;
            _room = room;
            _wake = wake;
        }

        private SleepController Sleep => _sleep != null ? _sleep : _sleep = FindAnyObjectByType<SleepController>();

        public override string Prompt => !_game.Casino.Hotel.HasKey(_room, _game.Clock.Now) ? "Bed (not your room)"
            : Sleep != null && Sleep.CanSleep ? "Sleep" : "Sleep (after 4 PM)";

        public override bool CanInteract => base.CanInteract && _game.Casino.Hotel.HasKey(_room, _game.Clock.Now) && Sleep != null && Sleep.CanSleep;

        public override void Interact() => Sleep.SleepAt(_wake.position, _wake.eulerAngles.y);
    }

    /// <summary>Room fittings that do something small: a hot shower (sobers you a little), the wardrobe.</summary>
    public sealed class RoomFitting : Interactable
    {
        private string _prompt;
        private Action _use;
        private Func<bool> _usable;

        public void Configure(string prompt, Action use, Func<bool> usable = null)
        {
            _prompt = prompt;
            _use = use;
            _usable = usable;
        }

        public override string Prompt => _prompt;
        public override bool CanInteract => base.CanInteract && (_usable == null || _usable());
        public override void Interact() => _use();
    }

    /// <summary>
    /// Room service (CASINO_SPEC §50): ordered by the room phone, it arrives about twenty game minutes later as a
    /// tray on the table, with a knock.
    /// </summary>
    public sealed class RoomService : MonoBehaviour
    {
        private GameBootstrap _game;
        private InteractionHud _hud;
        private GameObject _tray;
        private DateTime? _due;
        private string _item;

        public void Configure(GameBootstrap game, InteractionHud hud, GameObject tray)
        {
            _game = game;
            _hud = hud;
            _tray = tray;
            _tray.SetActive(false);
        }

        public void Ordered(CasinoMenuItem item)
        {
            _due = _game.Clock.Now.AddMinutes(20);
            _item = item.Name;
            _tray.SetActive(false);
        }

        private void Update()
        {
            if (_due is not { } due || _game.Clock.Now < due) return;
            _due = null;
            _tray.SetActive(true);
            CasinoAudio.Play(CasinoArt.ReelStop(), _tray.transform.position, 0.8f); // a knock
            _hud.ShowToast($"Room service: {_item} is on the table.", 4f);
        }
    }
}
