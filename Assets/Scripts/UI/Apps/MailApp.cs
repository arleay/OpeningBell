using System.Collections.Generic;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>Inbox (newest first) and a reading pane. Opening an email marks it read.</summary>
    public sealed class MailApp : TerminalPanel
    {
        private readonly List<Email> _items = new List<Email>();
        private readonly ListView _list;
        private readonly Label _subject, _meta, _body;
        private Email _selected;
        private int _knownCount = -1;

        public MailApp(TerminalContext context) : base(context, "mail-app")
        {
            var left = Ui.Box("app-column app-card", Root);
            Ui.Label("panel-title", left, "INBOX");
            _list = new ListView(_items, 52, MakeRow, BindRow) { selectionType = SelectionType.None, name = "mail-list" };
            _list.AddToClassList("activity-list");
            left.Add(_list);

            var pane = Ui.Box("app-column app-wide app-card", Root);
            _subject = Ui.Label("mail-subject", pane);
            _meta = Ui.Label("muted mail-meta", pane);
            _body = Ui.Label("mail-body", pane);
            _body.name = "mail-body";
        }

        public override void Refresh()
        {
            var emails = Context.Game.Inbox.Emails;
            if (emails.Count != _knownCount)
            {
                _knownCount = emails.Count;
                _items.Clear();
                for (int i = emails.Count - 1; i >= 0; i--) _items.Add(emails[i]);
                if (_selected == null && _items.Count > 0) Open(_items[0]);
            }
            _list.RefreshItems();
        }

        public void Open(Email email)
        {
            _selected = email;
            Context.Game.Inbox.MarkRead(email);
            Ui.SetText(_subject, email.Subject);
            Ui.SetText(_meta, $"From {email.Sender}  ·  {Fmt.Date(email.Time)} {Fmt.Minutes(email.Time)}");
            Ui.SetText(_body, email.Body);
            _list.RefreshItems();
        }

        private VisualElement MakeRow()
        {
            var row = Ui.Box("mail-row");
            var top = Ui.Box("news-meta", row);
            Ui.Label("mail-sender", top);
            Ui.Label("news-time muted mail-time", top);
            Ui.Label("news-headline", row);
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (row.userData is Email email) Open(email);
            });
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            Email e = _items[index];
            row.userData = e;
            row.name = "mail-" + e.Id;
            Ui.SetText((Label)row[0][0], e.Sender);
            Ui.SetText((Label)row[0][1], $"{Fmt.Date(e.Time)} {Fmt.Minutes(e.Time)}");
            Ui.SetText((Label)row[1], e.Subject);
            row.EnableInClassList("unread", !e.IsRead);
            row.EnableInClassList("selected", e == _selected);
        }
    }
}
