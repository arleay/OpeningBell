using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>
    /// The casino's screens (cage, table) share a look: a dark lacquer panel with a gold edge, drawn on the HUD. While
    /// one is up the player stands still with a free cursor and Esc backs out, like the phone.
    /// </summary>
    internal static class CasinoUi
    {
        public static readonly Color Panel = new Color(0.05f, 0.04f, 0.05f, 0.94f);
        public static readonly Color Gold = new Color(0.86f, 0.7f, 0.36f);
        public static readonly Color Felt = new Color(0.07f, 0.3f, 0.2f);
        public static readonly Color Button = new Color(0.16f, 0.15f, 0.17f);
        public static readonly Color Primary = new Color(0.78f, 0.6f, 0.26f);

        public static string Money(decimal v) => (v < 0m ? "-$" : "$") + Math.Abs(v).ToString(v == Math.Floor(v) ? "N0" : "N2", CultureInfo.InvariantCulture);

        public static VisualElement Frame(VisualElement root, string name, float width)
        {
            VisualElement panel = PhoneKit.Box(root, name);
            panel.style.width = width;
            panel.style.backgroundColor = Panel;
            PhoneKit.Radius(panel, 14f);
            PhoneKit.Border(panel, 2f, Gold);
            PhoneKit.Pad(panel, 18f, 14f);
            panel.style.display = DisplayStyle.None;
            return panel;
        }

        /// <summary>A button: a pill that is dimmed and ignores clicks while <paramref name="enabled"/> is false.</summary>
        public sealed class Pill
        {
            public readonly VisualElement Root;
            public readonly Label Text;
            private readonly Func<bool> _enabled;

            public Pill(VisualElement parent, string name, string text, Action onTap, Func<bool> enabled = null, bool primary = false, float size = 15f)
            {
                _enabled = enabled;
                Root = PhoneKit.Pill(parent, text, primary ? Primary : Button, primary ? new Color(0.08f, 0.06f, 0.03f) : PhoneKit.Text, () =>
                {
                    if (Enabled) onTap();
                }, size);
                Root.name = name;
                Root.style.marginRight = 6f;
                Root.style.marginTop = 6f;
                PhoneKit.Border(Root, 1f, new Color(Gold.r, Gold.g, Gold.b, primary ? 0f : 0.35f));
                Text = Root.Q<Label>();
            }

            public bool Enabled => _enabled == null || _enabled();

            public void Refresh() => Root.style.opacity = Enabled ? 1f : 0.3f;
        }

        public static Label Heading(VisualElement parent, string text) => PhoneKit.Label(parent, text, 13f, Gold, true);

        public static VisualElement Wrap(VisualElement parent)
        {
            VisualElement row = PhoneKit.Row(parent, Justify.FlexStart);
            row.style.flexWrap = UnityEngine.UIElements.Wrap.Wrap;
            return row;
        }
    }

    /// <summary>Takes the player's controls for a casino screen and gives them back.</summary>
    internal sealed class CasinoControls
    {
        private readonly FirstPersonController _player;
        private readonly PlayerInteractor _interactor;
        private readonly GameInput _input;

        public CasinoControls(Transform player)
        {
            _player = player != null ? player.GetComponent<FirstPersonController>() : null;
            _interactor = UnityEngine.Object.FindAnyObjectByType<PlayerInteractor>();
            _input = UnityEngine.Object.FindAnyObjectByType<GameInput>();
        }

        public FirstPersonController Player => _player;
        public bool BackPressed => _input != null && _input.CloseMenu.WasPressedThisFrame();

        public void Take()
        {
            if (_player != null) _player.ControlEnabled = false; // frees the cursor
            if (_interactor != null) _interactor.enabled = false;
            _input?.UseMenuControls();
        }

        public void Release()
        {
            _input?.UsePlayerControls();
            if (_interactor != null) _interactor.enabled = true;
            if (_player != null) _player.ControlEnabled = true;
        }
    }

    internal static class CasinoUiExtensions
    {
        public static void RefreshAll(this List<CasinoUi.Pill> pills)
        {
            foreach (CasinoUi.Pill p in pills) p.Refresh();
        }
    }
}
