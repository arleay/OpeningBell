using System;
using System.Collections.Generic;
using OpeningBell.Core;
using OpeningBell.Market;
using OpeningBell.Trading;

namespace OpeningBell.Fund
{
    /// <summary>
    /// The trading floor (FUND_SPEC §5): each employee's desk account, the order gate that enforces hard limits, the
    /// tick monitor (daily loss lockout), fills into trade records and commission, and the brain that decides trades from
    /// what's on the screen. Soft wishes and personality shape decisions; hard limits clamp them afterwards.
    /// </summary>
    public sealed partial class HedgeFund
    {
        private readonly Dictionary<string, Features> _features = new Dictionary<string, Features>();
        private long _featuresMinute = -1;

        // ------------------------------------------------------------------ desks

        /// <summary>A fresh desk account on the shared market, gated by the company's limits.</summary>
        private void Wire(Employee e)
        {
            e.Account = new Account(_market);
            e.Orders = new OrderManager(_market, e.Account, _rules) { SharedLiquidity = _liquidity };
            e.Orders.Gate = order => Gate(e, order);
            e.Orders.OrderFilled += fill => OnFill(e, fill);
        }

        /// <summary>Hard limits. Anything that only reduces a position always passes (a locked desk can still get flat).</summary>
        private string Gate(Employee e, Order order)
        {
            if (order.OcoGroup != 0) return null;
            long opening = e.Orders.OpeningQuantity(order.Ticker, order.Side, order.Quantity);
            if (opening == 0) return null;
            if (!Exists || WindingUp) return "The fund is closing: no new positions.";
            if (e.Former) return $"{e.Name} no longer works here.";
            if (!e.Policy.Authorized) return "Trading paused by management.";
            if (e.LockedToday) return "Daily loss limit reached: locked until tomorrow.";
            if (!e.Policy.Allows(order.Ticker)) return $"{order.Ticker} isn't on {e.Person.First}'s approved list.";

            long held = Math.Abs(e.Orders.PositionQuantity(order.Ticker));
            long working = 0;
            foreach (Order o in e.Orders.OpenOrders)
                if (o.OcoGroup == 0 && o.Ticker == order.Ticker && o != order) working += e.Orders.OpeningQuantity(o.Ticker, o.Side, o.RemainingQuantity);
            if (held + working + opening > e.Policy.MaxContracts)
                return $"Position limit: {e.Policy.MaxContracts} contracts in {order.Ticker}.";
            if (held == 0)
            {
                var symbols = new HashSet<string>();
                foreach (Position p in e.Account.Portfolio.Positions) if (p.IsOpen) symbols.Add(p.Ticker);
                foreach (Order o in e.Orders.OpenOrders) if (o.OcoGroup == 0 && o != order) symbols.Add(o.Ticker);
                if (symbols.Count >= e.Policy.MaxPositions) return $"Open-position limit: {e.Policy.MaxPositions}.";
            }
            (long sym, long sector) = CompanyGross(order.Ticker);
            if (sym + opening > Config.CompanyMaxPerSymbol) return $"Company limit: {Config.CompanyMaxPerSymbol} contracts across all desks in {order.Ticker}.";
            if (sector + opening > Config.CompanyMaxPerSector) return $"Company limit: {Config.CompanyMaxPerSector} contracts across all desks in one sector.";
            return null;
        }

        /// <summary>Contracts held or working to open across every desk: in <paramref name="ticker"/> and in its sector.</summary>
        public (long Symbol, long Sector) CompanyGross(string ticker)
        {
            Sector sector = SectorOf(ticker);
            long sym = 0, sec = 0;
            foreach (Employee e in _employees)
            {
                if (e.Account == null) continue;
                foreach (Position p in e.Account.Portfolio.Positions)
                {
                    if (!p.IsOpen) continue;
                    long q = Math.Abs(p.Quantity);
                    if (p.Ticker == ticker) sym += q;
                    if (SectorOf(p.Ticker) == sector) sec += q;
                }
                foreach (Order o in e.Orders.OpenOrders)
                {
                    if (o.OcoGroup != 0) continue;
                    long q = e.Orders.OpeningQuantity(o.Ticker, o.Side, o.RemainingQuantity);
                    if (o.Ticker == ticker) sym += q;
                    if (SectorOf(o.Ticker) == sector) sec += q;
                }
            }
            return (sym, sec);
        }

