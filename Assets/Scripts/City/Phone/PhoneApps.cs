using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Economy;
using OpeningBell.Market;
using OpeningBell.Trading;
using OpeningBell.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>
    /// Texts from the broker (order fills) and the bank (rent, bills, fees), rebuilt from the saved fill and
    /// transaction histories, so they're all still there after a reload.
    /// </summary>
    internal sealed class MessagesApp : PhoneScreen
    {
        public const string BrokerThread = "PennyBridge", BankThread = "Bank";
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private readonly struct Message
        {
            public readonly DateTime Time;
            public readonly string Text;

            public Message(DateTime time, string text)
            {
                Time = time;
                Text = text;
            }
        }

        public override PhoneAppId Id => PhoneAppId.Messages;

        private readonly VisualElement _threadsView, _threadView;
        private readonly ScrollView _threads, _bubbles;
        private readonly Label _threadTitle;
        private readonly Dictionary<string, DateTime> _seen = new Dictionary<string, DateTime>();
        private readonly List<Message> _scratch = new List<Message>();
        private string _open;
        private int _shownCount = -1;

        public MessagesApp(Phone phone) : base(phone)
        {
            // Anything from before the phone was built counts as read.
            _seen[BrokerThread] = _seen[BankThread] = phone.Game.Clock.Now;

            _threadsView = PhoneKit.Box(Root);
            _threadsView.style.flexGrow = 1;
            Header(_threadsView, "Messages");
            _threads = List(_threadsView);

            _threadView = PhoneKit.Box(Root);
            _threadView.style.flexGrow = 1;
            _threadView.style.display = DisplayStyle.None;
            var header = Header(_threadView, "", ShowThreads, "Messages");
            _threadTitle = header.Q<Label>("title");
            _threadTitle.style.fontSize = 20f;
            _bubbles = List(_threadView);
        }

        public override int Unread => Count(BrokerThread, true) + Count(BankThread, true);

        public static string FillText(Fill f) =>
            $"{(f.Side == OrderSide.Buy ? "Bought" : "Sold")} {Fmt.Shares(f.Quantity)} {f.Ticker} at ${Fmt.Price(f.Price)}" +
            (f.Commission > 0m ? $" (commission {Fmt.Money(f.Commission)})." : ".");

        private void Collect(string thread, List<Message> into)
        {
            into.Clear();
            if (thread == BrokerThread)
            {
                foreach (Fill f in Phone.Game.Orders.Fills) into.Add(new Message(f.Time, FillText(f)));
            }
            else
            {
                foreach (BankTransaction tx in Phone.Game.Economy.Bank.Transactions)
                    if (tx.IsNotable)
                        into.Add(new Message(tx.Time, $"{tx.Description}: {(tx.Amount < 0 ? "-" : "+")}{Fmt.Money(Math.Abs(tx.Amount))}. Balance {Fmt.Money(tx.BalanceAfter)}."));
            }
        }

        private int Count(string thread, bool unreadOnly)
        {
            Collect(thread, _scratch);
            if (!unreadOnly) return _scratch.Count;
            int n = 0;
            foreach (Message m in _scratch)
                if (m.Time > _seen[thread]) n++;
            return n;
        }

        public override void Opened()
        {
            if (_open == null) ShowThreads();
        }

        public void OpenThread(string thread)
        {
            _open = thread;
            _shownCount = -1;
            _threadsView.style.display = DisplayStyle.None;
            _threadView.style.display = DisplayStyle.Flex;
            _threadTitle.text = thread;
            Refresh();
        }

        private void ShowThreads()
        {
            _open = null;
            Invalidate();
            _threadView.style.display = DisplayStyle.None;
            _threadsView.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public override void Refresh()
        {
            DateTime now = Phone.Game.Clock.Now;
            if (_open == null)
            {
                string sig = $"{Count(BrokerThread, false)}/{Count(BrokerThread, true)}|{Count(BankThread, false)}/{Count(BankThread, true)}|{now.Date}";
                if (!Rebuild(sig)) return;
                _threads.Clear();
                foreach (string thread in new[] { BrokerThread, BankThread })
                {
                    Collect(thread, _scratch);
                    int unread = Count(thread, true);
                    var row = PhoneKit.Row(_threads.contentContainer, Justify.FlexStart);
                    row.name = "thread-" + thread;
                    row.style.paddingTop = row.style.paddingBottom = 10f;
                    row.style.borderBottomWidth = 1f;
                    row.style.borderBottomColor = PhoneKit.Separator;
                    var dot = PhoneKit.Box(row);
                    dot.style.width = dot.style.height = 10f;
                    dot.style.marginRight = 6f;
                    dot.style.backgroundColor = unread > 0 ? PhoneKit.Blue : Color.clear;
                    PhoneKit.Radius(dot, 5f);
                    PhoneKit.Avatar(row, thread, 44f, thread == BrokerThread ? PhoneKit.BrokerNavy : new Color(0.36f, 0.4f, 0.47f));
                    var col = PhoneKit.Box(row);
                    col.style.flexGrow = 1;
                    col.style.flexShrink = 1;
                    col.style.marginLeft = 10f;
                    var top = PhoneKit.Row(col);
                    PhoneKit.Label(top, thread, 16f, PhoneKit.Text, true);
                    PhoneKit.Label(top, _scratch.Count > 0 ? When(_scratch[_scratch.Count - 1].Time, now) : "", 12f, PhoneKit.Muted);
                    var preview = PhoneKit.Label(col, _scratch.Count > 0 ? _scratch[_scratch.Count - 1].Text : "No messages yet.", 13f, PhoneKit.Muted);
                    preview.style.maxHeight = 34f;
                    preview.style.overflow = Overflow.Hidden;
                    string t = thread;
                    PhoneKit.Tap(row, () => OpenThread(t));
                }
                return;
            }

            _seen[_open] = now;
            Collect(_open, _scratch);
            if (_scratch.Count == _shownCount) return;
            _shownCount = _scratch.Count;
            _bubbles.Clear();
            if (_scratch.Count == 0) PhoneKit.Label(_bubbles.contentContainer, "No messages yet.", 14f, PhoneKit.Muted);
            DateTime lastDay = DateTime.MinValue;
            // The latest 60: a long trading history would otherwise build hundreds of bubbles.
            for (int i = Math.Max(0, _scratch.Count - 60); i < _scratch.Count; i++)
            {
                Message m = _scratch[i];
                if (m.Time.Date != lastDay)
                {
                    lastDay = m.Time.Date;
                    var stamp = PhoneKit.Label(_bubbles.contentContainer, m.Time.ToString("ddd, MMM d  h:mm tt", C), 11f, PhoneKit.Muted);
                    stamp.style.unityTextAlign = TextAnchor.MiddleCenter;
                    stamp.style.marginTop = 10f;
                    stamp.style.marginBottom = 4f;
                }
                var bubble = PhoneKit.Box(_bubbles.contentContainer);
                bubble.style.alignSelf = Align.FlexStart;
                bubble.style.maxWidth = Phone.ScreenWidth * 0.78f;
                bubble.style.backgroundColor = new Color(0.23f, 0.23f, 0.25f);
                PhoneKit.Radius(bubble, 17f);
                PhoneKit.Pad(bubble, 12f, 8f);
                bubble.style.marginTop = 4f;
                PhoneKit.Label(bubble, m.Text, 15f, PhoneKit.Text);
            }
            // Newest at the bottom, in view.
            _bubbles.schedule.Execute(() => _bubbles.scrollOffset = new Vector2(0f, float.MaxValue));
        }
    }

    /// <summary>
    /// Contacts, recents and a keypad. Calls ring, connect and play the other side as a HUD subtitle; unknown
    /// numbers fail. PennyBridge support reads out the market's state and hours.
    /// </summary>
    internal sealed class CallsApp : PhoneScreen
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private sealed class Contact
        {
            public string Name, Number;
            public Func<CallsApp, string> Line;
        }

        private readonly struct Recent
        {
            public readonly string Who;
            public readonly DateTime Time;
            public readonly bool Failed;

            public Recent(string who, DateTime time, bool failed)
            {
                Who = who;
                Time = time;
                Failed = failed;
            }
        }

        public override PhoneAppId Id => PhoneAppId.Calls;

        private readonly List<Contact> _contacts;
        private readonly List<Recent> _recents = new List<Recent>();
        private readonly VisualElement _pages, _call;
        private readonly ScrollView _contactList, _recentList;
        private readonly VisualElement _keypad;
        private readonly Label _dialed, _callName, _callStatus;
        private readonly List<Label> _tabs = new List<Label>();
        private int _tab;
        private Contact _calling;
        private string _callingNumber;
        private float _callStart = -1f, _connectAt, _hangUpAt;

        public bool InCall => _callStart >= 0f;
        public string CallStatus => _callStatus.text;

        public CallsApp(Phone phone) : base(phone)
        {
            _contacts = new List<Contact>
            {
                new Contact { Name = "Mom", Number = "5550142281", Line = _ => "Hi sweetheart! I'm at the store, can't talk long. Are you eating properly? Call me later!" },
                new Contact { Name = "PennyBridge Support", Number = "5550100200", Line = a => a.SupportLine() },
                new Contact { Name = "Calder Building", Number = "5550163300", Line = _ => "Calder Building front desk. Offices on the second floor are leased through reception; come by and ask for a tour." },
                new Contact { Name = "Hillside Cycles", Number = "5550177410", Line = _ => "Hillside Cycles! Bikes, e-bikes, tune-ups. Swing by the shop and we'll get you rolling." },
                new Contact { Name = "Tidewater Fuel", Number = "5550188120", Line = _ => FindAnyTidewater() },
                new Contact { Name = "Landlord", Number = "5550129954", Line = _ => "Yeah, hi. Rent comes out of your account automatically, so just keep it topped up and we're good." },
            };

            _pages = PhoneKit.Box(Root);
            _pages.style.flexGrow = 1;

            // Contacts.
            var contacts = PhoneKit.Box(_pages, "page-contacts");
            contacts.style.flexGrow = 1;
            Header(contacts, "Contacts");
            _contactList = List(contacts);
            foreach (Contact c in _contacts)
            {
                var row = PhoneKit.Row(_contactList.contentContainer, Justify.FlexStart);
                row.name = "contact-" + c.Name;
                row.style.paddingTop = row.style.paddingBottom = 9f;
                row.style.borderBottomWidth = 1f;
                row.style.borderBottomColor = PhoneKit.Separator;
                PhoneKit.Avatar(row, c.Name, 36f, new Color(0.4f, 0.42f, 0.48f));
                var col = PhoneKit.Box(row);
                col.style.marginLeft = 10f;
                PhoneKit.Label(col, c.Name, 16f, PhoneKit.Text, true);
                PhoneKit.Label(col, Pretty(c.Number), 12f, PhoneKit.Muted);
                Contact contact = c;
                PhoneKit.Tap(row, () => Call(contact, contact.Number));
            }

            // Recents.
            var recents = PhoneKit.Box(_pages, "page-recents");
            recents.style.flexGrow = 1;
            Header(recents, "Recents");
            _recentList = List(recents);

            // Keypad.
            _keypad = PhoneKit.Box(_pages, "page-keypad");
            _keypad.style.flexGrow = 1;
            _keypad.style.alignItems = Align.Center;
            _dialed = PhoneKit.Label(_keypad, "", 30f, PhoneKit.Text);
            _dialed.name = "dialed";
            _dialed.style.marginTop = 72f;
            _dialed.style.height = 40f;
            var grid = PhoneKit.Box(_keypad);
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.width = 3 * 78f;
            grid.style.marginTop = 10f;
            foreach (string key in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "*", "0", "#" })
            {
                var cell = PhoneKit.Box(grid, "key-" + key);
                cell.style.width = cell.style.height = 64f;
                cell.style.marginLeft = cell.style.marginRight = 7f;
                cell.style.marginBottom = 10f;
                cell.style.backgroundColor = new Color(0.2f, 0.2f, 0.22f);
                PhoneKit.Radius(cell, 32f);
                cell.style.justifyContent = Justify.Center;
                var l = PhoneKit.Label(cell, key, 28f, PhoneKit.Text);
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
                string k = key;
                PhoneKit.Tap(cell, () => { if (_dialed.text.Length < 14) _dialed.text += k; });
            }
            var actions = PhoneKit.Row(_keypad, Justify.Center);
            actions.style.width = 3 * 78f;
            var dial = PhoneKit.Box(actions, "dial");
            dial.style.width = dial.style.height = 64f;
            dial.style.backgroundColor = PhoneKit.Green;
            PhoneKit.Radius(dial, 32f);
            dial.style.justifyContent = Justify.Center;
            var dialLabel = PhoneKit.Label(dial, "Call", 15f, Color.white, true);
            dialLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            PhoneKit.Tap(dial, () => Dial(_dialed.text));
            var del = PhoneKit.Label(actions, "⌫", 24f, PhoneKit.Muted);
            PhoneKit.Absolute(del, right: 10f);
            PhoneKit.Tap(del, () => { if (_dialed.text.Length > 0) _dialed.text = _dialed.text.Substring(0, _dialed.text.Length - 1); });

            // Tab bar.
            var bar = PhoneKit.Row(Root, Justify.SpaceAround);
            PhoneKit.Absolute(bar, 0f, bottom: 0f);
            bar.style.width = Phone.ScreenWidth;
            bar.style.height = 72f;
            bar.style.paddingBottom = 20f;
            bar.style.backgroundColor = new Color(0.08f, 0.08f, 0.09f, 0.96f);
            bar.style.borderTopWidth = 1f;
            bar.style.borderTopColor = PhoneKit.Separator;
            string[] names = { "Contacts", "Recents", "Keypad" };
            for (int i = 0; i < names.Length; i++)
            {
                var tab = PhoneKit.Label(bar, names[i], 13f, PhoneKit.Muted, true);
                tab.name = "tab-" + names[i];
                int index = i;
                PhoneKit.Tap(tab, () => ShowTab(index));
                _tabs.Add(tab);
            }

            // The in-call screen, over everything.
            _call = PhoneKit.Box(Root, "call");
            PhoneKit.Absolute(_call, 0f, 0f);
            _call.style.width = Phone.ScreenWidth;
            _call.style.height = Phone.ScreenHeight;
            _call.style.backgroundColor = new Color(0.1f, 0.12f, 0.16f);
            _call.style.alignItems = Align.Center;
            _call.style.display = DisplayStyle.None;
            var avatarHolder = PhoneKit.Box(_call);
            avatarHolder.style.marginTop = 90f;
            _callName = PhoneKit.Label(_call, "", 28f, PhoneKit.Text, true);
            _callName.style.marginTop = 14f;
            _callName.style.unityTextAlign = TextAnchor.MiddleCenter;
            _callStatus = PhoneKit.Label(_call, "", 16f, PhoneKit.Muted);
            _callStatus.name = "call-status";
            _callStatus.style.marginTop = 6f;
            var end = PhoneKit.Box(_call, "end-call");
            PhoneKit.Absolute(end, (Phone.ScreenWidth - 70f) / 2f, bottom: 70f);
            end.style.width = end.style.height = 70f;
            end.style.backgroundColor = PhoneKit.Red;
            PhoneKit.Radius(end, 35f);
            end.style.justifyContent = Justify.Center;
            var endLabel = PhoneKit.Label(end, "End", 16f, Color.white, true);
            endLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            PhoneKit.Tap(end, HangUp);

            ShowTab(0);
        }

        /// <summary>Tidewater answers through the fuel hands (roadside delivery when you're stranded).</summary>
        private static string FindAnyTidewater()
        {
            FuelHands hands = UnityEngine.Object.FindAnyObjectByType<FuelHands>();
            return hands != null ? hands.CallTidewater() : "Tidewater Fuel. Pull up to any pump and pay at the pump.";
        }

        private string SupportLine()
        {
            MarketSimulation m = Phone.Game.Market;
            string state = m.Session switch
            {
                MarketSession.Regular => "the market is open right now",
                MarketSession.Premarket => "we're in the pre-market session; market orders wait for the open, so use limits",
                MarketSession.AfterHours => "we're in after-hours trading; limit orders only",
                _ => "the market is closed right now",
            };
            string t(TimeSpan s) => DateTime.Today.Add(s).ToString("h:mm tt", C);
            return $"Thanks for calling PennyBridge. Just so you know, {state}. Regular hours are {t(m.Schedule.RegularOpen)} to {t(m.Schedule.RegularClose)}, " +
                   $"with extended trading from {t(m.Schedule.PremarketOpen)} to {t(m.Schedule.AfterHoursClose)}. You can trade any time from the app.";
        }

        private void ShowTab(int index)
        {
            _tab = index;
            for (int i = 0; i < _pages.childCount; i++)
                _pages[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
            for (int i = 0; i < _tabs.Count; i++)
                _tabs[i].style.color = i == index ? PhoneKit.Blue : PhoneKit.Muted;
            Invalidate();
            Refresh();
        }

        /// <summary>Dials a number: a contact's rings through; anything else fails.</summary>
        public void Dial(string number)
        {
            if (string.IsNullOrEmpty(number)) return;
            Contact match = _contacts.Find(c => c.Number == number);
            Call(match, number);
        }

        private void Call(Contact contact, string number)
        {
            if (InCall) return;
            _calling = contact;
            _callingNumber = number;
            _callStart = Time.unscaledTime;
            _connectAt = _callStart + (contact != null ? 2.2f : 1.6f);
            _hangUpAt = float.MaxValue;
            _call.style.display = DisplayStyle.Flex;
            var holder = _call[0];
            holder.Clear();
            PhoneKit.Avatar(holder, contact?.Name ?? "#", 96f, new Color(0.4f, 0.42f, 0.48f));
            _callName.text = contact?.Name ?? Pretty(number);
            _callStatus.text = "calling…";
            _recents.Insert(0, new Recent(contact?.Name ?? Pretty(number), Phone.Game.Clock.Now, contact == null));
        }

        private void HangUp()
        {
            _callStart = -1f;
            _calling = null;
            _call.style.display = DisplayStyle.None;
            _dialed.text = "";
            Refresh();
        }

        public override void Refresh()
        {
            if (InCall)
            {
                float now = Time.unscaledTime;
                if (now >= _hangUpAt) { HangUp(); return; }
                if (now >= _connectAt)
                {
                    if (_calling == null)
                    {
                        _callStatus.text = "Call failed: number not in service";
                        if (_hangUpAt == float.MaxValue) _hangUpAt = now + 1.8f;
                    }
                    else
                    {
                        if (_hangUpAt == float.MaxValue)
                        {
                            string line = _calling.Line(this);
                            float seconds = Mathf.Clamp(line.Length / 14f, 4f, 11f);
                            Phone.Hud.ShowSubtitle(_calling.Name, line, seconds);
                            _hangUpAt = now + seconds + 0.8f;
                        }
                        int s = Mathf.FloorToInt(now - _connectAt);
                        _callStatus.text = $"{s / 60}:{s % 60:00}";
                    }
                }
                return;
            }

            if (_tab == 1 && Rebuild("recents|" + _recents.Count))
            {
                _recentList.Clear();
                if (_recents.Count == 0) PhoneKit.Label(_recentList.contentContainer, "No recent calls.", 14f, PhoneKit.Muted);
                foreach (Recent r in _recents)
                {
                    var row = PhoneKit.Row(_recentList.contentContainer);
                    row.style.paddingTop = row.style.paddingBottom = 9f;
                    row.style.borderBottomWidth = 1f;
                    row.style.borderBottomColor = PhoneKit.Separator;
                    var col = PhoneKit.Box(row);
                    PhoneKit.Label(col, r.Who, 16f, r.Failed ? PhoneKit.Red : PhoneKit.Text, true);
                    PhoneKit.Label(col, r.Failed ? "Failed" : "Outgoing", 12f, PhoneKit.Muted);
                    PhoneKit.Label(row, When(r.Time, Phone.Game.Clock.Now), 12f, PhoneKit.Muted);
                }
            }
        }

        private static string Pretty(string number) =>
            number.Length == 10 ? $"({number.Substring(0, 3)}) {number.Substring(3, 3)}-{number.Substring(6)}" : number;
    }

    /// <summary>
    /// The painted town map, north up, with a you-are-here arrow and the marked places. Drag to pan, +/− to zoom,
    /// the locate button to recentre; tap a place (or pick it from the list) for its distance and direction.
    /// </summary>
    internal sealed class MapsApp : PhoneScreen
    {
        private static readonly float[] Zooms = { 0.7f, 1.1f, 1.7f, 2.6f }; // UI pixels per metre

        public override PhoneAppId Id => PhoneAppId.Maps;

        private readonly VisualElement _view, _map, _you;
        private readonly Label _selected;
        private readonly VisualElement _taxi, _teleport;
        private readonly ScrollView _places;
        private readonly List<(MapIcon Icon, string Name, Vector3 At, VisualElement Marker)> _marks =
            new List<(MapIcon, string, Vector3, VisualElement)>();
        private int _zoom = 1;
        private Vector2 _centre; // world x/z at the middle of the view
        private bool _follow = true, _dragging;
        private Vector2 _dragFrom;
        private int _selectedIndex = -1;
        private int _marksVersion = -1;
        private readonly List<(Rect Area, Label Label, string Name)> _districts = new List<(Rect, Label, string)>();

        private const float ViewHeight = Phone.ScreenHeight * 0.62f;

        public float Zoom => Zooms[_zoom];
        public string SelectedText => _selected.text;

        public MapsApp(Phone phone) : base(phone)
        {
            _view = PhoneKit.Box(Root, "map-view");
            _view.style.height = ViewHeight;
            _view.style.overflow = Overflow.Hidden;
            _view.style.backgroundColor = new Color(0.3f, 0.42f, 0.26f);

            _map = PhoneKit.Box(_view, "map");
            _map.pickingMode = PickingMode.Ignore;
            PhoneKit.Absolute(_map, 0f, 0f);
            if (phone.Map.Texture != null) _map.style.backgroundImage = new StyleBackground(phone.Map.Texture);

            // District names, shown once you've been there (under the markers).
            foreach (var (name, area) in CityPlan.Districts)
            {
                var label = PhoneKit.Label(_map, name.ToUpperInvariant(), 13f, new Color(1f, 1f, 1f, 0.85f), true);
                label.pickingMode = PickingMode.Ignore;
                label.style.position = UnityEngine.UIElements.Position.Absolute;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.width = 180f;
                label.style.letterSpacing = 2f;
                _districts.Add((area, label, name));
            }

            _you = PhoneKit.Box(_map, "you");
            _you.pickingMode = PickingMode.Ignore;
            _you.style.width = _you.style.height = 20f;
            _you.generateVisualContent += ctx =>
            {
                Painter2D p = ctx.painter2D;
                p.fillColor = PhoneKit.Blue;
                p.strokeColor = Color.white;
                p.lineWidth = 2f;
                p.BeginPath();
                p.MoveTo(new Vector2(10f, 1f));
                p.LineTo(new Vector2(18f, 18f));
                p.LineTo(new Vector2(10f, 14f));
                p.LineTo(new Vector2(2f, 18f));
                p.ClosePath();
                p.Fill();
                p.Stroke();
            };

            // Floating controls, top right under the status bar.
            var controls = PhoneKit.Box(_view);
            PhoneKit.Absolute(controls, right: 10f, top: 52f);
            controls.style.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 0.92f);
            PhoneKit.Radius(controls, 10f);
            foreach (var (text, action, name) in new (string, Action, string)[]
                     {
                         ("+", () => SetZoom(_zoom + 1), "zoom-in"), ("−", () => SetZoom(_zoom - 1), "zoom-out"), ("◎", Recentre, "locate"),
                     })
            {
                var b = PhoneKit.Label(controls, text, 20f, PhoneKit.Text, true);
                b.name = name;
                b.style.width = 40f;
                b.style.height = 38f;
                b.style.unityTextAlign = TextAnchor.MiddleCenter;
                PhoneKit.Tap(b, action);
            }

            _view.RegisterCallback<PointerDownEvent>(e =>
            {
                _dragging = true;
                _dragFrom = e.position;
                _view.CapturePointer(e.pointerId);
            });
            _view.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_dragging) return;
                Vector2 d = (Vector2)e.position - _dragFrom;
                _dragFrom = e.position;
                _follow = false;
                _centre += new Vector2(-d.x, d.y) / Zoom; // screen y runs south
                Layout();
            });
            _view.RegisterCallback<PointerUpEvent>(e =>
            {
                _dragging = false;
                _view.ReleasePointer(e.pointerId);
            });

            // Bottom sheet: the selected place, then every place by distance.
            var sheet = PhoneKit.Box(Root, "map-sheet");
            sheet.style.flexGrow = 1;
            sheet.style.backgroundColor = new Color(0.09f, 0.09f, 0.1f);
            sheet.style.borderTopLeftRadius = sheet.style.borderTopRightRadius = 18f;
            sheet.style.marginTop = -16f;
            sheet.style.paddingTop = 10f;
            var grip = PhoneKit.Box(sheet);
            grip.style.alignSelf = Align.Center;
            grip.style.width = 36f;
            grip.style.height = 5f;
            grip.style.backgroundColor = new Color(1f, 1f, 1f, 0.25f);
            PhoneKit.Radius(grip, 3f);
            _selected = PhoneKit.Label(sheet, "", 15f, PhoneKit.Text, true);
            _selected.name = "map-selected";
            _selected.style.marginLeft = _selected.style.marginRight = 16f;
            _selected.style.marginTop = 8f;
            // Fast travel to the selected place.
            _taxi = PhoneKit.Pill(sheet, "", new Color(0.95f, 0.78f, 0.15f), new Color(0.1f, 0.1f, 0.1f), () => TakeTaxi());
            _taxi.name = "map-taxi";
            _taxi.style.alignSelf = Align.FlexStart;
            _taxi.style.marginLeft = 16f;
            _taxi.style.marginTop = 6f;
            _teleport = PhoneKit.Pill(sheet, $"Teleport  ·  ${Teleport.Price:0}", new Color(0.55f, 0.35f, 0.95f), Color.white, () => TakeTeleport());
            _teleport.name = "map-teleport";
            _teleport.style.alignSelf = Align.FlexStart;
            _teleport.style.marginLeft = 16f;
            _teleport.style.marginTop = 6f;
            _places = List(sheet);
            _places.style.marginTop = 6f;
        }

        public override void Opened()
        {
            _follow = true;
            _selectedIndex = -1;
        }

        private void SetZoom(int zoom)
        {
            _zoom = Mathf.Clamp(zoom, 0, Zooms.Length - 1);
            Layout();
        }

        private void Recentre()
        {
            _follow = true;
            Layout();
        }

        private void Select(int index, bool centre)
        {
            _selectedIndex = index;
            if (centre)
            {
                _follow = false;
                _centre = new Vector2(_marks[index].At.x, _marks[index].At.z);
            }
            Refresh();
        }

        private Vector3 Me => Phone.Player.transform.position;

        private MapDiscovery Discovery => Phone.Map.Discovery;

        private MapPlace Selected
        {
            get
            {
                if (_selectedIndex < 0 || Discovery == null) return null;
                string name = _marks[_selectedIndex].Name;
                foreach (MapPlace p in Discovery.Places) if (p.Name == name) return p;
                return null;
            }
        }

        /// <summary>Teleports in front of the selected place for $250 and puts the phone away (or says why not).</summary>
        public string TakeTeleport()
        {
            MapPlace place = Selected;
            string error = place == null ? "Pick a place first." : Teleport.Go(Phone.Game, Phone.Player, Phone.City.Driver, place);
            if (error != null)
            {
                _selected.text = error;
                return error;
            }
            Phone.Close();
            return null;
        }

        /// <summary>Rides to the selected place and puts the phone away (or says why not).</summary>
        public string TakeTaxi()
        {
            MapPlace place = Selected;
            string error = place == null ? "Pick a place first." : Taxi.Ride(Phone.Game, Phone.Player, Phone.City.Driver, place);
            if (error != null)
            {
                _selected.text = error;
                return error;
            }
            Phone.Close();
            return null;
        }

        /// <summary>Markers for the places found so far, remade only when discovery moves on.</summary>
        private void SyncMarks()
        {
            MapDiscovery d = Discovery;
            if (d == null || d.Version == _marksVersion) return;
            _marksVersion = d.Version;
            foreach (var m in _marks) m.Marker.RemoveFromHierarchy();
            _marks.Clear();
            _selectedIndex = -1;
            foreach (MapPlace p in d.Places)
            {
                if (!d.Knows(p)) continue;
                var marker = PhoneKit.Box(_map, "place-" + p.Icon);
                marker.style.width = marker.style.height = 26f;
                marker.style.backgroundImage = new StyleBackground(MapIcons.Get(p.Icon));
                int index = _marks.Count;
                PhoneKit.Tap(marker, () => Select(index, false));
                _marks.Add((p.Icon, p.Name, p.At, marker));
            }
            _you.BringToFront();
            foreach (var (_, label, name) in _districts) label.style.display = d.KnowsDistrict(name) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public override void Refresh()
        {
            SyncMarks();
            Layout();
            Vector3 me = Me;
            _taxi.style.display = DisplayStyle.None;
            _teleport.style.display = _selectedIndex >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_selectedIndex >= 0)
            {
                var m = _marks[_selectedIndex];
                _selected.text = $"{m.Name}  ·  {Distance(m.At, me)} {Compass(m.At - me)}";
                float d = Vector2.Distance(new Vector2(m.At.x, m.At.z), new Vector2(me.x, me.z));
                if (d >= Taxi.Shortest)
                {
                    _taxi.style.display = DisplayStyle.Flex;
                    _taxi.Q<Label>().text = $"Taxi  ·  ${Taxi.Fare(d):0.00}  ·  {Taxi.Minutes(d)} min";
                }
            }
            else
            {
                _selected.text = Discovery != null ? $"Places  ·  {_marks.Count} of {Discovery.Places.Count} found" : "Places";
            }

            var order = new List<int>();
            for (int i = 0; i < _marks.Count; i++) order.Add(i);
            order.Sort((a, b) => (_marks[a].At - me).sqrMagnitude.CompareTo((_marks[b].At - me).sqrMagnitude));
            if (!Rebuild(string.Join(",", order))) return; // re-sorted only when the nearest-first order changes
            _places.Clear();
            foreach (int i in order)
            {
                var m = _marks[i];
                var row = PhoneKit.Row(_places.contentContainer, Justify.FlexStart);
                row.style.paddingTop = row.style.paddingBottom = 7f;
                row.style.borderBottomWidth = 1f;
                row.style.borderBottomColor = PhoneKit.Separator;
                var icon = PhoneKit.Box(row);
                icon.style.width = icon.style.height = 24f;
                icon.style.backgroundImage = new StyleBackground(MapIcons.Get(m.Icon));
                icon.style.marginRight = 10f;
                var name = PhoneKit.Label(row, m.Name, 15f, PhoneKit.Text);
                name.style.flexGrow = 1;
                var distance = PhoneKit.Label(row, "", 13f, PhoneKit.Muted);
                Vector3 at = m.At;
                Live(() => distance.text = Distance(at, Me));
                int index = i;
                PhoneKit.Tap(row, () => Select(index, true));
            }
        }

        /// <summary>Positions the map, markers and arrow for the current centre and zoom.</summary>
        private void Layout()
        {
            Texture2D tex = Phone.Map.Texture;
            if (tex == null) return;
            Vector3 me = Me;
            if (_follow) _centre = new Vector2(me.x, me.z);
            float k = Zoom / MapTexture.PixelsPerMetre; // UI px per texture px
            _map.style.width = tex.width * k;
            _map.style.height = tex.height * k;
            Vector2 c = MapTexture.ToPixel(new Vector3(_centre.x, 0f, _centre.y)) * k;
            _map.style.left = Phone.ScreenWidth / 2f - c.x;
            _map.style.top = ViewHeight / 2f + 16f - c.y;

            foreach (var m in _marks)
            {
                Vector2 p = MapTexture.ToPixel(m.At) * k;
                PhoneKit.Absolute(m.Marker, p.x - 13f, p.y - 13f);
            }
            foreach (var (area, label, _) in _districts)
            {
                Vector2 p = MapTexture.ToPixel(new Vector3(area.center.x, 0f, area.center.y)) * k;
                PhoneKit.Absolute(label, p.x - 90f, p.y - 9f);
            }
            Vector2 you = MapTexture.ToPixel(me) * k;
            PhoneKit.Absolute(_you, you.x - 10f, you.y - 10f);
            _you.style.rotate = new Rotate(Phone.Player.transform.eulerAngles.y);
        }

        private static string Distance(Vector3 at, Vector3 me)
        {
            float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(me.x, me.z));
            return d < 1000f ? $"{Mathf.RoundToInt(d / 5f) * 5} m" : $"{d / 1000f:0.0} km";
        }

        private static string Compass(Vector3 d)
        {
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            float bearing = (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 360f) % 360f;
            return names[Mathf.RoundToInt(bearing / 45f) % 8];
        }
    }

    /// <summary>The headline feed and articles, each with the live price of what it moved.</summary>
    internal sealed class NewsApp : PhoneScreen
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public override PhoneAppId Id => PhoneAppId.News;

        private readonly VisualElement _feedView, _articleView;
        private readonly ScrollView _feed, _article;
        private long _seenId;
        private NewsItem _reading;

        public NewsApp(Phone phone) : base(phone)
        {
            IReadOnlyList<NewsItem> news = phone.Game.Market.News;
            _seenId = news.Count > 0 ? news[news.Count - 1].Id : 0;

            _feedView = PhoneKit.Box(Root);
            _feedView.style.flexGrow = 1;
            var header = Header(_feedView, "News");
            var sub = PhoneKit.Label(header, "Top Stories", 15f, PhoneKit.Red, true);
            sub.style.marginTop = 2f;
            _feed = List(_feedView);

            _articleView = PhoneKit.Box(Root);
            _articleView.style.flexGrow = 1;
            _articleView.style.display = DisplayStyle.None;
            var back = PhoneKit.Label(_articleView, "‹ News", 16f, PhoneKit.Blue);
            back.name = "back";
            back.style.marginTop = 50f;
            back.style.marginLeft = 18f;
            PhoneKit.Tap(back, ShowFeed);
            _article = List(_articleView);
        }

        public NewsItem Reading => _reading;

        public override int Unread
        {
            get
            {
                IReadOnlyList<NewsItem> news = Phone.Game.Market.News;
                int n = 0;
                for (int i = news.Count - 1; i >= 0 && news[i].Id > _seenId; i--) n++;
                return n;
            }
        }

        public override void Opened()
        {
            if (_reading == null) ShowFeed();
        }

        private void ShowFeed()
        {
            _reading = null;
            Invalidate();
            _articleView.style.display = DisplayStyle.None;
            _feedView.style.display = DisplayStyle.Flex;
            Refresh();
        }

        /// <summary>Opens a story (from a notification or the feed).</summary>
        public void Read(NewsItem item)
        {
            _reading = item;
            _feedView.style.display = DisplayStyle.None;
            _articleView.style.display = DisplayStyle.Flex;
            Invalidate();
            BuildArticle();
        }

        public override void Refresh()
        {
            IReadOnlyList<NewsItem> news = Phone.Game.Market.News;
            if (news.Count > 0) _seenId = Math.Max(_seenId, news[news.Count - 1].Id);
            if (_reading != null)
            {
                BuildArticle();
                return;
            }
            if (!Rebuild("feed|" + news.Count)) return;
            _feed.Clear();
            DateTime now = Phone.Game.Clock.Now;
            if (news.Count == 0) PhoneKit.Label(_feed.contentContainer, "No stories yet today.", 14f, PhoneKit.Muted);
            for (int i = news.Count - 1; i >= Math.Max(0, news.Count - 40); i--)
            {
                NewsItem item = news[i];
                var row = PhoneKit.Box(_feed.contentContainer, "story-" + item.Id);
                row.style.paddingTop = row.style.paddingBottom = 10f;
                row.style.borderBottomWidth = 1f;
                row.style.borderBottomColor = PhoneKit.Separator;
                var meta = PhoneKit.Row(row);
                PhoneKit.Label(meta, NewsText.Tag(item), 11f, PhoneKit.Red, true);
                var ago = PhoneKit.Label(meta, "", 11f, PhoneKit.Muted);
                Live(() => ago.text = NewsText.Ago(item.Time, Phone.Game.Clock.Now));
                var headline = PhoneKit.Label(row, item.Headline, 16f, PhoneKit.Text, true);
                headline.style.marginTop = 3f;
                PhoneKit.Tap(row, () => Read(item));
            }
        }

        private void BuildArticle()
        {
            NewsItem item = _reading;
            if (!Rebuild("article|" + item.Id)) return; // the quotes below stay live
            _article.Clear();
            VisualElement body = _article.contentContainer;
            PhoneKit.Label(body, NewsText.Tag(item), 12f, PhoneKit.Red, true).style.marginTop = 6f;
            var headline = PhoneKit.Label(body, item.Headline, 23f, PhoneKit.Text, true);
            headline.name = "article-headline";
            headline.style.marginTop = 4f;
            PhoneKit.Label(body, item.Time.ToString("dddd, MMM d · h:mm tt", C), 12f, PhoneKit.Muted).style.marginTop = 6f;
            string dek = NewsText.Dek(item.Type);
            if (dek.Length > 0) PhoneKit.Label(body, dek, 15f, new Color(0.85f, 0.85f, 0.88f)).style.marginTop = 12f;

            // What it moved, live.
            PhoneKit.Label(body, "MARKET REACTION", 11f, PhoneKit.Muted, true).style.marginTop = 16f;
            var card = Card(body);
            card.style.marginTop = 6f;
            MarketSimulation market = Phone.Game.Market;
            if (item.Scope == NewsScope.Market)
            {
                MarketIndex index = market.Index;
                Quote(card, index.Ticker, index.Spec.Name, () => (index.Level, index.ChangePercent), null);
            }
            foreach (string ticker in item.Tickers)
                if (market.TryGetSecurity(ticker, out SecurityRuntimeState s))
                    Quote(card, s.Ticker, s.Spec.CompanyName, () => (s.Last, s.ChangePercent), () =>
                    {
                        Phone.Show(PhoneAppId.PennyBridge);
                        ((PennyBridgeApp)Phone.App(PhoneAppId.PennyBridge)).ShowStock(ticker);
                    });
        }

        private void Quote(VisualElement card, string ticker, string name, Func<(decimal Price, decimal ChangePercent)> quote, Action open)
        {
            var row = PhoneKit.Row(card);
            PhoneKit.Pad(row, 12f, 9f);
            row.style.borderBottomWidth = 1f;
            row.style.borderBottomColor = PhoneKit.Separator;
            var col = PhoneKit.Box(row);
            PhoneKit.Label(col, ticker, 15f, PhoneKit.Text, true);
            PhoneKit.Label(col, name, 11f, PhoneKit.Muted);
            var right = PhoneKit.Box(row);
            right.style.alignItems = Align.FlexEnd;
            var price = PhoneKit.Label(right, "", 15f, PhoneKit.Text, true);
            var change = PhoneKit.Label(right, "", 12f, PhoneKit.Muted, true);
            Live(() =>
            {
                var (p, pct) = quote();
                price.text = Fmt.Price(p);
                change.text = Fmt.Percent(pct);
                change.style.color = PhoneKit.SignColor(pct);
            });
            if (open != null) PhoneKit.Tap(row, open);
        }
    }
}
