using System;
using System.Collections.Generic;

namespace OpeningBell.Economy
{
    // Serialized by value in assets: append only, never reorder.
    public enum BillCategory
    {
        Housing,
        Utilities,
        Subscription,
        Lease,
    }

    public enum StoreCategory
    {
        Equipment,
        Service,
        /// <summary>A rented space (e.g. an office). Its monthly cost is charged as rent.</summary>
        Lease,
    }

    /// <summary>Life costs and the starting bank balance. Amounts are doubles for the inspector; converted to decimal on use.</summary>
    [Serializable]
    public sealed class EconomyConfig
    {
        public double StartingBankBalance = 1800;

        /// <summary>Food and everyday spending, charged every calendar day (weekends too).</summary>
        public double DailyLivingCost = 15;

        /// <summary>Charged whenever a bill leaves the bank account below zero.</summary>
        public double OverdraftFee = 35;

        public List<RecurringBill> Bills = new List<RecurringBill>();
    }

    [Serializable]
    public sealed class RecurringBill
    {
        public string Id = "";
        public string Name = "";
        public BillCategory Category;
        public double Amount;

        /// <summary>1–31; clamped to the month's length (a "31st" bill lands on Feb 28/29).</summary>
        public int DayOfMonth = 1;
    }

    /// <summary>Something the Store sells. Equipment upgrades an apartment slot; services add a monthly bill.</summary>
    [Serializable]
    public sealed class StoreItem
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public StoreCategory Category;

        /// <summary>Apartment slot this upgrades (e.g. "Chair"). Presentation picks the visual.</summary>
        public string Slot = "";

        public double Price;
        public double MonthlyCost;
        public int BillDayOfMonth = 1;

        /// <summary>Base bill this service replaces (e.g. fibre replaces basic internet).</summary>
        public string ReplacesBillId = "";
    }
}
