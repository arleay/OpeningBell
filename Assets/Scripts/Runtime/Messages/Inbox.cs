using System;
using System.Collections.Generic;

namespace OpeningBell
{
    public sealed class Email
    {
        public long Id { get; }
        public string Key { get; }
        public DateTime Time { get; }
        public string Sender { get; }
        public string Subject { get; }
        public string Body { get; }
        public bool IsRead { get; internal set; }

        internal Email(long id, string key, DateTime time, string sender, string subject, string body, bool isRead)
        {
            Id = id;
            Key = key;
            Time = time;
            Sender = sender;
            Subject = subject;
            Body = body;
            IsRead = isRead;
        }
    }

    /// <summary>Delivered emails, oldest first. Each delivery key is used once, so nothing arrives twice.</summary>
    public sealed class Inbox
    {
        private readonly List<Email> _emails = new List<Email>();
        private readonly HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);
        private long _nextId = 1;

        public IReadOnlyList<Email> Emails => _emails;

        public int UnreadCount
        {
            get
            {
                int n = 0;
                foreach (Email e in _emails)
                    if (!e.IsRead) n++;
                return n;
            }
        }

        public event Action<Email> Received;

        public bool HasDelivered(string key) => _keys.Contains(key);

        public Email Deliver(string key, DateTime time, string sender, string subject, string body)
        {
            if (!_keys.Add(key)) return null;
            var email = new Email(_nextId++, key, time, sender, subject, body, false);
            _emails.Add(email);
            Received?.Invoke(email);
            return email;
        }

        public void MarkRead(Email email) => email.IsRead = true;

        public InboxSaveData CaptureState()
        {
            var data = new InboxSaveData { NextId = _nextId };
            foreach (Email e in _emails)
                data.Emails.Add(new EmailSaveData
                {
                    Id = e.Id, Key = e.Key, Time = e.Time.Ticks, Sender = e.Sender, Subject = e.Subject, Body = e.Body, IsRead = e.IsRead,
                });
            return data;
        }

        public void RestoreState(InboxSaveData data)
        {
            _emails.Clear();
            _keys.Clear();
            _nextId = data.NextId;
            foreach (EmailSaveData e in data.Emails)
            {
                _emails.Add(new Email(e.Id, e.Key, new DateTime(e.Time), e.Sender, e.Subject, e.Body, e.IsRead));
                _keys.Add(e.Key);
            }
        }
    }

    [Serializable]
    public sealed class InboxSaveData
    {
        public long NextId = 1;
        public List<EmailSaveData> Emails = new List<EmailSaveData>();
    }

    [Serializable]
    public sealed class EmailSaveData
    {
        public long Id, Time;
        public string Key, Sender, Subject, Body;
        public bool IsRead;
    }
}