        /// <summary>The company's net and gross exposure per symbol, and dollars at stake for a typical day's move.</summary>
        public List<(string Ticker, Sector Sector, long Net, long Gross, decimal DayRisk)> Exposure()
        {
            var list = new List<(string, Sector, long, long, decimal)>();
            foreach (SecurityRuntimeState s in _market.Securities)
            {
                long net = 0, gross = 0;
                foreach (Employee e in _employees)
                {
                    long q = e.Account?.Portfolio.QuantityOf(s.Ticker) ?? 0;
                    net += q;
                    gross += Math.Abs(q);
                }
                if (gross == 0) continue;
                // A contract is sized so a typical day's move is worth about $500 (ContractSpec): net contracts × that.
                list.Add((s.Ticker, s.Spec.Sector, net, gross, Math.Abs(net) * (decimal)ContractSpec.DollarsPerDailyMove));
            }
            return list;
        }

        public Sector SectorOf(string ticker) => _market.TryGetSecurity(ticker, out SecurityRuntimeState s) ? s.Spec.Sector : default;

        // ------------------------------------------------------------------ ticks

        private void OnSessionChanged(MarketSession previous, MarketSession current)
        {
            if (!Exists) return;
            foreach (Employee e in _employees) e.Memory.DayStartEquity = e.Account?.Equity ?? 0m;
        }

        private void OnTick()
        {
            if (!Exists) return;
            AdvanceTo(_market.Now);
            bool regular = _market.Session == MarketSession.Regular;
            DateTime now = _market.Now;
            long mi = now.Ticks / TimeSpan.TicksPerMinute;
            foreach (Employee e in _employees)
            {
                if (e.Account == null || e.Former && e.IsFlat) continue;
                LossLimit(e, now);
                if (!regular) continue;
                bool atDesk = e.Activity == Activity.Trading && !e.Former && e.Policy.Authorized && !WindingUp && !e.LockedToday;
                if (!atDesk) DropEntries(e, now);
                if (e.Activity == Activity.Trading || e.Activity == Activity.RiskLocked) Manage(e, now, mi);
                EndOfDay(e, now);
                if (!atDesk) continue;
                Pending(e, now);
                if (e.Memory.LastScanMinute != mi)
                {
                    e.Memory.LastScanMinute = mi;
                    Scan(e, now, mi);
                }
            }
        }

        /// <summary>The daily loss lockout: checked every tick, whatever the employee is doing. Flat, cancelled, locked.</summary>
        private void LossLimit(Employee e, DateTime now)
        {
            if (e.LockedToday || e.Former || _market.Session == MarketSession.Closed) return;
            decimal day = e.Account.DailyPnL;
            if (day > -e.Policy.MaxDailyLoss) return;
            e.LockedToday = true;
            e.LockReason = $"Daily loss limit of {Money(e.Policy.MaxDailyLoss)} reached ({Money(day)}).";
            if (e.Today != null) e.Today.LossLimitHit = true;
            CloseOut(e, "Daily loss limit");
            e.Note(now, e.LockReason + " Trading stopped for the day.", limit: true);
            e.Stress = Math.Min(1, e.Stress + 0.25);
            Notify(NoticeLevel.Important, "Daily loss limit reached", $"{e.Name}: {e.LockReason} Positions closed, desk locked until tomorrow.", e.Id);
        }

        // ------------------------------------------------------------------ fills

