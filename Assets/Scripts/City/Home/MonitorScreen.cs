using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpeningBell.Home;
using OpeningBell.Market;
using OpeningBell.Trading;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// What a placed monitor shows, drawn from the live simulation: a symbol's 1-minute chart, the watchlist, your
    /// positions, the news, the market's movers, or nothing. Charts are painted into a small texture from a shared
    /// pool; lists are a text overlay. Screens only redraw when seen: four times a second up close (so prices visibly
    /// tick), less often further away, never beyond <see cref="FarDistance"/>. A chart redraw is one 256×160 fill,
    /// so a room of thirty screens stays well under a millisecond a frame.
    /// </summary>
    public sealed class MonitorScreen : MonoBehaviour
    {
        public const int Width = 256, Height = 160;
        public const float FarDistance = 30f;
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        // Pooled textures: every screen that's on and in use holds one; switched-off screens hand theirs back.
        private static readonly Stack<Texture2D> Pool = new Stack<Texture2D>();
        public static int Live { get; private set; }
        /// <summary>Redraws across all screens, for the performance test.</summary>
        public static int Redraws { get; private set; }

        private HomeWorld _w;
        private ItemView _view;
        private Renderer _screen;
        private Transform _display;
        private TextMesh _text;
        private Material _material;
        private Texture2D _texture;
        /// <summary>The screen's size as built (landscape): a monitor's panel or a laptop's lid.</summary>
        private Vector2 _size;
        private Color32[] _pixels;
        private float _next;
        private string _drawnKey;

        public string Shown { get; private set; } = "";

        public void Configure(HomeWorld world, ItemView view)
        {
            _w = world;
            _view = view;
            Transform screen = null;
            foreach (Transform t in GetComponentsInChildren<Transform>(true)) if (t.name == HomeModels.ScreenName) screen = t;
            if (screen == null) return;
            _screen = screen.GetComponent<Renderer>();
            _size = new Vector2(screen.localScale.x, screen.localScale.y);
            // The display stays upright when the bezel turns for portrait.
            _display = Kit.Group(screen.parent.parent, "Display", screen.parent.localPosition);
            screen.SetParent(_display, true);
            screen.localPosition = new Vector3(0f, 0f, -0.017f);
            screen.localRotation = Quaternion.identity;
            _material = new Material(_screen.sharedMaterial);
            _screen.sharedMaterial = _material;
            _text = world.City.Kit.Text(_display, "", new Vector3(0f, 0f, -0.02f), 0f, 0.018f, new Color(0.85f, 0.9f, 0.95f), TextAnchor.UpperLeft);
            _text.alignment = TextAlignment.Left;
            _text.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void OnDestroy()
        {
            GiveBack();
            if (_material != null) Destroy(_material);
        }

        private void GiveBack()
        {
            if (_texture == null) return;
            Pool.Push(_texture);
            _texture = null;
            Live--;
        }

        private Texture2D Take()
        {
            Live++;
            if (Pool.Count > 0) return Pool.Pop();
            return new Texture2D(Width, Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Monitor" };
        }

        private void Update()
        {
            if (_screen == null || _view == null || _view.Item == null) return;
            OwnedItem i = _view.Item;
            // Portrait: the screen area turns with the bezel.
            Vector2 size = i.Portrait ? new Vector2(_size.y, _size.x) : _size;
            _screen.transform.localScale = new Vector3(size.x, size.y, 0.002f);
            _text.transform.localPosition = new Vector3(-size.x / 2f + 0.015f, size.y / 2f - 0.012f, -0.02f);
            _text.transform.localScale = Vector3.one * (Mathf.Min(size.x, size.y) / 0.36f);

            bool on = i.Power && i.State != ItemState.Carried;
            if (!on)
            {
                if (_drawnKey != "off") { GiveBack(); _material.mainTexture = null; _material.color = new Color(0.02f, 0.02f, 0.03f); _text.text = ""; _drawnKey = "off"; Shown = ""; }
                return;
            }
            if (Time.time < _next || !_screen.isVisible) return;
            Camera cam = Camera.main;
            float d = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 0f;
            if (d > FarDistance) { _next = Time.time + 2f; return; }
            _next = Time.time + (d < 6f ? 0.25f : d < 15f ? 1f : 4f);
            Draw(i);
        }

        /// <summary>Draws the current view now (tests call it directly).</summary>
        public void Draw(OwnedItem i)
        {
            Redraws++;
            MarketSimulation market = _w.Game.Market;
            if (string.IsNullOrEmpty(i.Symbol) && market.Securities.Count > 0) i.Symbol = market.Securities[0].Ticker;
            _drawnKey = i.View.ToString();
            _material.color = Color.white;
            switch (i.View)
            {
                case MonitorView.Chart:
                    EnsureTexture();
                    Chart(market, i.Symbol);
                    break;
                case MonitorView.Blank:
                    EnsureTexture();
                    Fill(0, 0, Width, Height, new Color32(18, 22, 30, 255));
                    Apply();
                    _text.text = "";
                    Shown = "blank";
                    break;
                default:
                    GiveBack();
                    _material.mainTexture = null;
                    _material.color = new Color(0.03f, 0.04f, 0.06f);
                    _text.text = Shown = TextFor(i.View, market);
                    break;
            }
        }

        private void EnsureTexture()
        {
            if (_texture == null) _texture = Take();
            if (_pixels == null) _pixels = new Color32[Width * Height];
            _material.mainTexture = _texture;
        }

        private void Apply()
        {
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        private void Fill(int x0, int y0, int x1, int y1, Color32 c)
        {
            x0 = Mathf.Clamp(x0, 0, Width); x1 = Mathf.Clamp(x1, 0, Width);
            y0 = Mathf.Clamp(y0, 0, Height); y1 = Mathf.Clamp(y1, 0, Height);
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) _pixels[y * Width + x] = c;
        }

        /// <summary>
        /// The last 48 one-minute candles, scaled to the screen, with the last price as a line. One-minute candles so
        /// the chart moves while you walk about (at 30× game speed a new candle every two seconds).
        /// </summary>
        private void Chart(MarketSimulation market, string symbol)
        {
            Fill(0, 0, Width, Height, new Color32(12, 15, 22, 255));
            SecurityRuntimeState sec = null;
            foreach (SecurityRuntimeState s in market.Securities) if (s.Ticker == symbol) sec = s;
            if (sec == null) { Apply(); _text.text = Shown = symbol + "\nno data"; return; }
            CandleSeries series = sec.Candles.Get(Timeframe.Minute1);
            int n = Mathf.Min(48, series.Count), first = series.Count - n;
            decimal lo = decimal.MaxValue, hi = decimal.MinValue;
            for (int k = first; k < series.Count; k++) { lo = System.Math.Min(lo, series[k].Low); hi = System.Math.Max(hi, series[k].High); }
            const int top = 128, bottom = 8;
            // Grid.
            for (int g = 1; g < 4; g++) Fill(0, bottom + g * (top - bottom) / 4, Width, bottom + g * (top - bottom) / 4 + 1, new Color32(30, 36, 48, 255));
            if (n > 0 && hi > lo)
            {
                float step = (Width - 8f) / 48f;
                int Y(decimal p) => bottom + Mathf.RoundToInt((float)((p - lo) / (hi - lo)) * (top - bottom));
                for (int k = 0; k < n; k++)
                {
                    Candle c = series[first + k];
                    bool up = c.Close >= c.Open;
                    var col = up ? new Color32(60, 200, 120, 255) : new Color32(230, 80, 70, 255);
                    int x = 4 + Mathf.RoundToInt(k * step);
                    int w = Mathf.Max(1, Mathf.RoundToInt(step * 0.6f));
                    Fill(x + w / 2, Y(c.Low), x + w / 2 + 1, Y(c.High) + 1, col);
                    int a = Y(c.Open), b = Y(c.Close);
                    Fill(x, Mathf.Min(a, b), x + w, Mathf.Max(a, b) + 1, col);
                }
                int last = Y(sec.Last);
                Fill(0, last, Width, last + 1, new Color32(240, 200, 80, 255));
            }
            Apply();
            _text.text = Shown = $"{symbol}  {sec.Last.ToString("0.00", C)}  {Signed(sec.ChangePercent)}%" + SessionTag(market);
        }

        private string TextFor(MonitorView view, MarketSimulation market)
        {
            var sb = new StringBuilder();
            switch (view)
            {
                case MonitorView.Watchlist:
                    sb.AppendLine("WATCHLIST" + SessionTag(market));
                    foreach (SecurityRuntimeState s in market.Securities)
                        sb.AppendLine($"{s.Ticker,-6}{s.Last.ToString("0.00", C),9}  {Signed(s.ChangePercent),6}%");
                    break;
                case MonitorView.Portfolio:
                    Account a = _w.Game.Account;
                    sb.AppendLine($"EQUITY {a.Equity.ToString("N2", C)}");
                    sb.AppendLine($"DAY {Signed(a.DailyPnL)}  CASH {a.Cash.ToString("N0", C)}");
                    foreach (Position p in a.Portfolio.Positions)
                        if (p.Quantity != 0) sb.AppendLine($"{p.Ticker,-6}{p.Quantity,6} @ {p.AveragePrice.ToString("0.00", C)}");
                    if (a.Portfolio.Positions.Count == 0) sb.AppendLine("No positions");
                    break;
                case MonitorView.News:
                    sb.AppendLine("NEWS");
                    var news = market.News;
                    for (int k = news.Count - 1, shown = 0; k >= 0 && shown < 6; k--, shown++)
                        sb.AppendLine(Clip(news[k].Headline, 34));
                    if (news.Count == 0) sb.AppendLine("Quiet so far");
                    break;
                case MonitorView.Market:
                    sb.AppendLine("MOVERS" + SessionTag(market));
                    var list = new List<SecurityRuntimeState>(market.Securities);
                    list.Sort((x, y) => y.ChangePercent.CompareTo(x.ChangePercent));
                    foreach (SecurityRuntimeState s in list) sb.AppendLine($"{s.Ticker,-6}{Signed(s.ChangePercent),7}%");
                    break;
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Outside regular hours prices barely move (or not at all), so the screen says why.</summary>
        private static string SessionTag(MarketSimulation market) =>
            market.Session switch
            {
                MarketSession.Closed => "  CLOSED",
                MarketSession.Premarket => "  PRE-MARKET",
                MarketSession.AfterHours => "  AFTER HOURS",
                _ => "",
            };

        private static string Signed(decimal d) => (d >= 0 ? "+" : "") + d.ToString("0.00", C);
        private static string Clip(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";
    }
}
