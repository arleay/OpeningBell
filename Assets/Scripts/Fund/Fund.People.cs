using System;
using System.Collections.Generic;
using OpeningBell.Market;
using OpeningBell.Trading;

namespace OpeningBell.Fund
{
    public sealed partial class HedgeFund
    {
        // ------------------------------------------------------------------ job ads

        public static decimal ListingPrice(Channel c) => c switch
        {
            Channel.IndustryBoard => 2_500m,
            Channel.Headhunter => 15_000m,
            _ => 0m,
        };

        public static string ChannelName(Channel c) => c switch
        {
            Channel.IndustryBoard => "Industry job board",
            Channel.Headhunter => "Headhunter search",
            _ => "Kell Valley job board",
        };

        public static string ChannelBlurb(Channel c) => c switch
        {
            Channel.IndustryBoard => "Trading careers board. About two applications a day, mostly experienced traders.",
            Channel.Headhunter => "A search firm approaches senior people for you. Fewer, stronger candidates, who expect more.",
            _ => "Free local listing. About one application a day, mostly early-career traders.",
        };

        /// <summary>Applications a business day from a channel, before reputation.</summary>
        private static double ChannelRate(Channel c) => c switch
        {
            Channel.IndustryBoard => 2.0,
            Channel.Headhunter => 0.9,
            _ => 1.1,
        };

        /// <summary>How senior a channel's applicants are (0–1), before reputation.</summary>
        private static double ChannelLevel(Channel c) => c switch
        {
            Channel.IndustryBoard => 0.35,
            Channel.Headhunter => 0.7,
            _ => 0.1,
        };

        /// <summary>Posts a job ad (paid now if not free). Error, or null.</summary>
        public string Post(Channel channel, Sector? sector, Strategy? strategy, out Listing listing)
        {
            listing = null;
            if (!Exists) return "Register the fund first.";
            decimal price = ListingPrice(channel);
            if (price > 0m)
            {
                string error = Spend(_market.Now, CashKind.Recruiting, ExpenseKind.Recruiting, price, ChannelName(channel) + " listing");
                if (error != null) return error;
            }
            DateTime now = _market.Now;
            listing = new Listing
            {
                Id = _nextListingId++, Channel = channel, AnySector = sector == null, Sector = sector ?? default,
                AnyStrategy = strategy == null, Strategy = strategy ?? default, Posted = now.Ticks, Expires = now.AddDays(Config.ListingDays).Ticks,
            };
            _listings.Add(listing);
            Touch();
            return null;
        }

        public void CloseListing(Listing l)
        {
            l.Closed = true;
            Touch();
        }

        /// <summary>
        /// Once an hour: each live listing may bring an application. Rates are per business day, spread over 8 AM–8 PM;
        /// a targeted listing (one sector or strategy) draws a little less. Reputation widens the pool and raises its level.
        /// </summary>
        private void Applications(DateTime now)
        {
            if (now.Hour < 8 || now.Hour >= 20) return;
            double repBoost = 0.7 + 0.8 * Reputation / 100.0;
            foreach (Listing l in _listings)
            {
                if (!l.Live(now)) continue;
                double perDay = ChannelRate(l.Channel) * repBoost * (l.AnySector ? 1.0 : 0.7) * (l.AnyStrategy ? 1.0 : 0.75);
                if (!_market.Schedule.IsTradingDay(now.Date)) perDay *= 0.4;
                long hour = now.Ticks / TimeSpan.TicksPerHour;
                if (!_rng.Sub("apply").Sub(l.Id).Chance(perDay / 12.0, hour)) continue;
                double level = Math.Min(1.0, ChannelLevel(l.Channel) + 0.35 * Reputation / 100.0);
                long id = _nextPersonId++;
                Person p = PeopleFactory.Make(_rng.Sub("people"), id, level, l.AnySector ? (Sector?)null : l.Sector, l.AnyStrategy ? (Strategy?)null : l.Strategy);
                double interest = Math.Max(0.1, Math.Min(1.0, 0.35 + 0.5 * Reputation / 100.0 + 0.2 * (_rng.Sub("interest").Double(id) - 0.5)));
                var a = new Applicant
                {
                    Person = p, ListingId = l.Id, Received = now.Ticks, Interest = interest, Status = ApplicantStatus.Open,
                    AvailableFrom = NextWorkday(now.Date.AddDays(1 + _rng.Sub("start").Int(7, id))).Ticks,
                    Expires = now.AddDays(Config.ApplicantPatienceDays).Ticks,
                    Asking = Negotiation.Asking(p, Reputation),
                };
                p.Note(now, $"Applied through the {ChannelName(l.Channel).ToLowerInvariant()}");
                _applicants.Add(a);
                l.Received++;
                Notify(NoticeLevel.Routine, "New applicant", $"{p.Name}, {p.Seniority.ToLowerInvariant()} ({Person.StrategyName(p.Strategy).ToLowerInvariant()}), applied.", p.Id, "applicant");
                Touch();
            }
            foreach (Applicant a in _applicants)
            {
                if (a.Status != ApplicantStatus.Open || now.Ticks < a.Expires) continue;
                a.Status = ApplicantStatus.Expired;
                a.Person.Note(now, "Withdrew: no answer from the firm");
                Touch();
            }
        }