        internal void OnFill(Employee e, Fill fill)
        {
            DateTime now = fill.Time;
            decimal net = fill.RealizedPnL - fill.Commission;
            e.NetRealized += net;
            if (e.Today != null)
            {
                e.Today.Realized += fill.RealizedPnL;
                e.Today.Fees += fill.Commission;
            }
            // Commission only on new high ground: recovering a loss earns nothing.
            if (e.NetRealized > e.HighWater)
            {
                decimal gain = e.NetRealized - e.HighWater;
                e.HighWater = e.NetRealized;
                if (e.Contract.PaysCommission)
                {
                    decimal c = gain * e.Contract.CommissionRate;
                    e.CommissionAccrued += c;
                    if (e.Today != null) e.Today.Commission += c;
                    Ledger.Recognise(now, ExpenseKind.Commission, c, "Commission: " + e.Name, e.Id);
                }
            }

            long after = e.Account.Portfolio.QuantityOf(fill.Ticker);
            e.Open.TryGetValue(fill.Ticker, out TradeRecord rec);
            int dir = fill.Side == OrderSide.Buy ? 1 : -1;
            if (rec == null && after != 0)
            {
                rec = new TradeRecord { Id = _nextTradeId++, Ticker = fill.Ticker, Side = dir, Opened = now.Ticks, Strategy = e.Person.Strategy };
                if (e.Memory.Pending.TryGetValue(fill.OrderId, out EntryPlan plan))
                {
                    rec.Strategy = plan.Strategy;
                    rec.PlannedStop = plan.Stop;
                    rec.PlannedTarget = plan.Target;
                    rec.Quality = plan.Quality;
                    rec.FollowedPlan = plan.FollowedPlan;
                    rec.Notes = plan.Notes;
                }
                else rec.Notes = "Manual";
                e.Open[fill.Ticker] = rec;
                e.Trades.Add(rec);
                if (e.Trades.Count > 400) e.Trades.RemoveAt(0);
            }
            if (rec == null) { Filled?.Invoke(e, fill); return; }
            rec.Fees += fill.Commission;
            rec.Gross += fill.RealizedPnL;
            if (dir == rec.Side)
            {
                // Adding to the position: average the entry.
                rec.EntryNotional += fill.Quantity * fill.Price;
                rec.Contracts += fill.Quantity;
                rec.Entry = rec.EntryNotional / rec.Contracts;
                if (rec.PlannedStop > 0m) rec.Risk = Math.Abs(rec.Entry - rec.PlannedStop) * rec.Contracts * e.Account.Contract(fill.Ticker).PointValue;
            }
            else
            {
                rec.ExitNotional += fill.Quantity * fill.Price;
                rec.ExitQuantity += fill.Quantity;
            }
            if (after == 0) CloseRecord(e, rec, now);
            Filled?.Invoke(e, fill);
        }

        private void CloseRecord(Employee e, TradeRecord rec, DateTime now)
        {
            rec.Closed = now.Ticks;
            rec.Exit = rec.ExitQuantity > 0 ? rec.ExitNotional / rec.ExitQuantity : rec.Entry;
            rec.Net = rec.Gross - rec.Fees;
            e.Open.Remove(rec.Ticker);
            e.Memory.TradesToday++;
            if (e.Today != null) e.Today.Trades++;
            if (rec.Net > 0m)
            {
                e.ConsecutiveWins++;
                e.ConsecutiveLosses = 0;
                if (e.Today != null) e.Today.Wins++;
                e.Stress = Math.Max(0, e.Stress - 0.05);
            }
            else if (rec.Net < 0m)
            {
                e.ConsecutiveLosses++;
                e.ConsecutiveWins = 0;
                if (e.Today != null) e.Today.Losses++;
                double hurt = 0.06 + 0.04 * Math.Min(3, e.ConsecutiveLosses);
                e.Stress = Math.Min(1, e.Stress + hurt * (1.2 - e.Person.Trait(Trait.Composure) / 100.0));
            }
            // Brackets left over (both legs are day-good-till-cancelled): nothing to protect now.
            foreach (Order o in new List<Order>(e.Orders.OpenOrders))
                if (o.Ticker == rec.Ticker && o.OcoGroup != 0) e.Orders.Cancel(o.Id);
        }

        // ------------------------------------------------------------------ the brain

        private Features FeaturesOf(string ticker, long mi)
        {
            if (_featuresMinute != mi)
            {
                _features.Clear();
                _featuresMinute = mi;
            }
            if (_features.TryGetValue(ticker, out Features f)) return f;
            f = _market.TryGetSecurity(ticker, out SecurityRuntimeState sec) ? Features.Build(_market, sec) : new Features();
            _features[ticker] = f;
            return f;
        }

