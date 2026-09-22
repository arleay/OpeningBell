using System;

namespace OpeningBell
{
    /// <summary>Marks a serialized reference that may legitimately stay empty (the scene reference guard skips it).</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class OptionalAttribute : Attribute
    {
    }
}