        private DateTime NextWorkday(DateTime d)
        {
            while (!_market.Schedule.IsTradingDay(d)) d = d.AddDays(1);
            return d;
        }

        // ------------------------------------------------------------------ offers and hiring

        /// <summary>
        /// Makes an offer. The applicant accepts (then they're hired), counters, rejects, or withdraws after too many
        /// unreasonable offers. The talks themselves cost a little interest.
        /// </summary>
        public Response Offer(Applicant a, Contract offer)
        {
            if (a.Status != ApplicantStatus.Open) return new Response(Answer.Reject, null, "They're no longer available.");
            DateTime now = _market.Now;
            a.Rounds++;
            Response r = Negotiation.Respond(a.Person, offer, a.Asking, a.Interest, a.Lowballs, Config.LowballsBeforeWithdraw);
            a.LastLine = r.Line;
            switch (r.Answer)
            {
                case Answer.Accept:
                    Hire(a, offer, now);
                    break;
                case Answer.Counter:
                    a.LastCounter = r.Counter;
                    a.Interest = Math.Max(0.05, a.Interest - 0.03);
                    a.Person.Note(now, "Countered an offer of " + offer.Describe());
                    break;
                case Answer.Reject:
                    a.Lowballs++;
                    a.Interest = Math.Max(0.05, a.Interest - 0.08);
                    a.Person.Note(now, "Turned down an offer of " + offer.Describe());
                    break;
                case Answer.Withdraw:
                    a.Status = ApplicantStatus.Withdrawn;
                    a.Person.Note(now, "Withdrew after repeated low offers");
                    break;
            }
            Notify(NoticeLevel.Routine, r.Answer == Answer.Accept ? "Offer accepted" : r.Answer == Answer.Counter ? "Counteroffer" : "Offer declined",
                $"{a.Person.Name}: \"{r.Line}\"", a.Id, "offer");
            Touch();
            return r;
        }

        public void Decline(Applicant a)
        {
            if (a.Status != ApplicantStatus.Open) return;
            a.Status = ApplicantStatus.Declined;
            a.Person.Note(_market.Now, "Application declined by the firm");
            Touch();
        }

        /// <summary>Hires someone directly (tests and debug tools; the game hires through <see cref="Offer"/>).</summary>
        internal Employee HireDirect(Person p, Contract c, DateTime starts)
        {
            var a = new Applicant { Person = p, AvailableFrom = starts.Ticks, Asking = c, Interest = 0.6, Status = ApplicantStatus.Open };
            _applicants.Add(a);
            Hire(a, c, _market.Now);
            Employee e = _employees[_employees.Count - 1];
            e.StartsOn = starts.Ticks;
            return e;
        }

        internal Person MakePerson(double level) => PeopleFactory.Make(_rng.Sub("people"), _nextPersonId++, level, null, null);

        private void Hire(Applicant a, Contract contract, DateTime now)
        {
            a.Status = ApplicantStatus.Hired;
            Contract signed = contract.Copy();
            signed.Signed = now.Ticks;
            var e = new Employee
            {
                Person = a.Person,
                Contract = signed,
                HiredOn = now.Ticks,
                StartsOn = Math.Max(a.AvailableFrom, NextWorkday(now.Date.AddDays(1)).Ticks),
                Activity = Activity.AwaitingStart,
                Satisfaction = 62 + 18 * a.Interest,
                Policy = new RiskPolicy
                {
                    MaxContracts = Config.DefaultMaxContracts, MaxPositions = Config.DefaultMaxPositions,
                    MaxRiskPerTrade = Config.DefaultRiskPerTrade, MaxDailyLoss = Config.DefaultDailyLoss,
                },
            };
            Wire(e);
            _employees.Add(e);
            a.Person.Note(now, $"Hired as {a.Person.Seniority.ToLowerInvariant()} on {signed.Describe()}");
            Notify(NoticeLevel.Important, "New hire", $"{a.Person.Name} signed ({signed.Describe()}) and starts {new DateTime(e.StartsOn):ddd MMM d}. Give them a workstation and trading capital.", e.Id);
        }