        /// <summary>
        /// Once a minute at the desk: look over their symbols and strategies, judge each setup (through their own
        /// perception), and take the best one if it clears their bar. Every draw is keyed by (person, minute, candidate),
        /// so a replay decides exactly the same.
        /// </summary>
        private void Scan(Employee e, DateTime now, long mi)
        {
            Person p = e.Person;
            RiskPolicy pol = e.Policy;
            TimeSpan tod = now.TimeOfDay;
            if (tod >= _market.Schedule.RegularClose - TimeSpan.FromMinutes(25)) return;
            if (e.Memory.Pending.Count > 0 || e.Memory.Delayed.Count > 0) return;
            if (e.OpenPositionCount >= pol.MaxPositions) return;
            int minute = now.Hour * 60 + now.Minute;
            if (e.Plan != null && BreakSoon(e.Plan, minute, 8)) return;

            CounterRandom r = _rng.Sub("brain").Sub(e.Id);
            int k = 0;
            double U() => r.Double(mi, k++);
            double G() => r.Gaussian(mi, k++);

            double sc = p[Skill.SelfControl] / 100.0, timing = p[Skill.Timing] / 100.0, analysis = p[Skill.Analysis] / 100.0;
            double adapt = p[Skill.Adaptability] / 100.0, spec = p[Skill.Specialization] / 100.0, rr = p[Skill.RewardRisk] / 100.0;
            double greed = p.Trait(Trait.Greed) / 100.0;

            // How much they trade: their style, stretched by poor self-control; management's cap is a wish.
            int styleCap = (int)Math.Ceiling(p.TradesPerDay * 1.5) + 1 + (int)(3 * (1 - sc));
            if (e.Memory.TradesToday >= styleCap) return;
            if (pol.PreferredMaxTrades > 0 && e.Memory.TradesToday >= pol.PreferredMaxTrades && U() < 0.35 + 0.65 * sc) return;

            decimal day = e.Account.DailyPnL;
            bool goodDay = day > pol.MaxRiskPerTrade * 1.5m;
            double threshold = 0.5 + 0.18 * timing + (pol.PreferQuality ? 0.07 * (0.3 + 0.7 * sc) : 0);
            int cl = e.ConsecutiveLosses;
            if (cl >= 2)
            {
                // Revenge: the bar drops after losses, unless they have the discipline to hold it.
                double drop = 0.11 * (1 - sc) * Math.Min(3, cl - 1);
                threshold -= drop;
                if (drop > 0.05 && !e.Memory.FrequencyNoted)
                {
                    e.Memory.FrequencyNoted = true;
                    e.Note(now, $"Trade frequency increased after {cl} consecutive losses.");
                }
            }
            if (goodDay)
            {
                if (sc >= 0.55)
                {
                    // Disciplined: a good day is banked, not pushed.
                    if (U() < 0.5 * sc) return;
                    threshold += 0.1 * sc;
                }
                else
                {
                    threshold -= 0.07 * greed * (1 - sc);
                    if (!e.Memory.GreedNoted && greed > 0.5)
                    {
                        e.Memory.GreedNoted = true;
                        e.Note(now, $"Kept looking for trades after a good day ({Money(day)} up).");
                    }
                }
            }

            // Candidates: their approved symbols (all, if none set) and strategies (their own, plus any approved).
            var strategies = new List<Strategy> { p.Strategy };
            foreach (Strategy s in pol.Strategies) if (!strategies.Contains(s)) strategies.Add(s);
            double fatigueNoise = 1 + 0.8 * e.Fatigue + 0.6 * e.Stress;
            Setup best = default;
            Features bestF = null;
            double bestSeen = double.MinValue;
            int ci = 0;
            foreach (SecurityRuntimeState sec in _market.Securities)
            {
                if (!pol.Allows(sec.Ticker)) continue;
                Features f = FeaturesOf(sec.Ticker, mi);
                if (!f.Valid) continue;
                foreach (Strategy strat in strategies)
                {
                    ci++;
                    if (!Setups.Find(strat, f, out Setup s)) continue;
                    bool home = f.Sector == p.Specialty;
                    double noise = 0.4 * (1.05 - analysis) * fatigueNoise * (home ? 1 - 0.4 * spec : 1.0) * (strat == p.Strategy ? 1.0 : 1.25);
                    double seen = s.Quality + noise * r.Gaussian(mi, 1000 + ci);
                    // The day's character: adaptable traders notice when it doesn't suit the play.
                    bool wrongDay = (strat == Strategy.Reversion && f.Trendiness > 0.65) || ((strat == Strategy.Breakout || strat == Strategy.TrendPullback) && f.Trendiness < 0.25);
                    if (wrongDay) seen -= 0.15 * adapt;
                    if (seen > bestSeen)
                    {
                        bestSeen = seen;
                        best = s;
                        bestF = f;
                    }
                }
                // Misreads: a weak reader sometimes sees a pattern in a one-bar wiggle. The chart shows nothing there
                // (true quality near zero); how convincing it looks depends on how little they know.
                if (r.Chance(0.002 * (1 - analysis), mi, 3000 + ci) && Setups.Misread(p.Strategy, f, out Setup ghost))
                {
                    double seen = 0.4 + 0.45 * (1 - analysis) + 0.1 * r.Gaussian(mi, 4000 + ci);
                    if (seen > bestSeen) { bestSeen = seen; best = ghost; bestF = f; }
                }
            }

            // Impulse: bored after a long wait, or stung by losses, an undisciplined trader jumps on whatever is
            // moving hardest, outside their plan. A chase of an extended move (measured: those lose on average).
            bool bored = e.Memory.LastEntryTicks > 0 && now.Ticks - e.Memory.LastEntryTicks > TimeSpan.TicksPerMinute * 50
                         && now.Date == new DateTime(e.Memory.LastEntryTicks).Date;
            if ((bored || cl >= 2) && sc < 0.5 && (bestF == null || bestSeen < threshold) && r.Chance(0.04 * (1 - sc / 0.5), mi, 5000))
            {
                Features hot = null;
                foreach (SecurityRuntimeState sec in _market.Securities)
                {
                    if (!pol.Allows(sec.Ticker)) continue;
                    Features f = FeaturesOf(sec.Ticker, mi);
                    if (f.Valid && Math.Abs(f.Z5) >= 1.5 && (hot == null || Math.Abs(f.Z5) > Math.Abs(hot.Z5))) hot = f;
                }
                if (hot != null && Setups.Chase(hot, out Setup chase))
                {
                    best = chase;
                    bestF = hot;
                    bestSeen = threshold;
                    if (!e.Memory.ImpulseNoted)
                    {
                        e.Memory.ImpulseNoted = true;
                        e.Note(now, bored ? $"Grew impatient and jumped into {hot.Ticker} outside their plan." : $"Jumped into {hot.Ticker} after {cl} losses, outside their plan.");
                    }
                }
            }
            if (bestF == null || bestSeen < threshold) return;

            Setup setup = best;
            var plan = new EntryPlan { Ticker = bestF.Ticker, Strategy = setup.Strategy, Side = setup.Side, Quality = setup.Quality, FollowedPlan = true };
            if (setup.Impulsive) { plan.FollowedPlan = false; plan.Notes = "Impulsive, outside the plan"; }
            // Reward:risk: planners skip thin trades and aim further where the play allows it.
            // Scalps and fades are short-target plays by design (about 1.3R and up to 2R), so the bar is lower for them.
            bool shortTarget = setup.Strategy == Strategy.Reversion || setup.Strategy == Strategy.Scalping;
            double rrMin = shortTarget ? 0.8 + 0.4 * rr : 0.9 + 0.9 * rr;
            if (setup.RewardRisk < rrMin && rr > 0.5) return;
            double wantR = Math.Max(setup.RewardRisk, shortTarget ? setup.RewardRisk : 1.2 + 1.3 * rr);
            // Stop placement is reading the chart too: a weak analyst puts it somewhere arbitrary, often inside the
            // noise (whipsawed) or so far out that the size shrinks to nothing.
            double riskPx = setup.Risk * Math.Max(0.55, Math.Min(1.7, Math.Exp(0.45 * (1 - analysis) * G())));

            // Chasing: the move already left without them.
            if (setup.Late > 0.3)
            {
                double patience = (sc + timing) / 2;
                if (U() < 0.25 + 0.75 * patience) return;
                plan.FollowedPlan = false;
                plan.Notes = "Chased a late entry";
                e.Note(now, $"Chased {bestF.Ticker} after the ideal entry had passed.");
            }
            if (bestSeen < threshold + 0.02 && setup.Quality < 0.45) { plan.FollowedPlan = false; plan.Notes = Append(plan.Notes, "Below their usual standard"); }

            // Size: about 1% of the desk at risk, varying with how disciplined their sizing is, then the hard clamp.
            double rm = p[Skill.RiskManagement] / 100.0;
            decimal budget = Math.Max(0m, e.Account.Equity) * 0.012m;
            double mult = Math.Max(0.4, Math.Min(2.0, 1 + 0.5 * (1 - rm) * G()));
            if (e.ConsecutiveWins >= 2 && rm < 0.5) { mult *= 1 + 0.3 * (1 - rm); plan.Notes = Append(plan.Notes, "Sized up after wins"); }
            if (cl >= 2 && sc < 0.45) { mult *= 1 + 0.4 * (1 - sc); plan.Notes = Append(plan.Notes, "Revenge sizing"); plan.FollowedPlan = false; }
            budget *= (decimal)mult;
            if (budget > pol.MaxRiskPerTrade) budget = pol.MaxRiskPerTrade; // hard: whatever they feel
            decimal pv = bestF.PointValue;
            decimal perContract = (decimal)riskPx * pv + 2 * _rules.CommissionFor(1);
            if (perContract <= 0m) return;
            long contracts = (long)Math.Floor(budget / perContract);
            long held = Math.Abs(e.Orders.PositionQuantity(bestF.Ticker));
            contracts = Math.Min(contracts, pol.MaxContracts - held);
            contracts = Math.Min(contracts, e.Orders.MaxQuantity(bestF.Ticker, setup.Side > 0 ? OrderSide.Buy : OrderSide.Sell));
            (long symGross, long secGross) = CompanyGross(bestF.Ticker);
            contracts = Math.Min(contracts, Math.Min(Config.CompanyMaxPerSymbol - symGross, Config.CompanyMaxPerSector - secGross));
            if (contracts < 1) return;

            decimal entry = (decimal)bestF.Last;
            plan.Contracts = contracts;
            plan.Entry = entry;
            plan.Stop = PriceTick.RoundNearest(entry - setup.Side * (decimal)riskPx);
            plan.Target = PriceTick.RoundNearest(entry + setup.Side * (decimal)(riskPx * wantR));
            if (plan.Stop <= 0m || plan.Target <= 0m) return;

            // Hands: slow or clumsy order entry costs seconds (and price); a poor station now and then freezes.
            double exec = p[Skill.Execution] / 100.0;
            double quality = StationOf(e)?.Quality ?? 0.3;
            double delay = (1 - exec) * 16 * (1.4 - quality);
            if (U() < 0.03 * (1.2 - quality)) { delay += 25; plan.Notes = Append(plan.Notes, "Platform lag"); }
            bool market = plan.Notes.Contains("Chased") || U() < 0.25 * (1 - exec);
            if (market) plan.Notes = Append(plan.Notes, "Market order");
            if (delay >= 2)
                e.Memory.Delayed.Add((now.AddSeconds(delay).Ticks, plan));
            else SubmitEntry(e, plan, market);
        }

