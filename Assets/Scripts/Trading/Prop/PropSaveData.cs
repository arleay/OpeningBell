using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpeningBell.Trading
{
    [Serializable]
    public sealed class PropSaveData
    {
        public long NextNumber;
        public List<PropAccountSaveData> Accounts = new List<PropAccountSaveData>();
        public List<PendingPayoutSaveData> Pending = new List<PendingPayoutSaveData>();
    }

    [Serializable]
    public sealed class PropAccountSaveData
    {
        public string Firm, Id, StatusReason, ActivatedAs;
        public int Size, Phase, Status, PayoutCount;
        public long Number, Opened, NextBilling, LastTradingDate, LastPayoutDate;
        public bool Subscribed, Locked, TradedToday, FlatForToday;
        public string HighWater, DayStartBalance, WithdrawnToday, TotalPaidOut;
        public List<PropDaySaveData> Days = new List<PropDaySaveData>();
        public TradingSaveData Trading = new TradingSaveData();
    }

    [Serializable]
    public sealed class PropDaySaveData
    {
        public long Date;
        public string PnL;
        public bool Traded;
    }

    [Serializable]
    public sealed class PendingPayoutSaveData
    {
        public string AccountId, FirmName, Net;
        public long PayOn;
    }

    public sealed partial class PropDesk
    {
        private static string S(decimal v) => v.ToString(CultureInfo.InvariantCulture);

        private static decimal D(string v) =>
            string.IsNullOrEmpty(v) ? 0m : decimal.Parse(v, NumberStyles.Number, CultureInfo.InvariantCulture);

        public PropSaveData CaptureState()
        {
            var data = new PropSaveData { NextNumber = _nextNumber };
            foreach (PropAccount a in _accounts)
            {
                var s = new PropAccountSaveData
                {
                    Firm = a.Firm.Id, Id = a.Id, StatusReason = a.StatusReason, ActivatedAs = a.ActivatedAs,
                    Size = a.Plan.Size, Phase = (int)a.Phase, Status = (int)a.Status, PayoutCount = a.PayoutCount,
                    Number = a.Number, Opened = a.Opened.Ticks, NextBilling = a.NextBilling.Ticks,
                    LastTradingDate = a.LastTradingDate.Ticks, LastPayoutDate = a.LastPayoutDate.Ticks,
                    Subscribed = a.Subscribed, Locked = a.Locked, TradedToday = a.TradedToday, FlatForToday = a.FlatForToday,
                    HighWater = S(a.HighWater), DayStartBalance = S(a.DayStartBalance), WithdrawnToday = S(a.WithdrawnToday),
                    TotalPaidOut = S(a.TotalPaidOut),
                    Trading = TradingState.Capture(a.Account, a.Orders, null),
                };
                foreach (PropDay d in a.DayList) s.Days.Add(new PropDaySaveData { Date = d.Date.Ticks, PnL = S(d.PnL), Traded = d.Traded });
                data.Accounts.Add(s);
            }
            foreach (PendingPayout p in _pending)
                data.Pending.Add(new PendingPayoutSaveData { AccountId = p.AccountId, FirmName = p.FirmName, Net = S(p.Net), PayOn = p.PayOn.Ticks });
            return data;
        }

        /// <summary>Into a freshly constructed desk. Accounts of firms or plans that no longer exist are dropped.</summary>
        public void RestoreState(PropSaveData data)
        {
            _nextNumber = Math.Max(_nextNumber, data.NextNumber);
            foreach (PropAccountSaveData s in data.Accounts)
            {
                PropFirm firm = PropFirms.ById(s.Firm);
                PropPlan plan = firm?.Plan(s.Size);
                if (plan == null) continue;
                var a = new PropAccount(firm, plan, (PropPhase)s.Phase)
                {
                    Id = s.Id, Number = s.Number, Status = (PropStatus)s.Status, StatusReason = s.StatusReason ?? "",
                    ActivatedAs = s.ActivatedAs ?? "", Opened = new DateTime(s.Opened), NextBilling = new DateTime(s.NextBilling),
                    LastTradingDate = new DateTime(s.LastTradingDate), LastPayoutDate = new DateTime(s.LastPayoutDate),
                    Subscribed = s.Subscribed, PayoutCount = s.PayoutCount, TotalPaidOut = D(s.TotalPaidOut),
                };
                Wire(a);
                TradingState.Restore(s.Trading, a.Account, a.Orders, null);
                a.HighWater = D(s.HighWater);
                a.Locked = s.Locked;
                a.DayStartBalance = D(s.DayStartBalance);
                a.TradedToday = s.TradedToday;
                a.FlatForToday = s.FlatForToday;
                a.WithdrawnToday = D(s.WithdrawnToday);
                foreach (PropDaySaveData d in s.Days)
                    a.DayList.Add(new PropDay { Date = new DateTime(d.Date), PnL = D(d.PnL), Traded = d.Traded });
                _accounts.Add(a);
            }
            foreach (PendingPayoutSaveData p in data.Pending)
                _pending.Add(new PendingPayout { AccountId = p.AccountId, FirmName = p.FirmName, Net = D(p.Net), PayOn = new DateTime(p.PayOn) });
            Changed?.Invoke();
        }
    }
}
