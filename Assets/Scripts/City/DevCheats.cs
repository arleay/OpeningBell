using UnityEngine;
using UnityEngine.InputSystem;

namespace OpeningBell.City
{
    /// <summary>
    /// Playtesting shortcuts, present only in development builds and the editor (CityBuilder never adds it to a
    /// release build). F9 puts $10,000 in the bank, Shift+F9 $100,000; the phone confirms with a banner.
    /// </summary>
    public sealed class DevCheats : MonoBehaviour
    {
        private GameBootstrap _game;
        private Phone _phone;

        public void Configure(GameBootstrap game, Phone phone)
        {
            _game = game;
            _phone = phone;
        }

        private void Update()
        {
            Keyboard k = Keyboard.current;
            if (_game == null || k == null) return;
            if (k.f8Key.wasPressedThisFrame)
            {
                // F8: next weather (the forecast resumes with the next six-hour spell).
                WeatherSystem weather = FindAnyObjectByType<WeatherSystem>();
                if (weather != null)
                {
                    Weather next = (Weather)(((int)weather.Current + 1) % System.Enum.GetValues(typeof(Weather)).Length);
                    WeatherSystem.Forced = next;
                    weather.Set(next);
                    _phone?.Notify(PhoneAppId.News, "Weather", next.ToString());
                }
            }
            if (!k.f9Key.wasPressedThisFrame) return;
            decimal amount = k.shiftKey.isPressed ? 100000m : 10000m;
            _game.Economy.DevDeposit(amount, _game.Clock.Now);
            if (_phone != null)
                _phone.Notify(PhoneAppId.PennyBridge, "Developer deposit", $"+${amount:N0} in the bank (now ${_game.Economy.Bank.Balance:N0})");
        }
    }
}
