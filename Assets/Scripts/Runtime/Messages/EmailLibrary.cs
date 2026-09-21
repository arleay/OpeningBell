using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell
{
    // Serialized by value in assets: append only, never reorder.
    public enum EmailTrigger
    {
        GameStart,
        AtTime,
        FirstFill,
        FirstLosingTrade,
        FirstWinningTrade,
        RentDueSoon,
        Overdrawn,
    }

    /// <summary>
    /// A scripted email. Onboarding teaches through these (spec §47) instead of a textbook up front.
    /// Body tokens: {bank}, {brokerage}, {rent}, {due}.
    /// </summary>
    [Serializable]
    public sealed class EmailDefinition
    {
        public string Id = "";
        public string Sender = "";
        public string Subject = "";
        [TextArea(3, 12)] public string Body = "";
        public EmailTrigger Trigger;

        [Tooltip("AtTime: calendar days after the game's start date, and minute of that day.")]
        public int DayOffset;
        public int MinuteOfDay;

        [Tooltip("RentDueSoon: send this many days before each rent date (repeats monthly).")]
        public int DaysBefore = 3;
    }

    [CreateAssetMenu(menuName = "Opening Bell/Email Library", fileName = "EmailLibrary")]
    public sealed class EmailLibrary : ScriptableObject
    {
        [SerializeField] private List<EmailDefinition> emails = new List<EmailDefinition>();

        public IReadOnlyList<EmailDefinition> Emails => emails;
    }
}
