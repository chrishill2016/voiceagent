using VoiceAgent.Core.Cases;

namespace VoiceAgent.Core.Verification;

/// <summary>The caller's answers to the three security questions.</summary>
public sealed record SecurityAnswers(string? FullName, string? PreviousLender, string? DateOfBirth);

public enum VerificationDecision
{
    /// <summary>All answers matched.</summary>
    Passed,

    /// <summary>At least one answer was wrong but the caller can try again.</summary>
    RetryAllowed,

    /// <summary>Attempts used up: hand the caller to a person (never lock them out).</summary>
    TransferToAgent,
}

public sealed record VerificationResult(VerificationDecision Decision, int AttemptsUsed, int AttemptsRemaining);

/// <summary>
/// Checks security answers on the server so the voice model never sees the real answers.
/// The caller must give:
/// 1. the full name of anyone on the account,
/// 2. their previous mortgage lender,
/// 3. the date of birth of anyone on the account.
/// The result never says which answer was wrong.
/// </summary>
public static class IdentityVerifier
{
    public const int MaxAttempts = 3;

    private static readonly string[] NoLenderAnswers =
        ["none", "nobody", "noone", "no", "nolender", "noprevious", "nopreviouslender", "nomortgage", "neverhadone", "neverhadamortgage", "cash", "cashbuyer", "firsttimebuyer"];

    private static readonly string[] LenderFillerWords =
        ["the", "bank", "plc", "uk", "building", "society", "group", "mortgages", "mortgage", "home", "loans", "limited", "ltd", "of", "and", "&"];

    /// <summary>Common short names callers use for lenders.</summary>
    private static readonly Dictionary<string, string> LenderAliases = new(StringComparer.Ordinal)
    {
        ["rbs"] = "royalscotland",
        ["natwest"] = "nationalwestminster",
        ["boi"] = "ireland",
        ["nationalwestminster"] = "nationalwestminster",
    };

    public static bool AnswersMatch(ConveyancingCase account, SecurityAnswers answers) =>
        NameMatches(account, answers.FullName)
        && LenderMatches(account.PreviousLender, answers.PreviousLender)
        && DateOfBirthMatches(account, answers.DateOfBirth);

    public static VerificationResult Evaluate(ConveyancingCase account, SecurityAnswers answers, int previousFailedAttempts)
    {
        var attemptsUsed = previousFailedAttempts + 1;

        if (AnswersMatch(account, answers))
            return new VerificationResult(VerificationDecision.Passed, attemptsUsed, MaxAttempts - attemptsUsed);

        var remaining = Math.Max(0, MaxAttempts - attemptsUsed);
        return new VerificationResult(
            remaining > 0 ? VerificationDecision.RetryAllowed : VerificationDecision.TransferToAgent,
            attemptsUsed,
            remaining);
    }

    /// <summary>
    /// The first and last name of someone on the account, in order. Middle names and titles are allowed
    /// ("Mrs Amelia Jane Hughes" matches Amelia Hughes), but a first name or surname on its own is not enough.
    /// </summary>
    public static bool NameMatches(ConveyancingCase account, string? fullName)
    {
        var words = (fullName ?? string.Empty)
            .Split([' ', ',', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Identifiers.NormaliseName)
            .Where(w => w.Length > 0)
            .ToList();

        if (words.Count < 2) return false;

        return account.Parties.Any(p =>
        {
            var first = Identifiers.NormaliseName(p.FirstName);
            var last = Identifiers.NormaliseName(p.LastName);
            var firstIndex = words.IndexOf(first);
            return firstIndex >= 0 && words.Skip(firstIndex + 1).Contains(last)
                // Double-barrelled or multi-word surnames said as one ("Smith-Jones" → "smithjones").
                || firstIndex >= 0 && string.Concat(words.Skip(firstIndex + 1)) == last;
        });
    }

    public static bool DateOfBirthMatches(ConveyancingCase account, string? dateOfBirth) =>
        SpokenDateParser.TryParse(dateOfBirth, out var dob) && account.Parties.Any(p => p.DateOfBirth == dob);

    /// <summary>
    /// Tolerant lender match: ignores "bank", "building society", "plc" and so on, and accepts common
    /// short names (RBS, NatWest). If the client has never had a mortgage, answers like "none" match.
    /// </summary>
    public static bool LenderMatches(string? expected, string? answer)
    {
        var said = LenderKey(answer);
        if (said.Length == 0) return false;

        if (string.IsNullOrWhiteSpace(expected))
        {
            var compact = Compact(answer);
            return NoLenderAnswers.Contains(compact)
                || compact.Contains("never") || compact.Contains("firsttime")
                || compact.Contains("nomortgage") || compact.Contains("nolender") || compact.Contains("noprevious");
        }

        var actual = Alias(LenderKey(expected));
        said = Alias(said);

        // Allow a shortened name ("Nationwide" for Nationwide Building Society), but not a fragment.
        return said == actual
            || (said.Length >= 5 && actual.StartsWith(said, StringComparison.Ordinal))
            || (actual.Length >= 5 && said.StartsWith(actual, StringComparison.Ordinal));
    }

    private static string Alias(string key) => LenderAliases.GetValueOrDefault(key, key);

    private static string LenderKey(string? value) => string.Concat(
        (value ?? string.Empty).ToLowerInvariant()
            .Split([' ', ',', '.', '-', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Select(Identifiers.NormaliseName)
            .Where(w => w.Length > 0 && !LenderFillerWords.Contains(w)));

    private static string Compact(string? value) =>
        string.Concat((value ?? string.Empty).Where(char.IsLetter)).ToLowerInvariant();
}
