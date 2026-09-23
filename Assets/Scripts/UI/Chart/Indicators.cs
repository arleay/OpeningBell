using System;
using OpeningBell.Market;

namespace OpeningBell.UI
{
    /// <summary>
    /// Indicator math over a candle series, computed for the visible window. Recursive indicators (EMA, RSI, ATR,
    /// MACD) warm up on earlier candles first, so the values on screen match what a longer calculation would give.
    /// NaN = not enough history yet.
    /// </summary>
    public static class Indicators
    {
        private static int WarmupStart(int first, int warmup) => Math.Max(0, first - warmup);

        public static void Sma(CandleSeries s, int period, int first, int count, double[] output)
        {
            double sum = 0;
            int start = WarmupStart(first, period);
            for (int i = start; i < first + count; i++)
            {
                sum += (double)s[i].Close;
                if (i - period >= start) sum -= (double)s[i - period].Close;
                if (i >= first) output[i - first] = i - start + 1 >= period ? sum / period : double.NaN;
            }
        }

        public static void Ema(CandleSeries s, int period, int first, int count, double[] output)
        {
            double k = 2.0 / (period + 1);
            double ema = double.NaN;
            int start = WarmupStart(first, period * 5);
            for (int i = start; i < first + count; i++)
            {
                double c = (double)s[i].Close;
                ema = double.IsNaN(ema) ? c : ema + k * (c - ema);
                if (i >= first) output[i - first] = i - start + 1 >= period ? ema : double.NaN;
            }
        }

        /// <summary>Middle (SMA), upper and lower bands at <paramref name="deviations"/> standard deviations.</summary>
        public static void Bollinger(CandleSeries s, int period, double deviations, int first, int count, double[] mid, double[] upper, double[] lower)
        {
            for (int i = first; i < first + count; i++)
            {
                int o = i - first;
                if (i + 1 < period)
                {
                    mid[o] = upper[o] = lower[o] = double.NaN;
                    continue;
                }
                double sum = 0, sq = 0;
                for (int j = i - period + 1; j <= i; j++)
                {
                    double c = (double)s[j].Close;
                    sum += c;
                    sq += c * c;
                }
                double mean = sum / period;
                double sd = Math.Sqrt(Math.Max(0, sq / period - mean * mean));
                mid[o] = mean;
                upper[o] = mean + deviations * sd;
                lower[o] = mean - deviations * sd;
            }
        }

        /// <summary>Wilder's RSI, 0..100.</summary>
        public static void Rsi(CandleSeries s, int period, int first, int count, double[] output)
        {
            int start = WarmupStart(first, period * 6);
            double gain = 0, loss = 0;
            int seen = 0;
            for (int i = Math.Max(1, start); i < first + count; i++)
            {
                double change = (double)(s[i].Close - s[i - 1].Close);
                double g = Math.Max(0, change), l = Math.Max(0, -change);
                seen++;
                if (seen <= period)
                {
                    gain += g / period;
                    loss += l / period;
                }
                else
                {
                    gain = (gain * (period - 1) + g) / period;
                    loss = (loss * (period - 1) + l) / period;
                }
                if (i >= first) output[i - first] = seen < period ? double.NaN : loss == 0 ? 100 : 100 - 100 / (1 + gain / loss);
            }
            if (first == 0 && count > 0) output[0] = double.NaN;
        }

        /// <summary>MACD line (fast EMA − slow EMA), its signal EMA, and the histogram.</summary>
        public static void Macd(CandleSeries s, int fast, int slow, int signal, int first, int count, double[] line, double[] sig, double[] hist)
        {
            double kf = 2.0 / (fast + 1), ks = 2.0 / (slow + 1), kg = 2.0 / (signal + 1);
            double ef = double.NaN, es = double.NaN, eg = double.NaN;
            int start = WarmupStart(first, slow * 5);
            for (int i = start; i < first + count; i++)
            {
                double c = (double)s[i].Close;
                ef = double.IsNaN(ef) ? c : ef + kf * (c - ef);
                es = double.IsNaN(es) ? c : es + ks * (c - es);
                double m = ef - es;
                eg = double.IsNaN(eg) ? m : eg + kg * (m - eg);
                if (i < first) continue;
                bool ready = i - start + 1 >= slow;
                line[i - first] = ready ? m : double.NaN;
                sig[i - first] = ready ? eg : double.NaN;
                hist[i - first] = ready ? m - eg : double.NaN;
            }
        }

        /// <summary>Wilder's average true range.</summary>
        public static void Atr(CandleSeries s, int period, int first, int count, double[] output)
        {
            int start = WarmupStart(first, period * 6);
            double atr = double.NaN;
            int seen = 0;
            for (int i = start; i < first + count; i++)
            {
                Candle c = s[i];
                double tr = (double)(c.High - c.Low);
                if (i > 0)
                {
                    double prev = (double)s[i - 1].Close;
                    tr = Math.Max(tr, Math.Max(Math.Abs((double)c.High - prev), Math.Abs((double)c.Low - prev)));
                }
                seen++;
                atr = double.IsNaN(atr) ? tr : seen <= period ? atr + (tr - atr) / seen : (atr * (period - 1) + tr) / period;
                if (i >= first) output[i - first] = seen >= period ? atr : double.NaN;
            }
        }
    }
}
