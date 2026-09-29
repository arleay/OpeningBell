using System;
using OpeningBell.Fund;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// The employee panel on the HUD, for [E] on someone in the office (FUND_SPEC §14): the same panel as their
    /// Ledgerline profile, in a window over the world. Opening it doesn't stop them working or stand them up, and game
    /// time keeps running (the header says so); Esc or Close hands the controls back.
    /// </summary>
    public sealed class EmployeeWindow
    {
        private readonly GameBootstrap _game;
        private readonly VisualElement _overlay, _card, _host;
        private readonly Label _status, _clock;
        private EmployeePanel _panel;
        private string _signature = "";
        private float _nextRefresh;

        public bool IsOpen => _panel != null;
        public Employee Employee => _panel?.Employee;
        public VisualElement Root => _overlay;

        public EmployeeWindow(GameBootstrap game, VisualElement hudRoot, StyleSheet sheet)
        {
            _game = game;
            _overlay = new VisualElement { name = "employee-window" };
            if (sheet != null) _overlay.styleSheets.Add(sheet);
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = _overlay.style.right = _overlay.style.top = _overlay.style.bottom = 0;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 0.45f);
            _overlay.style.display = DisplayStyle.None;
            hudRoot.Add(_overlay);

            _card = Ui.Box("fund-site fund-window", _overlay);
            var head = Ui.Box("fund-window-head", _card);
            Ui.Label("fund-header-brand", head, "LEDGERLINE · ON THE FLOOR");
            Ui.Box("spacer", head);
            _clock = Ui.Label("fund-muted", head, "");
            _clock.name = "employee-window-clock";
            Ui.Button("Close (Esc)", Close, "fund-btn secondary", head, "employee-window-close");
            _status = Ui.Label("firm-status", _card);
            _status.name = "employee-window-status";
            Ui.Show(_status, false);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("fund-window-scroll");
            _card.Add(scroll);
            _host = scroll.contentContainer;
        }

        public void Open(Employee e, string tab = "overview")
        {
            _host.Clear();
            Say("", false);
            _panel = new EmployeePanel(_game, e, _host, Say, tab);
            _signature = Signature();
            _overlay.style.display = DisplayStyle.Flex;
            Tick(force: true);
        }

        public void Close()
        {
            if (_panel == null) return;
            _panel = null;
            _host.Clear();
            _overlay.style.display = DisplayStyle.None;
            Closed?.Invoke();
        }

        /// <summary>Raised when the window closes (Close button or <see cref="Close"/>), so the owner can hand the controls back.</summary>
        public event Action Closed;

        /// <summary>Live numbers twice a second; a full rebuild when their situation changes (hired, fired, a new desk).</summary>
        public void Tick(bool force = false)
        {
            if (_panel == null) return;
            if (!force && Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.5f;
            Employee e = _panel.Employee;
            if (e.Activity == Activity.Former) { Close(); return; }
            string sig = Signature();
            if (sig != _signature)
            {
                _signature = sig;
                string tab = _panel.Tab;
                _host.Clear();
                _panel = new EmployeePanel(_game, e, _host, Say, tab);
            }
            _panel.Refresh();
            Ui.SetText(_clock, $"{_game.Clock.Now:ddd h:mm tt} · time keeps running while this is open");
        }

        private string Signature()
        {
            Employee e = _panel?.Employee;
            return e == null ? "" : $"{e.Contract.Version}|{e.Desk}|{e.Training.Count}|{e.CurrentTraining?.Id}|{e.ResignationNotice}|{e.RaiseRequested}";
        }

        private void Say(string text, bool error)
        {
            Ui.SetText(_status, text);
            Ui.Show(_status, text.Length > 0);
            _status.EnableInClassList("error", error);
        }
    }
}
