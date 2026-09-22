using System.Globalization;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Buy something small over the counter (coffee, a snack), paid by card from the bank account. Needs staff
    /// at the counter. What the item does for the player arrives with the needs/psychology systems; for now it's
    /// a real purchase and a reason to stop by.
    /// </summary>
    public sealed class ShopCounter : Interactable
    {
        private GameBootstrap _game;
        private InteractionHud _hud;
        private StaffNpc _staff;
        private string _item;
        private decimal _price;
        private string _thanks;

        public string Item => _item;
        public decimal Price => _price;

        public void Configure(GameBootstrap game, InteractionHud hud, StaffNpc staff, string item, decimal price, string thanks)
        {
            _game = game;
            _hud = hud;
            _staff = staff;
            _item = item;
            _price = price;
            _thanks = thanks;
        }

        public override string Prompt => _staff.AtStation
            ? $"Buy {_item.ToLowerInvariant()} · ${_price.ToString("N2", CultureInfo.InvariantCulture)}"
            : "Nobody at the counter";

        public override void Interact()
        {
            if (!_staff.AtStation) return;
            string error = _game.Economy.Spend(_price, _item, _game.Clock.Now);
            if (error != null)
            {
                _staff.Say("Sorry, it says declined.");
                _hud.ShowToast(error);
                return;
            }
            _staff.Say(_thanks);
            decimal bank = _game.Economy.Bank.Balance;
            _hud.ShowToast($"{_item}  -${_price.ToString("N2", CultureInfo.InvariantCulture)}   ·   Bank {(bank < 0 ? "-$" : "$")}{System.Math.Abs(bank).ToString("N2", CultureInfo.InvariantCulture)}");
        }
    }
}
