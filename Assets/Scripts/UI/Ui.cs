using System;
using UnityEngine.UIElements;

namespace OpeningBell.UI
{
    /// <summary>Terse element construction for code-built panels. Styling lives in Terminal.uss.</summary>
    internal static class Ui
    {
        public static VisualElement Box(string classes, VisualElement parent = null)
        {
            var e = new VisualElement();
            AddClasses(e, classes);
            parent?.Add(e);
            return e;
        }

        public static Label Label(string classes, VisualElement parent = null, string text = "")
        {
            var l = new Label(text);
            AddClasses(l, classes);
            parent?.Add(l);
            return l;
        }

        public static Button Button(string text, Action onClick, string classes, VisualElement parent = null, string name = null)
        {
            var b = new Button(onClick) { text = text };
            AddClasses(b, "tb " + classes);
            if (name != null) b.name = name;
            parent?.Add(b);
            return b;
        }

        /// <summary>Caption above a value, e.g. "EQUITY / $10,000.00". Returns the value label.</summary>
        public static Label Stat(string caption, VisualElement parent, string classes = "stat")
        {
            var box = Box(classes, parent);
            Label("stat-caption", box, caption);
            return Label("stat-value", box);
        }

        public static void SetText(TextElement element, string text)
        {
            if (element.text != text) element.text = text;
        }

        /// <summary>Applies up/down/flat color classes from the sign of a value.</summary>
        public static void SetSign(VisualElement e, decimal value)
        {
            e.EnableInClassList("up", value > 0m);
            e.EnableInClassList("down", value < 0m);
        }

        public static void Show(VisualElement e, bool visible) =>
            e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public static void AddClassesTo(VisualElement e, string classes) => AddClasses(e, classes);

        private static void AddClasses(VisualElement e, string classes)
        {
            if (string.IsNullOrEmpty(classes)) return;
            foreach (string c in classes.Split(' '))
                if (c.Length > 0) e.AddToClassList(c);
        }
    }
}
