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
    }

    [Serializable]
    public sealed class PlayerSaveData
    {
        public float X, Y, Z, Yaw;
    }
}
