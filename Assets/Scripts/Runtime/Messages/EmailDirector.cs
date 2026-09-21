using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Economy;
using OpeningBell.Trading;

namespace OpeningBell
{
    /// <summary>
    /// Decides when each scripted email arrives: at game start, at a time, on the player's first fill/win/loss,
    /// a few days before each rent date, and on overdraft. Event triggers are latched and delivered on the next
    /// Update, so every email gets the current game time and balances.
    /// </summary>
    public sealed class EmailDirector
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private readonly IReadOnlyList<EmailDefinition> _definitions;
        private readonly Inbox _inbox;
        private readonly Account _brokerage;
        private readonly EconomySystem _economy;
        private readonly DateTime _gameStartDate;
        private readonly HashSet<EmailTrigger> _pending = new HashSet<EmailTrigger>();

        public EmailDirector(IReadOnlyList<EmailDefinition> definitions, Inbox inbox, Account brokerage, OrderManager orders,
            EconomySystem economy, DateTime gameStartDate)
        {
            _definitions = definitions;
            _inbox = inbox;
            _brokerage = brokerage;
            _economy = economy;
            _gameStartDate = gameStartDate.Date;
            _pending.Add(EmailTrigger.GameStart);

            orders.OrderFilled += fill =>
            {
                _pending.Add(EmailTrigger.FirstFill);
                if (fill.RealizedPnL < 0m) _pending.Add(EmailTrigger.FirstLosingTrade);
                if (fill.RealizedPnL > 0m) _pending.Add(EmailTrigger.FirstWinningTrade);
            };
            economy.TransactionPosted += tx =>
            {
                if (economy.Bank.IsOverdrawn) _pending.Add(EmailTrigger.Overdrawn);
            };
        }

        public void Update(DateTime now)
        {
            foreach (EmailDefinition def in _definitions)
            {
                switch (def.Trigger)
                {
                    case EmailTrigger.AtTime:
                        if (now >= _gameStartDate.AddDays(def.DayOffset).AddMinutes(def.MinuteOfDay)) Send(def, def.Id, now);
                        break;
                    case EmailTrigger.RentDueSoon:
                        foreach (UpcomingBill due in _economy.Upcoming(now, def.DaysBefore))
                            if (due.Bill.Category == BillCategory.Housing)
                                Send(def, $"{def.Id}:{due.Date:yyyyMMdd}", now, due);
                        break;
                    default:
                        if (_pending.Contains(def.Trigger)) Send(def, def.Id, now);
                        break;
                }
            }
            _pending.Clear();
        }

        private void Send(EmailDefinition def, string key, DateTime now, UpcomingBill? rent = null)
        {
            if (_inbox.HasDelivered(key)) return;
            string body = def.Body
                .Replace("{bank}", Dollars(_economy.Bank.Balance))
                .Replace("{brokerage}", Dollars(_brokerage.Equity))
                .Replace("{rent}", rent.HasValue ? Dollars(rent.Value.Bill.Amount) : "")
                .Replace("{due}", rent.HasValue ? rent.Value.Date.ToString("dddd, MMMM d", C) : "");
            _inbox.Deliver(key, now, def.Sender, def.Subject, body);
        }

        private static string Dollars(decimal value) => (value < 0m ? "-$" : "$") + Math.Abs(value).ToString("N2", C);
    }
}
