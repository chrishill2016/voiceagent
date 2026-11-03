using System.Text.RegularExpressions;

namespace VoiceAgent.Api.Features.Agents;

/// <summary>Validates where an agent's calls are transferred to.</summary>
public static partial class TransferTargets
{
    /// <summary>
    /// Accepts an international number (+447700900123), a UK national number (07700 900123, converted to +44),
    /// or a raw ACS / Teams identifier (8:acs:..., 8:orgid:...).
    /// </summary>
    public static bool TryNormalise(string? input, out string target, out string error)
    {
        target = string.Empty;
        error = string.Empty;
        var value = (input ?? string.Empty).Trim();

        if (value.StartsWith("8:", StringComparison.Ordinal))
        {
            if (value.Length > 6)
            {
                target = value;
                return true;
            }

            error = "That Teams or ACS identifier looks incomplete.";
            return false;
        }

        var compact = Separators().Replace(value, string.Empty);
        if (compact.StartsWith("00")) compact = "+" + compact[2..];
        if (compact.StartsWith('0')) compact = "+44" + compact[1..];

        if (E164().IsMatch(compact))
        {
            target = compact;
            return true;
        }

        error = "Enter a phone number such as +44 7700 900123, or a Teams ID starting with 8:orgid:.";
        return false;
    }

    [GeneratedRegex(@"[\s\-\(\)\.]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")]
    private static partial Regex E164();
}
