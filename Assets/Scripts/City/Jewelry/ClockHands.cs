using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>A wall clock's hands, keeping the game's time (the face looks along -z; 12 is +y).</summary>
    public sealed class ClockHands : MonoBehaviour
    {
        private CityContext _c;
        private Transform _hour, _minute;

        public void Configure(CityContext c, Transform hour, Transform minute)
        {
            _c = c;
            _hour = hour;
            _minute = minute;
        }

        private void Update()
        {
            if (_c?.Game?.Clock == null || _hour == null) return;
            System.DateTime now = _c.Game.Clock.Now;
            float minutes = now.Minute + now.Second / 60f;
            float hours = now.Hour % 12 + minutes / 60f;
            // Seen from the front (-z), clockwise is a negative turn about z.
            _hour.localRotation = Quaternion.Euler(0f, 0f, -hours * 30f);
            _minute.localRotation = Quaternion.Euler(0f, 0f, -minutes * 6f);
        }
    }
}