        private static string Append(string notes, string more) => string.IsNullOrEmpty(more) ? notes : string.IsNullOrEmpty(notes) ? more : notes + "; " + more;

        private static bool BreakSoon(DayPlan p, int m, int within)
        {
            for (int d = 0; d <= within; d++)
            {
                BreakKind b = p.BreakAt(m + d);
                if (b != BreakKind.None && b != BreakKind.Restroom) return true;
            }
            return false;
        }

        private void SubmitEntry(Employee e, EntryPlan plan, bool market)
        {
            OrderSide side = plan.Side > 0 ? OrderSide.Buy : OrderSide.Sell;
            Order order;
            if (market) order = e.Orders.SubmitMarket(plan.Ticker, side, plan.Contracts);
            else
            {
                // A marketable limit a couple of ticks through the touch: fills now, never at any price.
                _market.TryGetQuote(plan.Ticker, out Quote q);
                decimal tick = PriceTick.For(q.Last);
                decimal limit = side == OrderSide.Buy ? q.Ask + 2 * tick : q.Bid - 2 * tick;
                order = e.Orders.SubmitLimit(plan.Ticker, side, plan.Contracts, PriceTick.RoundNearest(limit));
            }
            e.Memory.LastEntryTicks = _market.Now.Ticks;
            if (order.Status == OrderStatus.Rejected)
            {
                e.Note(_market.Now, $"Order refused: {order.StatusReason}", limit: order.StatusReason.Contains("limit"));
                return;
            }
            e.Memory.Pending[order.Id] = plan;
            // A fill at submission: record it (OnFill ran before the plan was known), then protect it now.
            if (order.FilledQuantity > 0 && e.Open.TryGetValue(plan.Ticker, out TradeRecord rec) && rec.Notes == "Manual")
            {
                rec.Strategy = plan.Strategy;
                rec.PlannedStop = plan.Stop;
                rec.PlannedTarget = plan.Target;
                rec.Quality = plan.Quality;
                rec.FollowedPlan = plan.FollowedPlan;
                rec.Notes = plan.Notes;
                rec.Risk = Math.Abs(rec.Entry - plan.Stop) * rec.Contracts * e.Account.Contract(plan.Ticker).PointValue;
            }
            Pending(e, _market.Now);
        }

