using System;
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

        public PlayerLook Copy() => (PlayerLook)MemberwiseClone();
    }

    [Serializable]
    public sealed class PlayerSaveData
    {
        public float X, Y, Z, Yaw;
    }
}
