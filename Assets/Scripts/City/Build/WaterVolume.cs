using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The bay, canal and river are deep and the player can't swim: wading in past the chest puts you back on the
    /// last dry ground you stood on (a car that goes in is fished out by the tow truck, see DriveController).
    /// Remembers a safe spot every second you're on land.
    /// </summary>
    public sealed class WaterVolume : MonoBehaviour
    {
        private CityContext _c;
        private FirstPersonController _player;
        private Vector3 _safe;
        private float _safeYaw;
        private float _timer;

        public void Configure(CityContext c)
        {
            _c = c;
            _player = c.Player.GetComponent<FirstPersonController>();
            _safe = c.Player.position;
        }

        /// <summary>True when p is under water deep enough to drown in (0.9 m below the surface).</summary>
        public static bool Submerged(Vector3 p, float depth = 0.9f) =>
            TownTerrain.IsWater(p.x, p.z, out float level) && p.y < level - depth;

        private void Update()
        {
            if (_player == null || _player.Suspended || !_player.ControlEnabled) return;
            Vector3 p = _c.Player.position;
            if (Submerged(p))
            {
                _player.PlaceAt(_safe, _safeYaw);
                _c.Hud.ShowToast("Too deep to wade. You climb back out, soaked.");
                return;
            }
            _timer -= Time.deltaTime;
            if (_timer > 0f || TownTerrain.IsWater(p.x, p.z, out _)) return;
            _timer = 1f;
            _safe = p;
            _safeYaw = _c.Player.eulerAngles.y;
        }
    }
}