        /// <summary>Entries waiting on fills or on slow hands: protect what filled, give up on what didn't within two minutes.</summary>
        private void Pending(Employee e, DateTime now)
        {
            for (int i = e.Memory.Delayed.Count - 1; i >= 0; i--)
            {
                if (e.Memory.Delayed[i].DueTicks > now.Ticks) continue;
                EntryPlan plan = e.Memory.Delayed[i].Plan;
                e.Memory.Delayed.RemoveAt(i);
                SubmitEntry(e, plan, plan.Notes.Contains("Market order"));
            }
            if (e.Memory.Pending.Count == 0) return;
            foreach (long id in new List<long>(e.Memory.Pending.Keys))
            {
                EntryPlan plan = e.Memory.Pending[id];
                Order order = null;
                foreach (Order o in e.Orders.Orders) if (o.Id == id) order = o;
                if (order == null) { e.Memory.Pending.Remove(id); continue; }
                bool stale = now.Ticks - order.SubmittedAt.Ticks > TimeSpan.TicksPerMinute * 2;
                if (order.IsOpen && !stale) continue;
                if (order.IsOpen) e.Orders.Cancel(order.Id);
                e.Memory.Pending.Remove(id);
                if (order.FilledQuantity == 0)
                {
                    e.Note(now, $"Entry in {plan.Ticker} didn't fill: price ran away.");
                    continue;
                }
                Protect(e, plan, order.FilledQuantity, now);
            }
        }

