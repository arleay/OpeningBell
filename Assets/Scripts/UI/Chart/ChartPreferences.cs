using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.UI
{
    /// <summary>The chart's colours. Presets plus a custom copy the player edits.</summary>
    [Serializable]
    public sealed class ChartTheme
    {
        public string Name = "Dark";
        public Color Background, Grid, Up, Down, UpWick, DownWick, UpVolume, DownVolume, Crosshair, Vwap;

        public ChartTheme Copy() => (ChartTheme)MemberwiseClone();

        private static Color C(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            c.a = alpha;
            return c;
        }

        public static readonly ChartTheme[] Presets =
        {
            new ChartTheme
            {
                Name = "Dark", Background = C("#12161C"), Grid = C("#FFFFFF", 0.06f), Up = C("#34C77B"), Down = C("#E8544C"),
                UpWick = C("#34C77B"), DownWick = C("#E8544C"), UpVolume = C("#34C77B", 0.32f), DownVolume = C("#E8544C", 0.32f),
                Crosshair = C("#FFFFFF", 0.25f), Vwap = C("#E8B63C"),
            },
            new ChartTheme
            {
                Name = "Classic", Background = C("#131722"), Grid = C("#2A2E39", 0.8f), Up = C("#26A69A"), Down = C("#EF5350"),
                UpWick = C("#26A69A"), DownWick = C("#EF5350"), UpVolume = C("#26A69A", 0.35f), DownVolume = C("#EF5350", 0.35f),
                Crosshair = C("#9598A1", 0.6f), Vwap = C("#FF9800"),
            },
            new ChartTheme
            {
                Name = "Light", Background = C("#FFFFFF"), Grid = C("#E0E3EB"), Up = C("#089981"), Down = C("#F23645"),
                UpWick = C("#089981"), DownWick = C("#F23645"), UpVolume = C("#089981", 0.3f), DownVolume = C("#F23645", 0.3f),
                Crosshair = C("#131722", 0.35f), Vwap = C("#E65100"),
            },
            new ChartTheme
            {
                Name = "Midnight", Background = C("#0B1020"), Grid = C("#1C2540"), Up = C("#4FC3F7"), Down = C("#FF7043"),
                UpWick = C("#4FC3F7"), DownWick = C("#FF7043"), UpVolume = C("#4FC3F7", 0.3f), DownVolume = C("#FF7043", 0.3f),
                Crosshair = C("#FFFFFF", 0.25f), Vwap = C("#FFD54F"),
            },
            new ChartTheme
            {
                Name = "Mono", Background = C("#101010"), Grid = C("#FFFFFF", 0.05f), Up = C("#E6E6E6"), Down = C("#5A5A5A"),
                UpWick = C("#E6E6E6"), DownWick = C("#8A8A8A"), UpVolume = C("#E6E6E6", 0.25f), DownVolume = C("#8A8A8A", 0.25f),
                Crosshair = C("#FFFFFF", 0.3f), Vwap = C("#E8B63C"),
            },
        };

        /// <summary>Colours the player can pick from for candles, lines and drawings.</summary>
        public static readonly Color[] Swatches =
        {
            C("#34C77B"), C("#26A69A"), C("#089981"), C("#4FC3F7"), C("#5A9CF5"), C("#7E57C2"), C("#E040FB"),
            C("#E8544C"), C("#EF5350"), C("#FF7043"), C("#FF9800"), C("#E8B63C"), C("#FFEB3B"), C("#FFFFFF"),
            C("#9598A1"), C("#5A5A5A"), C("#131722"), C("#12161C"), C("#0B1020"), C("#000000"),
        };
    }

    /// <summary>Indicators on the chart. Saved by value: append only.</summary>
    public enum IndicatorKind
    {
        Ema,
        Sma,
        Vwap,
        Bollinger,
        Rsi,
        Macd,
        Atr,
        Volume,
    }

    [Serializable]
    public sealed class IndicatorConfig
    {
        public IndicatorKind Kind;
        /// <summary>Length (EMA/SMA/Bollinger/RSI/ATR); MACD fast.</summary>
        public int Period = 20;
        /// <summary>MACD slow; Bollinger width ×10 (20 = 2.0 standard deviations).</summary>
        public int Period2;
        /// <summary>MACD signal.</summary>
        public int Period3;
        public Color Color = Color.white;
        public float Width = 1.5f;
        public bool Visible = true;

        public bool IsOverlay => Kind == IndicatorKind.Ema || Kind == IndicatorKind.Sma || Kind == IndicatorKind.Vwap || Kind == IndicatorKind.Bollinger;

        public string Title => Kind switch
        {
            IndicatorKind.Ema => $"EMA {Period}",
            IndicatorKind.Sma => $"SMA {Period}",
            IndicatorKind.Vwap => "VWAP",
            IndicatorKind.Bollinger => $"BB {Period} {Period2 / 10.0:0.#}",
            IndicatorKind.Rsi => $"RSI {Period}",
            IndicatorKind.Macd => $"MACD {Period} {Period2} {Period3}",
            IndicatorKind.Atr => $"ATR {Period}",
            _ => "Volume",
        };

        public static IndicatorConfig Default(IndicatorKind kind) => kind switch
        {
            IndicatorKind.Ema => new IndicatorConfig { Kind = kind, Period = 9, Color = new Color(0.25f, 0.76f, 0.97f) },
            IndicatorKind.Sma => new IndicatorConfig { Kind = kind, Period = 50, Color = new Color(0.49f, 0.34f, 0.76f) },
            IndicatorKind.Vwap => new IndicatorConfig { Kind = kind, Period = 0, Color = new Color(0.91f, 0.71f, 0.24f) },
            IndicatorKind.Bollinger => new IndicatorConfig { Kind = kind, Period = 20, Period2 = 20, Color = new Color(0.35f, 0.61f, 0.96f), Width = 1f },
            IndicatorKind.Rsi => new IndicatorConfig { Kind = kind, Period = 14, Color = new Color(0.88f, 0.25f, 0.98f) },
            IndicatorKind.Macd => new IndicatorConfig { Kind = kind, Period = 12, Period2 = 26, Period3 = 9, Color = new Color(0.35f, 0.61f, 0.96f) },
            IndicatorKind.Atr => new IndicatorConfig { Kind = kind, Period = 14, Color = new Color(1f, 0.6f, 0f) },
            _ => new IndicatorConfig { Kind = IndicatorKind.Volume, Period = 0, Color = Color.gray },
        };
    }

    /// <summary>
    /// The player's chart layout: theme (or custom colours), indicators with their settings, and which overlays are
    /// on. A preference, not part of any one game, so it persists between sessions and new games (PlayerPrefs).
    /// </summary>
    [Serializable]
    public sealed class ChartPreferences
    {
        private const string Key = "OpeningBell.ChartPreferences.v1";

        public string ThemeName = "Dark";
        public bool UseCustom;
        public ChartTheme Custom = ChartTheme.Presets[0].Copy();
        public List<IndicatorConfig> Indicators = new List<IndicatorConfig>
        {
            IndicatorConfig.Default(IndicatorKind.Vwap), IndicatorConfig.Default(IndicatorKind.Volume),
        };

        // Optional auto-markings (off by default so the chart stays clean).
        public bool ShowFvg, ShowStructure, ShowSweeps, ShowEqualHighsLows, ShowLiquidity;
        /// <summary>Developer-only: the hidden market state (day type, regime, walls, stops). Never on in normal play.</summary>
        public bool ShowDebug;
        /// <summary>TEMPORARY: buy/sell signal read from the hidden market. Remove before release.</summary>
        public bool ShowSignal;

        public event Action Changed;

        public ChartTheme Theme
        {
            get
            {
                if (UseCustom && Custom != null) return Custom;
                foreach (ChartTheme t in ChartTheme.Presets)
                    if (t.Name == ThemeName) return t;
                return ChartTheme.Presets[0];
            }
        }

        public void Notify()
        {
            Save();
            Changed?.Invoke();
        }

        public static ChartPreferences Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonUtility.FromJson<ChartPreferences>(json);
                    if (loaded != null)
                    {
                        loaded.Indicators ??= new List<IndicatorConfig>();
                        loaded.Custom ??= ChartTheme.Presets[0].Copy();
                        return loaded;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Chart preferences unreadable, using defaults: " + e.Message);
            }
            return new ChartPreferences();
        }

        public void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
        }
    }
}
