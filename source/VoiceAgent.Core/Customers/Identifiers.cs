using System.Text;

namespace VoiceAgent.Core.Customers;

/// <summary>
/// Normalises identifiers the way they arrive from speech-to-text, e.g. "one two three four" or "CS-1042".
/// </summary>
public static class Identifiers
{
    private static readonly Dictionary<string, char> SpokenDigits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = '0', ["oh"] = '0', ["o"] = '0', ["nought"] = '0',
        ["one"] = '1', ["two"] = '2', ["three"] = '3', ["four"] = '4', ["five"] = '5',
        ["six"] = '6', ["seven"] = '7', ["eight"] = '8', ["nine"] = '9',
    };

    /// <summary>Keeps letters and digits only, converting spoken digit words ("four") to digits.</summary>
    public static string AlphaNumeric(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var sb = new StringBuilder();
        foreach (var token in value.Split([' ', '-', ',', '.', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (SpokenDigits.TryGetValue(token, out var digit))
            {
                sb.Append(digit);
                continue;
            }

            foreach (var ch in token.Where(char.IsLetterOrDigit))
                sb.Append(char.ToUpperInvariant(ch));
        }

        return sb.ToString();
    }

    public static string NormaliseAccountNumber(string? value) => new(AlphaNumeric(value).Where(char.IsDigit).ToArray());

    public static string NormaliseCaseReference(string? value) => AlphaNumeric(value);

    public static string NormalisePostcode(string? value) => AlphaNumeric(value);

    public static string NormalisePhoneNumber(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        // Treat UK national format (07...) and international format (+447...) as the same number.
        if (digits.StartsWith("44")) digits = digits[2..];
        if (digits.StartsWith('0')) digits = digits[1..];
        return digits;
    }
}