        /// <summary>The bracket: take-profit and stop. A stop the market has already passed means get out now.</summary>
        private void Protect(Employee e, EntryPlan plan, long quantity, DateTime now)
        {
            long held = Math.Abs(e.Orders.PositionQuantity(plan.Ticker));
            long q = Math.Min(quantity, held);
            if (q <= 0) return;
            List<Order> legs = e.Orders.SubmitBracket(plan.Ticker, q, plan.Target, plan.Stop);
            bool stopOk = legs.Exists(o => o.IsStop && o.Status != OrderStatus.Rejected);
            if (!stopOk)
            {
                foreach (Order o in legs) if (o.IsOpen) e.Orders.Cancel(o.Id);
                Flatten(e, plan.Ticker);
                e.Note(now, $"Stopped out of {plan.Ticker} before the stop could be placed.");
            }
        }

        /// <summary>Cancels a symbol's protective orders and closes it at market.</summary>
        private void Flatten(Employee e, string ticker)
        {
            foreach (Order o in new List<Order>(e.Orders.OpenOrders)) if (o.Ticker == ticker) e.Orders.Cancel(o.Id);
            long q = e.Orders.PositionQuantity(ticker);
            if (q != 0) e.Orders.SubmitMarket(ticker, q > 0 ? OrderSide.Sell : OrderSide.Buy, Math.Abs(q));
        }

        /// <summary>Off the desk (break, training, paused): unfilled entries are pulled. Brackets keep working.</summary>
        private void DropEntries(Employee e, DateTime now)
        {
            e.Memory.Delayed.Clear();
            if (e.Memory.Pending.Count == 0) return;
            foreach (long id in new List<long>(e.Memory.Pending.Keys))
            {
                Order order = null;
                foreach (Order o in e.Orders.OpenOrders) if (o.Id == id) order = o;
                if (order != null) e.Orders.Cancel(order.Id);
                EntryPlan plan = e.Memory.Pending[id];
                e.Memory.Pending.Remove(id);
                if (order != null && order.FilledQuantity > 0) Protect(e, plan, order.FilledQuantity, now);
            }
        }

