using System;
using System.Collections.Generic;
using OpeningBell.Casino;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>Something on a menu: price, what it's booked as, and for drinks how many standard drinks it is.</summary>
    public sealed class CasinoMenuItem
    {
        public string Name;
        public decimal Price;
        public SpendCategory Category = SpendCategory.Drinks;
        /// <summary>Standard drinks of alcohol (0 = soft).</summary>
        public double Alcohol;
        /// <summary>Food and coffee help a little (standard drinks cleared).</summary>
        public double Sobers;

        public bool Soft => Alcohol <= 0;

        public static CasinoMenuItem Drink(string name, decimal price, double alcohol) => new CasinoMenuItem { Name = name, Price = price, Alcohol = alcohol };
        public static CasinoMenuItem SoftDrink(string name, decimal price, double sobers = 0) => new CasinoMenuItem { Name = name, Price = price, Sobers = sobers };
        public static CasinoMenuItem Food(string name, decimal price, double sobers = 0.5) => new CasinoMenuItem { Name = name, Price = price, Category = SpendCategory.Food, Sobers = sobers };
    }

    /// <summary>The resort's menus (CASINO_SPEC §45, §47, §50, §91). Paid from the bank, never with chips.</summary>
    public static class CasinoMenus
    {
        public static readonly CasinoMenuItem[] TideBar =
        {
            CasinoMenuItem.Drink("Lager", 7m, 1), CasinoMenuItem.Drink("Glass of red", 12m, 1.2), CasinoMenuItem.Drink("Meridian Old Fashioned", 14m, 1.5),
            CasinoMenuItem.Drink("Dry martini", 15m, 1.5), CasinoMenuItem.Drink("Glass of champagne", 18m, 1.2),
            CasinoMenuItem.SoftDrink("Club soda", 3m), CasinoMenuItem.SoftDrink("Espresso", 4m, 0.2), CasinoMenuItem.SoftDrink("Citrus mocktail", 8m), CasinoMenuItem.SoftDrink("Water", 0m, 0.3),
        };

        public static readonly CasinoMenuItem[] VipBar =
        {
            CasinoMenuItem.Drink("Single malt, 18 years", 32m, 1.2), CasinoMenuItem.Drink("Vintage champagne", 45m, 1.2), CasinoMenuItem.Drink("Reserve Manhattan", 28m, 1.5),
            CasinoMenuItem.SoftDrink("Sparkling water", 0m, 0.3), CasinoMenuItem.SoftDrink("Cold brew", 7m, 0.2),
        };

        public static readonly CasinoMenuItem[] Steakhouse =
        {
            CasinoMenuItem.Food("Dry-aged ribeye", 58m), CasinoMenuItem.Food("Filet mignon", 64m), CasinoMenuItem.Food("Pan-seared sea bass", 44m),
            CasinoMenuItem.Food("Wedge salad", 16m, 0.3), CasinoMenuItem.Food("Truffle fries", 12m, 0.3), CasinoMenuItem.Food("Crème brûlée", 14m, 0.2),
            CasinoMenuItem.Drink("House red", 16m, 1.2), CasinoMenuItem.SoftDrink("Sparkling water", 5m, 0.3),
        };

        public static readonly CasinoMenuItem[] RoomService =
        {
            CasinoMenuItem.Food("Club sandwich", 24m), CasinoMenuItem.Food("Meridian burger", 26m), CasinoMenuItem.Food("Steak frites", 48m), CasinoMenuItem.Food("Breakfast for one", 32m),
            CasinoMenuItem.SoftDrink("Pot of coffee", 12m, 0.4), CasinoMenuItem.Drink("Bottle of champagne", 120m, 3),
        };

        public static readonly CasinoMenuItem[] Cafe =
        {
            CasinoMenuItem.SoftDrink("Drip coffee", 4.5m, 0.3), CasinoMenuItem.SoftDrink("Oat latte", 5.5m, 0.3), CasinoMenuItem.Food("Almond croissant", 4.75m, 0.2),
            CasinoMenuItem.Food("Egg sandwich", 9m, 0.4),
        };
    }

    /// <summary>A place to order from: which menu, whose staff serves it, whether it's open, what's comped.</summary>
    public sealed class MenuVenue
    {
        public string Name, Where;
        public CasinoMenuItem[] Items;
        public StaffNpc Staff;
        public Hours? Open;
        /// <summary>The VIP bar's drinks are on the house for Platinum members.</summary>
        public bool VipBar;
        /// <summary>Everything's included (the penthouse bar).</summary>
        public bool Complimentary;
        /// <summary>Room service arrives later, on a tray.</summary>
        public Action<CasinoMenuItem> Delivered;
    }

    /// <summary>
    /// Ordering at a bar, a restaurant table, the café or by the room phone: a small menu panel with prices, rewards
    /// points to pay with, and the staff's refusal once you've had too much (§46). Soft drinks are free for Gold members.
    /// </summary>
    public sealed class CasinoMenu : MonoBehaviour
    {
        private static CasinoMenu _instance;
        private GameBootstrap _game;
        private InteractionHud _hud;
        private CasinoControls _controls;
        private MenuVenue _venue;
        private bool _usePoints, _tookControls;

        private VisualElement _panel, _list;
        private Label _title, _info, _status;

        public static CasinoMenu Instance => _instance;
        public bool IsOpen => _panel != null && _panel.style.display == DisplayStyle.Flex;
        public string Status => _status?.text;

        public static CasinoMenu Build(CityContext c, Transform parent)
        {
            var go = new GameObject("Casino menus");
            go.transform.SetParent(parent, false);
            _instance = go.AddComponent<CasinoMenu>();
            _instance._game = c.Game;
            _instance._hud = c.Hud;
            _instance._controls = new CasinoControls(c.Player);
            return _instance;
        }

        public bool IsOpenNow(MenuVenue v) => !v.Open.HasValue || v.Open.Value.Contains(_game.Clock.Now);

        /// <summary>Opens the menu. <paramref name="seated"/>: the player's controls are already taken (sitting down).</summary>
        public void Show(MenuVenue venue, bool seated = false)
        {
            if (_panel == null)
            {
                if (_hud.Root == null) return;
                Build(_hud.Root);
            }
            _venue = venue;
            _tookControls = !seated;
            if (_tookControls) _controls.Take();
            _title.text = venue.Name.ToUpperInvariant();
            _status.text = "";
            Fill();
            _panel.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            if (!IsOpen) return;
            _panel.style.display = DisplayStyle.None;
            if (_tookControls) _controls.Release();
        }

        private bool Free(CasinoMenuItem item)
        {
            Rewards r = _game.Casino.Rewards;
            return item.Price == 0m || _venue.Complimentary || (item.Soft && r.FreeSoftDrinks) || (_venue.VipBar && r.FreeVipBar);
        }

        /// <summary>Orders an item. Null on success, else why not.</summary>
        public string Order(CasinoMenuItem item)
        {
            CasinoFloor floor = _game.Casino;
            DateTime now = _game.Clock.Now;
            if (!IsOpenNow(_venue)) return Say($"{_venue.Name} is closed right now.");
            floor.Drinks.Update(now);
            if (!item.Soft && floor.Drinks.Refused)
                return Say("I think you've had enough for tonight. Water's on the house.", staff: true);
            decimal price = Free(item) ? 0m : item.Price;
            string error = floor.Spend(item.Category, price, item.Name, now, (a, w) => _game.Economy.Spend(a, w, now), _usePoints);
            if (error != null) return Say(error);
            if (!item.Soft) floor.Drinks.Drink(item.Alcohol, now);
            if (item.Sobers > 0) floor.Drinks.Recover(item.Sobers, now);
            if (_venue.Delivered != null)
            {
                _venue.Delivered(item);
                return Say($"{item.Name}: on its way up, about twenty minutes.");
            }
            string line = item.Category == SpendCategory.Food ? "Coming right up." : item.Soft ? "Here you go." : floor.Drinks.Level == Sobriety.Drunk ? "Last one for a while, maybe." : "Enjoy.";
            _hud.ShowToast($"{item.Name}  {(price > 0m ? "-" + CasinoMoney.Cents(price) : "on the house")}", 3f);
            return Say(line, staff: true, ok: true);
        }

        private string Say(string line, bool staff = false, bool ok = false)
        {
            if (_status != null) _status.text = line;
            if (staff && _venue?.Staff != null && _venue.Staff.AtStation) _venue.Staff.Say(line);
            return ok ? null : line;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (_tookControls && _controls.BackPressed) Close();
            CasinoFloor floor = _game.Casino;
            floor.Drinks.Update(_game.Clock.Now);
            string sober = floor.Drinks.Level switch { Sobriety.Tipsy => " · feeling it a little", Sobriety.Drunk => " · you're drunk", Sobriety.Wasted => " · you're very drunk", _ => "" };
            _info.text = $"Bank {CasinoMoney.Cents(_game.Economy.Bank.Balance)} · points {floor.Rewards.Points:N0} ({CasinoMoney.Cents(floor.Rewards.PointsValue)}){sober}";
        }

        private void Build(VisualElement root)
        {
            _panel = CasinoUi.Frame(root, "menu-panel", 420f);
            PhoneKit.Absolute(_panel, top: 90f);
            _panel.style.left = new Length(50f, LengthUnit.Percent);
            _panel.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f);
            VisualElement head = PhoneKit.Row(_panel);
            _title = CasinoUi.Heading(head, "");
            new CasinoUi.Pill(head, "menu-close", "Close (Esc)", Close);
            _info = PhoneKit.Label(_panel, "", 12f, PhoneKit.Muted);
            _info.style.marginTop = 4f;
            _list = PhoneKit.Box(_panel, "menu-items");
            _list.style.marginTop = 6f;
            VisualElement row = CasinoUi.Wrap(_panel);
            var points = new Toggle("Pay with rewards points") { name = "menu-use-points" };
            points.labelElement.style.color = PhoneKit.Text;
            points.RegisterValueChangedCallback(e => _usePoints = e.newValue);
            row.Add(points);
            _status = PhoneKit.Label(_panel, "", 14f, Color.white);
            _status.name = "menu-status";
            _status.style.marginTop = 6f;
        }

        private void Fill()
        {
            _list.Clear();
            foreach (CasinoMenuItem item in _venue.Items)
            {
                CasinoMenuItem it = item;
                VisualElement row = PhoneKit.Row(_list);
                row.style.marginTop = 3f;
                PhoneKit.Label(row, it.Name + (it.Soft ? "" : "  ·  alcohol"), 14f, PhoneKit.Text);
                string price = Free(it) ? "free" : CasinoMoney.Cents(it.Price);
                var p = new CasinoUi.Pill(row, "menu-order-" + it.Name.ToLowerInvariant().Replace(' ', '-').Replace(",", ""), price, () => Order(it), size: 13f);
                p.Root.style.marginTop = 0f;
            }
        }
    }

    /// <summary>A counter (bar, café) or phone you order from.</summary>
    public sealed class MenuCounter : Interactable
    {
        private MenuVenue _venue;
        private Func<string> _locked;

        public void Configure(MenuVenue venue, Func<string> locked = null)
        {
            _venue = venue;
            _locked = locked;
        }

        public MenuVenue Venue => _venue;

        public override string Prompt
        {
            get
            {
                string locked = _locked?.Invoke();
                if (locked != null) return locked;
                if (CasinoMenu.Instance != null && !CasinoMenu.Instance.IsOpenNow(_venue)) return $"{_venue.Name} (closed)";
                return $"{_venue.Where} · {_venue.Name}";
            }
        }

        public override bool CanInteract => base.CanInteract && _locked?.Invoke() == null && CasinoMenu.Instance != null && !CasinoMenu.Instance.IsOpen
                                            && CasinoMenu.Instance.IsOpenNow(_venue) && (_venue.Staff == null || _venue.Staff.AtStation);

        public override void Interact() => CasinoMenu.Instance.Show(_venue);
    }

    /// <summary>
    /// A seat to take (bar stool, restaurant chair, lounge sofa): the view settles, and where there's a menu the
    /// order panel comes up. Esc (or Stand up) gets you back on your feet.
    /// </summary>
    public sealed class CasinoChair : Interactable
    {
        private SeatView _view;
        private MenuVenue _venue;
        private string _verb;
        private Vector3 _eye, _focus, _stand;
        private float _yaw;

        public void Configure(CityContext c, string verb, Vector3 standLocal, Vector3 eyeLocal, Vector3 focusLocal, MenuVenue venue = null)
        {
            _view = new SeatView(c.Player);
            _verb = verb;
            _stand = standLocal;
            _eye = eyeLocal;
            _focus = focusLocal;
            _venue = venue;
            Vector3 look = focusLocal - standLocal;
            _yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
        }

        public bool Seated => _view.Seated;
        public override string Prompt => _venue != null && CasinoMenu.Instance != null && !CasinoMenu.Instance.IsOpenNow(_venue) ? $"{_verb} ({_venue.Name} is closed)" : _verb;
        public override bool CanInteract => base.CanInteract && !_view.Seated && !_view.Gliding;

        public override void Interact()
        {
            Transform t = transform.parent;
            _view.Sit(this, t.TransformPoint(_stand), t.eulerAngles.y + _yaw, t.TransformPoint(_eye), t.TransformPoint(_focus));
            if (_venue != null && CasinoMenu.Instance.IsOpenNow(_venue)) CasinoMenu.Instance.Show(_venue, seated: true);
        }

        private void Update()
        {
            if (_view == null || !_view.Seated || _view.Gliding) return;
            if (_view.BackPressed)
            {
                CasinoMenu.Instance?.Close();
                _view.Stand(this);
            }
        }
    }
}
