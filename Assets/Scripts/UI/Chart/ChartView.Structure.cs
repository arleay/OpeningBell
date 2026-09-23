using System;
using System.Collections.Generic;
using OpeningBell.Market;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>
    /// Optional automatic markings, each toggled in the Overlays menu (all off by default): fair value gaps (faded as
    /// they fill), BOS / CHoCH, swept highs and lows, equal highs and lows, and liquidity lines (premarket, previous
    /// day, opening range, previous week). They describe what price did; they don't predict it. Plus a developer
    /// view of the hidden market (day type, regime, resting walls and stops), only in development builds.
    /// </summary>
    public sealed partial class ChartView
    {
        private static readonly Color BullZone = new Color32(52, 199, 123, 255);
        private static readonly Color BearZone = new Color32(232, 84, 76, 255);
        private static readonly Color LiquidityColor = new Color32(224, 169, 59, 255);

        private readonly StructureResult _structure = new StructureResult();
        private SecurityRuntimeState _security;

        /// <summary>The live security (liquidity levels and the debug view read its market memory).</summary>
        public void SetSecurity(SecurityRuntimeState security) => _security = security;

        private bool AnyStructure => _prefs.ShowFvg || _prefs.ShowStructure || _prefs.ShowSweeps || _prefs.ShowEqualHighsLows;

        private void LayoutStructure()
        {
            _structure.Clear();
            if (AnyStructure) StructureDetector.Detect(_series, _first, _count, _structure);

            if (_prefs.ShowStructure)
                foreach (StructureBreak b in _structure.Breaks)
                {
                    if (b.BreakIndex < _first) continue;
                    float x0 = X(Math.Max(b.SwingIndex, _first)), x1 = X(b.BreakIndex);
                    Text((x0 + x1) / 2 - 14, Y((double)b.Price) + (b.Up ? -16 : 2), b.ChangeOfCharacter ? "CHoCH" : "BOS",
                        b.ChangeOfCharacter ? LiquidityColor : Ink);
                }
            if (_prefs.ShowEqualHighsLows)
                foreach (EqualLevel e in _structure.Equals)
                    if (e.Second >= _first)
                        Text(X(e.Second) + 4, Y((double)e.Price) + (e.High ? -16 : 2), e.High ? "EQH" : "EQL", LiquidityColor);

            if (_prefs.ShowLiquidity && _security != null)
                foreach (Level l in _security.Levels.All)
                {
                    string name = LiquidityName(l.Kind);
                    double price = Math.Exp(l.Log);
                    if (name == null || price < _min || price > _max) continue;
                    Text(_plot.xMax - 40, Y(price) - 15, name, LiquidityColor);
                }

            if (_prefs.ShowDebug && Debug.isDebugBuild && _security != null)
            {
                Text(_plot.x + 4, _plot.y + 36, $"[debug] day {_security.DayType} · regime {_security.Regime} · levels {_security.Levels.All.Count}",
                    new Color(1f, 0.4f, 1f), new Color(0, 0, 0, 0.6f));
            }
        }

        private static string LiquidityName(LevelKind k) => k switch
        {
            LevelKind.PremarketHigh => "PMH",
            LevelKind.PremarketLow => "PML",
            LevelKind.PreviousDayHigh => "PDH",
            LevelKind.PreviousDayLow => "PDL",
            LevelKind.OpeningRangeHigh => "ORH",
            LevelKind.OpeningRangeLow => "ORL",
            LevelKind.PreviousWeekHigh => "PWH",
            LevelKind.PreviousWeekLow => "PWL",
            LevelKind.PreviousClose => "PDC",
            _ => null,
        };

        /// <summary>Gap zones go behind the candles, fading as they fill.</summary>
        private void PaintStructureBehind(Painter2D p)
        {
            if (!_prefs.ShowFvg) return;
            foreach (FairValueGap g in _structure.Gaps)
            {
                if (g.Filled >= 1 && g.Index < _first) continue;
                Color c = g.Bullish ? BullZone : BearZone;
                float alpha = g.Filled >= 1 ? 0.06f : 0.2f * (float)(1 - 0.6 * g.Filled);
                p.fillColor = new Color(c.r, c.g, c.b, alpha);
                float x0 = X(Math.Max(g.Index - 1, _first - 1)), y0 = Y((double)g.High), y1 = Y((double)g.Low);
                p.BeginPath();
                PathRect(p, x0, y0, _plot.xMax - x0, Math.Max(1f, y1 - y0));
                p.Fill();
            }
        }

        private void PaintStructureFront(Painter2D p)
        {
            if (_prefs.ShowStructure)
            {
                foreach (StructureBreak b in _structure.Breaks)
                {
                    if (b.BreakIndex < _first) continue;
                    float y = Y((double)b.Price);
                    p.strokeColor = b.ChangeOfCharacter ? LiquidityColor : Ink;
                    p.lineWidth = 1f;
                    p.BeginPath();
                    for (float x = X(Math.Max(b.SwingIndex, _first)); x < X(b.BreakIndex); x += 7)
                    {
                        p.MoveTo(new Vector2(x, y));
                        p.LineTo(new Vector2(Math.Min(x + 4, X(b.BreakIndex)), y));
                    }
                    p.Stroke();
                }
            }

            if (_prefs.ShowSweeps)
                foreach (LiquiditySweep s in _structure.Sweeps)
                {
                    if (s.Index < _first) continue;
                    float x = X(s.Index), y = Y((double)s.Price) + (s.High ? -7 : 7);
                    p.strokeColor = LiquidityColor;
                    p.lineWidth = 2f;
                    p.BeginPath();
                    p.MoveTo(new Vector2(x - 4, y - 4));
                    p.LineTo(new Vector2(x + 4, y + 4));
                    p.MoveTo(new Vector2(x + 4, y - 4));
                    p.LineTo(new Vector2(x - 4, y + 4));
                    p.Stroke();
                }

            if (_prefs.ShowEqualHighsLows)
                foreach (EqualLevel e in _structure.Equals)
                {
                    if (e.Second < _first) continue;
                    float y = Y((double)e.Price);
                    p.strokeColor = LiquidityColor;
                    p.lineWidth = 1f;
                    p.BeginPath();
                    p.MoveTo(new Vector2(X(Math.Max(e.First, _first)), y));
                    p.LineTo(new Vector2(X(e.Second), y));
                    p.Stroke();
                }

            if (_prefs.ShowLiquidity && _security != null)
                foreach (Level l in _security.Levels.All)
                    if (LiquidityName(l.Kind) != null)
                        HorizontalLine(p, Math.Exp(l.Log), new Color(LiquidityColor.r, LiquidityColor.g, LiquidityColor.b, 0.55f), 1f, dashed: true);
        }

        /// <summary>Developer view: every remembered level with its resting wall (bar) and stops (tick) sizes.</summary>
        private void PaintDebug(Painter2D p)
        {
            if (!_prefs.ShowDebug || !Debug.isDebugBuild || _security == null) return;
            double sd = _security.Spec.DailyVolatility;
            foreach (Level l in _security.Levels.All)
            {
                double price = Math.Exp(l.Log);
                if (price < _min || price > _max) continue;
                float y = Y(price);
                p.strokeColor = new Color(1f, 0.4f, 1f, 0.5f);
                p.lineWidth = 1f;
                p.BeginPath();
                p.MoveTo(new Vector2(_plot.xMin, y));
                p.LineTo(new Vector2(_plot.xMax, y));
                p.Stroke();
                p.fillColor = new Color(1f, 0.4f, 1f, 0.8f);
                p.BeginPath();
                PathRect(p, _plot.xMax - 4 - (float)(l.Wall / sd * 600), y - 2, (float)(l.Wall / sd * 600), 4);
                PathRect(p, _plot.xMax - 4 - (float)(l.Stops / sd * 600), y + (l.Side > 0 ? -6 : 3), (float)(l.Stops / sd * 600), 2);
                p.Fill();
            }
        }
    }
}
