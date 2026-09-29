using System;
using NUnit.Framework;
using OpeningBell.Casino;
using OpeningBell.Core;
using UnityEngine;

namespace OpeningBell.Tests
{
    /// <summary>CASINO_SPEC §88: chips, membership and a hand in play survive a save, through the real JSON path.</summary>
    public class CasinoFloorTests
    {
        private static readonly DateTime Now = new DateTime(2030, 1, 7, 22, 0, 0);

        private static BlackjackRound Table(CasinoFloor floor, ulong seed) =>
            new BlackjackRound(new BlackjackRules(), new Shoe(6, 0.75, new SeededRandom(seed)), floor.Account, () => Now);

        [Test]
        public void MidHandSave_RestoresToItsTable_WhicheverComesFirst()
        {
            var floor = new CasinoFloor { Member = true };
            floor.Account.BuyChips(500m, Now, (a, w) => null);
            BlackjackRound live = Table(floor, 1);
            floor.Register("t1", live);
            // Deal until the hand needs a decision (a natural would settle at once).
            do live.Deal(25m); while (live.Phase != BlackjackPhase.PlayerTurn);
            decimal chips = floor.Account.Chips;
            string cards = string.Join(",", live.Hands[0].Cards);
            Assert.AreEqual("t1", floor.TableInPlay);

            string json = JsonUtility.ToJson(floor.CaptureState());
            var data = JsonUtility.FromJson<CasinoSaveData>(json);

            // Loaded before the table is built (the usual order: bootstrap, then the city).
            var loaded = new CasinoFloor();
            loaded.RestoreState(data);
            Assert.IsTrue(loaded.Member);
            Assert.AreEqual(chips, loaded.Account.Chips, "the stake stays taken");
            Assert.AreEqual("t1", loaded.TableInPlay, "pending until the table registers");
            BlackjackRound rebuilt = Table(loaded, 99);
            loaded.Register("t1", rebuilt);
            Assert.AreEqual(BlackjackPhase.PlayerTurn, rebuilt.Phase);
            Assert.AreEqual(cards, string.Join(",", rebuilt.Hands[0].Cards));

            // Saving again before the hand finishes keeps it; finishing it clears it.
            Assert.AreEqual(1, loaded.CaptureState().Rounds.Count);
            while (rebuilt.Phase == BlackjackPhase.PlayerTurn) rebuilt.Stand();
            Assert.IsNull(loaded.TableInPlay);
            Assert.AreEqual(0, loaded.CaptureState().Rounds.Count);
            Assert.AreEqual(chips + rebuilt.TotalReturned, loaded.Account.Chips, "settled once, into the restored chips");
        }

        [Test]
        public void SettledRounds_AreNotSaved_AndOldSavesLoadEmpty()
        {
            var floor = new CasinoFloor();
            BlackjackRound t = Table(floor, 3);
            floor.Register("t1", t);
            Assert.AreEqual(0, floor.CaptureState().Rounds.Count);
            floor.RestoreState(null);
            floor.RestoreState(new CasinoSaveData());
            Assert.AreEqual(0m, floor.Account.Chips);
            Assert.IsFalse(floor.Member);
        }
    }
}
