using System.Globalization;

namespace EvidenceHound.Core.Scope;

/// <summary>A whole number of seconds written as <c>30m</c>, <c>4h</c>, <c>2d</c> or <c>1w</c> (s15-04): recovery objectives and escalation points.</summary>
public readonly record struct Duration(int Seconds)
{
    private static readonly (char Unit, int Seconds)[] Units = [('w', 604800), ('d', 86400), ('h', 3600), ('m', 60)];

    /// <summary>A positive whole number and one unit: <c>m</c>, <c>h</c>, <c>d</c> or <c>w</c>.</summary>
    public static bool TryParse(string? text, out Duration duration)
    {
        duration = default;
        if (text is not { Length: >= 2 } || Array.FindIndex(Units, u => u.Unit == text[^1]) is var index && index < 0
            || !int.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
        {
            return false;
        }

        duration = new Duration(checked(count * Units[index].Seconds));
        return true;
    }

    /// <summary>The largest unit that divides the seconds exactly.</summary>
    public override string ToString()
    {
        var seconds = Seconds;
        var (unit, size) = Units.FirstOrDefault(u => seconds % u.Seconds == 0 && seconds >= u.Seconds);
        return size == 0 ? string.Create(CultureInfo.InvariantCulture, $"{seconds}s") : string.Create(CultureInfo.InvariantCulture, $"{seconds / size}{unit}");
    }
}