        /// <summary>
        /// Proposes new terms to an employee. They accept a raise or an equal restructure; a cut only if content and
        /// small. Nothing changes unless they agree: the signed contract stays locked.
        /// </summary>
        public Response Renegotiate(Employee e, Contract offer)
        {
            if (e.Former) return new Response(Answer.Reject, null, "They've left.");
            DateTime now = _market.Now;
            Response r = Negotiation.RespondToChange(e.Person, e.Contract, offer, e.Satisfaction);
            if (r.Answer == Answer.Accept)
            {
                decimal before = Negotiation.Value(e.Person, e.Contract), after = Negotiation.Value(e.Person, offer);
                Contract signed = offer.Copy();
                signed.Signed = now.Ticks;
                signed.Version = e.Contract.Version + 1;
                e.Contract = signed;
                e.RaiseRequested = false;
                if (after > before) e.Feel(now, "Pay raise", Math.Min(18, (double)((after - before) / Math.Max(1m, before)) * 60));
                else if (after < before) e.Feel(now, "Pay cut", -Math.Min(20, (double)((before - after) / Math.Max(1m, before)) * 120));
                e.Person.Note(now, "New contract: " + signed.Describe());
            }
            else
            {
                e.Feel(now, "Asked to take a pay cut", -4);
            }
            Touch();
            return r;
        }

        // ------------------------------------------------------------------ leaving

        /// <summary>What letting someone go involves, for the confirmation dialog.</summary>
        public string ExitSummary(Employee e)
        {
            decimal owed = e.WagesAccrued + e.WagesOverdue + e.CommissionAccrued + e.CommissionOverdue;
            var parts = new List<string> { $"Final pay owed: {Money(owed)}." };
            if (e.OpenPositionCount > 0)
                parts.Add($"{e.OpenPositionCount} open position{(e.OpenPositionCount == 1 ? "" : "s")} will be closed at market ({Money(e.Unrealized)} open P&L).");
            if (e.Account != null && e.Account.Cash > 0m) parts.Add($"{Money(e.Account.Cash)} of desk capital returns to operating cash once flat.");
            return string.Join(" ", parts);
        }

        /// <summary>Lets an employee go now: no new trades, positions closed through the market, final pay, capital back.</summary>
        public void LetGo(Employee e, string reason = "Let go")
        {
            if (e.Former) return;
            DateTime now = _market.Now;
            e.Policy.Authorized = false;
            e.LeftReason = reason;
            e.LeftOn = now.Ticks;
            e.Person.Note(now, reason);
            CloseOut(e, reason);
            PayFinal(e, now);
            e.Desk = 0;
            e.MentorOf = 0;
            foreach (Employee m in _employees) if (m.MentorOf == e.Id) m.MentorOf = 0;
            // Still in the building? They pack up and walk out; otherwise they simply don't come back.
            bool here = e.Activity != Activity.OffDuty && e.Activity != Activity.AwaitingStart && e.Activity != Activity.Commuting;
            SetActivity(e, here ? Activity.Leaving : Activity.Former, now);
            Notify(NoticeLevel.Important, reason == "Let go" ? $"{e.Name} let go" : $"{e.Name} has left", ExitSummary(e), e.Id);
            Touch();
        }

        /// <summary>Stops trading on a desk: cancels working orders and closes positions at market (regular session).</summary>
        private void CloseOut(Employee e, string reason)
        {
            if (e.Orders == null) return;
            var open = new List<Order>(e.Orders.OpenOrders);
            foreach (Order o in open) e.Orders.Cancel(o.Id);
            if (_market.Session != MarketSession.Regular) return; // the broker settles anything left at the close
            foreach (Position p in e.Account.Portfolio.Positions)
            {
                if (!p.IsOpen) continue;
                e.Orders.SubmitMarket(p.Ticker, p.Quantity > 0 ? OrderSide.Sell : OrderSide.Buy, Math.Abs(p.Quantity));
            }
            e.Note(_market.Now, $"Positions closed: {reason.ToLowerInvariant()}", limit: true);
        }

        /// <summary>Everything owed to someone leaving, paid now if cash allows (the rest stays owed).</summary>
        private void PayFinal(Employee e, DateTime now)
        {
            PayEmployee(e, now, final: true);
            RecallAll(e, now);
        }

        /// <summary>Brings a flat desk's cash home (capital and uncollected profit).</summary>
        private void RecallAll(Employee e, DateTime now)
        {
            if (e.Account == null || !e.IsFlat) return;
            decimal cash = e.Account.Cash;
            if (cash <= 0m)
            {
                e.Base = 0m;
                return;
            }
            e.Account.Withdraw(cash, "Desk closed");
            Ledger.Post(now, CashKind.Recall, cash, $"Desk closed: {e.Name}", e.Id);
            e.Base = 0m;
        }

        private void Resign(Employee e, DateTime now, string why)
        {
            if (e.Former) return;
            LetGo(e, "Resigned: " + why);
        }
    }
}