        /// <summary>
        /// Managing open trades at the desk: breakeven and trailing stops (trade management), fear exits at small profits
        /// and widened stops on losers (low self-control, inside the hard risk limit), and a time stop.
        /// </summary>
        private void Manage(Employee e, DateTime now, long mi)
        {
            if (e.Open.Count == 0) return;
            Person p = e.Person;
            double tm = p[Skill.TradeManagement] / 100.0, sc = p[Skill.SelfControl] / 100.0, caution = p.Trait(Trait.Caution) / 100.0;
            CounterRandom r = _rng.Sub("manage").Sub(e.Id);
            foreach (TradeRecord rec in new List<TradeRecord>(e.Open.Values))
            {
                if (rec.PlannedStop <= 0m || rec.Entry <= 0m) continue;
                if (!_market.TryGetQuote(rec.Ticker, out Quote q)) continue;
                decimal riskPx = Math.Abs(rec.Entry - rec.PlannedStop);
                if (riskPx <= 0m) continue;
                double rNow = (double)((q.Last - rec.Entry) * rec.Side / riskPx);
                Order stop = null;
                foreach (Order o in e.Orders.OpenOrders) if (o.Ticker == rec.Ticker && o.IsStop && o.OcoGroup != 0) stop = o;
                bool newMinute = rec.LastManagedMinute != mi;
                rec.LastManagedMinute = mi;
                if (e.Activity == Activity.RiskLocked) continue;

                if (stop != null && rNow >= 1.0 && tm >= 0.4 && !rec.MovedToBreakeven)
                {
                    decimal be = PriceTick.RoundNearest(rec.Entry + rec.Side * PriceTick.For(rec.Entry));
                    if (e.Orders.ModifyPrice(stop.Id, be) == null) rec.MovedToBreakeven = true;
                }
                if (stop != null && rNow >= 1.6 && tm >= 0.7 && !rec.Trailed)
                {
                    decimal trail = PriceTick.RoundNearest(rec.Entry + rec.Side * riskPx * 0.8m);
                    if (e.Orders.ModifyPrice(stop.Id, trail) == null) rec.Trailed = true;
                }
                if (!newMinute) continue;
                // Fear: grab a small profit before the plan says so. Whether this trade gets bailed on is decided once
                // (keyed by the trade), then happens the first time it shows a small gain.
                if (rNow >= 0.35 && rNow <= 0.9 && r.Chance(0.6 * caution * (1 - sc), rec.Id, 11))
                {
                    Flatten(e, rec.Ticker);
                    rec.Notes = Append(rec.Notes, $"Took profit early at {rNow:+0.0}R");
                    rec.FollowedPlan = false;
                    e.Note(now, $"Took profit early in {rec.Ticker} at {rNow:+0.0}R out of fear.");
                    continue;
                }
                // Hope: move the stop away from a loser, but never past the company's risk limit.
                if (stop != null && rNow <= -0.7 && !rec.Widened && r.Chance(0.5 * (1 - sc), mi, rec.Id + 7))
                {
                    rec.Widened = true;
                    decimal wider = PriceTick.RoundNearest(rec.PlannedStop - rec.Side * riskPx * 0.5m);
                    decimal newRisk = Math.Abs(rec.Entry - wider) * stop.RemainingQuantity * e.Account.Contract(rec.Ticker).PointValue;
                    if (newRisk <= e.Policy.MaxRiskPerTrade && wider > 0m && e.Orders.ModifyPrice(stop.Id, wider) == null)
                    {
                        rec.Notes = Append(rec.Notes, "Moved stop further out");
                        rec.FollowedPlan = false;
                        e.Note(now, $"Held a losing {rec.Ticker} trade longer, moving the stop out (within the risk limit).");
                    }
                }
                double minutes = (now.Ticks - rec.Opened) / (double)TimeSpan.TicksPerMinute;
                if ((rec.Strategy == Strategy.Scalping && minutes > 15) || minutes > 120)
                {
                    Flatten(e, rec.Ticker);
                    rec.Notes = Append(rec.Notes, "Time stop");
                }
            }
        }

        /// <summary>Flat into the close: whoever's in the building closes up a few minutes before four.</summary>
        private void EndOfDay(Employee e, DateTime now)
        {
            if (e.Memory.FlattenedForClose) return;
            if (now.TimeOfDay < _market.Schedule.RegularClose - TimeSpan.FromMinutes(4)) return;
            e.Memory.FlattenedForClose = true;
            DropEntries(e, now);
            bool any = false;
            foreach (Position pos in e.Account.Portfolio.Positions) if (pos.IsOpen) any = true;
            if (!any) return;
            foreach (Position pos in new List<Position>(e.Account.Portfolio.Positions)) if (pos.IsOpen) Flatten(e, pos.Ticker);
        }
    }
}
