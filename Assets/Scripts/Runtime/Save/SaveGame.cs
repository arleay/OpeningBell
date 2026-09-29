using System;
using System.Collections.Generic;
using OpeningBell.Economy;
using OpeningBell.Market;
using OpeningBell.Trading;

namespace OpeningBell
{
    /// <summary>
    /// Root save model. Bump <see cref="CurrentVersion"/> on breaking changes and add a step to
    /// <see cref="SaveSystem"/>'s upgrade path; newer-than-known saves are refused rather than guessed at.
    /// </summary>
    [Serializable]
    public sealed class SaveGame
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public string SavedAtUtc;
        public long Seed;
        public long Clock;
        public MarketSaveData Market = new MarketSaveData();
        public TradingSaveData Trading = new TradingSaveData();

        // Additive since v1: older saves load with HasEconomy = false and start a fresh economy.
        public bool HasEconomy;
        public EconomySaveData Economy = new EconomySaveData();

        public bool HasInbox;
        public InboxSaveData Inbox = new InboxSaveData();
        public bool HasPlayer;
        public PlayerSaveData Player = new PlayerSaveData();
        public bool HasVehicles;
        public global::OpeningBell.Vehicles.FleetSaveData Vehicles = new global::OpeningBell.Vehicles.FleetSaveData();
        public bool HasLook;
        public PlayerLook Look = new PlayerLook();
        public bool HasDrawings;
        public ChartDrawingsSaveData Drawings = new ChartDrawingsSaveData();
        public bool HasHome;
        public global::OpeningBell.Home.HomeSaveData Home = new global::OpeningBell.Home.HomeSaveData();
        /// <summary>Prop firm accounts (PROP_SPEC). Additive: older saves have none.</summary>
        public bool HasProp;
        public PropSaveData Prop = new PropSaveData();
        public bool HasJob;
        public JobSaveData Job = new JobSaveData();
        /// <summary>Chips, records and any unfinished blackjack hand at The Meridian (CASINO_SPEC §88). Additive.</summary>
        public bool HasCasino;
        public global::OpeningBell.Casino.CasinoSaveData Casino = new global::OpeningBell.Casino.CasinoSaveData();
        /// <summary>The player's hedge fund (FUND_SPEC): company, people, desks, ledger. Additive: older saves have none.</summary>
        public bool HasFund;
        public global::OpeningBell.Fund.FundSaveData Fund = new global::OpeningBell.Fund.FundSaveData();
        /// <summary>Places and districts found on the map (names; districts prefixed "district:"). Additive: older saves start blank.</summary>
        public List<string> Discovered = new List<string>();
    }

    /// <summary>
    /// The player's character from the creator: which character model (index into the art library's people, with its
    /// label to catch a reordered library) and tint choices (0 = the model's own colour).
    /// </summary>
    [Serializable]
    public sealed class PlayerLook
    {
        public string Name = "Alex";
        public int Model;
        public string ModelLabel = "";
        public int Skin, Hair, Top;
        /// <summary>Jewellery bought (catalog ids) and the pieces being worn, at most one per slot. Additive: older saves own none.</summary>
        public List<string> Jewelry = new List<string>();
        public List<string> Worn = new List<string>();

        public PlayerLook Copy()
        {
            var copy = (PlayerLook)MemberwiseClone();
            copy.Jewelry = new List<string>(Jewelry ?? new List<string>());
            copy.Worn = new List<string>(Worn ?? new List<string>());
            return copy;
        }
    }

    [Serializable]
    public sealed class PlayerSaveData
    {
        public float X, Y, Z, Yaw;
    }
}
