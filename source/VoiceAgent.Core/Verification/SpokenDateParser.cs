using System.Globalization;
using System.Text.RegularExpressions;

namespace VoiceAgent.Core.Verification;

/// <summary>
/// Parses dates the way callers say them ("14th of March 1985", "14/03/1985", "March 14, 1985").
/// Day-first (UK) ordering is assumed for ambiguous numeric dates.
/// </summary>
public static partial class SpokenDateParser
{
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");

    private static readonly string[] Formats =
    [
        "yyyy-MM-dd", "d/M/yyyy", "d-M-yyyy", "d.M.yyyy", "d M yyyy",
        "d MMMM yyyy", "d MMM yyyy", "MMMM d yyyy", "MMM d yyyy", "d MMMM yy", "d/M/yy",
    ];

    public static bool TryParse(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var cleaned = Ordinals().Replace(value.Trim(), "$1");
        cleaned = Filler().Replace(cleaned, " ");
        cleaned = Whitespace().Replace(cleaned.Replace(",", " "), " ").Trim();

        return DateOnly.TryParseExact(cleaned, Formats, Uk, DateTimeStyles.AllowWhiteSpaces, out date);
    }

    [GeneratedRegex(@"(\d{1,2})(st|nd|rd|th)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Ordinals();

    [GeneratedRegex(@"\b(of|the)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Filler();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
