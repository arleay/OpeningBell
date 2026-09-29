using OpeningBell.Market;
using UnityEngine;

namespace OpeningBell.Gameplay
{
    /// <summary>
    /// Game-event sounds and apartment ambience (spec §35). Deliberately quiet: this is a trading room, not a
    /// slot machine. Cues are listed by name so tests (and later a mixer) can observe them.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        [SerializeField] private GameBootstrap game;
        [SerializeField] private SleepController sleep;
        [SerializeField] private Transform fridge;
        [SerializeField] private Transform computer;
        [SerializeField, Range(0f, 1f)] private float cueVolume = 0.7f;
        [SerializeField, Range(0f, 1f)] private float ambienceVolume = 0.25f;

        private AudioSource _cues;
        private AudioClip _bell, _fill, _news, _breaking, _bill, _mail;
        private float _lastFill = -1f;

        private readonly System.Collections.Generic.Dictionary<string, int> _counts = new System.Collections.Generic.Dictionary<string, int>();

        public string LastCue { get; private set; }

        /// <summary>How many times a cue fired (including silenced ones during sleep).</summary>
        public int Played(string cue) => _counts.TryGetValue(cue, out int n) ? n : 0;

        private void Start()
        {
            GameSettings.Apply();
            _cues = gameObject.AddComponent<AudioSource>();
            _cues.spatialBlend = 0f;
            _bell = ProceduralSounds.Bell();
            _fill = ProceduralSounds.Fill();
            _news = ProceduralSounds.NewsPing();
            _breaking = ProceduralSounds.Breaking();
            _bill = ProceduralSounds.Bill();
            _mail = ProceduralSounds.Mail();

            Loop("RoomTone", transform, ProceduralSounds.Noise("room-tone", 0.02f), ambienceVolume * 0.5f, spatial: false);
            Loop("FridgeHum", fridge, ProceduralSounds.FridgeHum(), ambienceVolume * 0.4f, spatial: true);
            Loop("PcFan", computer, ProceduralSounds.Noise("pc-fan", 0.08f), ambienceVolume * 0.6f, spatial: true);

            game.Market.SessionChanged += (previous, current) =>
            {
                // The opening and closing bells frame the regular session.
                if (current == MarketSession.Regular || previous == MarketSession.Regular) Play("bell", _bell);
            };
            game.Orders.OrderFilled += _ => OnFill();
            game.Prop.Filled += (_, __) => OnFill(); // copied trades fill several accounts at once: still one blip
            game.Market.NewsPublished += item => Play("news", item.IsMajor ? _breaking : _news);
            game.Economy.TransactionPosted += tx =>
            {
                if (tx.IsNotable) Play("bill", _bill);
            };
            game.Inbox.Received += _ => Play("mail", _mail);
        }

        private void OnFill()
        {
            // A sweep can produce several fills in one tick; one blip is enough.
            if (Time.unscaledTime - _lastFill < 0.15f) return;
            _lastFill = Time.unscaledTime;
            Play("fill", _fill);
        }

        private void Play(string cue, AudioClip clip)
        {
            LastCue = cue;
            _counts[cue] = Played(cue) + 1;
            if (sleep != null && sleep.State != SleepState.Awake) return; // the night is silent
            _cues.PlayOneShot(clip, cueVolume);
        }

        private static void Loop(string name, Transform at, AudioClip clip, float volume, bool spatial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(at, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = volume;
            source.spatialBlend = spatial ? 1f : 0f;
            source.minDistance = 0.5f;
            source.maxDistance = 6f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.Play();
        }
    }
}
