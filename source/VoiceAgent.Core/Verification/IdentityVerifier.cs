using VoiceAgent.Core.Customers;

namespace VoiceAgent.Core.Verification;

public sealed record SecurityAnswers(string? DateOfBirth, string? Postcode, string? MemorableWord);

public enum VerificationDecision
{
    /// <summary>All answers matched.</summary>
    Passed,

    /// <summary>At least one answer was wrong but the caller can try again.</summary>
    RetryAllowed,

    /// <summary>Attempts used up: hand the caller to a human agent (never lock them out).</summary>
    TransferToAgent,
}

public sealed record VerificationResult(VerificationDecision Decision, int AttemptsUsed, int AttemptsRemaining);

/// <summary>
/// Checks security answers on the server so the voice model never sees the real answers.
/// The result never says which answer was wrong.
/// </summary>
public static class IdentityVerifier
{
    public const int MaxAttempts = 3;

    public static bool AnswersMatch(Customer customer, SecurityAnswers answers)
    {
        var dobMatches = SpokenDateParser.TryParse(answers.DateOfBirth, out var dob) && dob == customer.DateOfBirth;

        var postcodeMatches = Identifiers.NormalisePostcode(answers.Postcode) is { Length: > 0 } postcode
            && postcode == Identifiers.NormalisePostcode(customer.Postcode);

        var wordMatches = NormaliseWord(answers.MemorableWord) is { Length: > 0 } word
            && word == NormaliseWord(customer.MemorableWord);

        return dobMatches && postcodeMatches && wordMatches;
    }

    public static VerificationResult Evaluate(Customer customer, SecurityAnswers answers, int previousFailedAttempts)
    {
        var attemptsUsed = previousFailedAttempts + 1;

        if (AnswersMatch(customer, answers))
            return new VerificationResult(VerificationDecision.Passed, attemptsUsed, MaxAttempts - attemptsUsed);

        var remaining = Math.Max(0, MaxAttempts - attemptsUsed);
        return new VerificationResult(
            remaining > 0 ? VerificationDecision.RetryAllowed : VerificationDecision.TransferToAgent,
            attemptsUsed,
            remaining);
    }

    private static string NormaliseWord(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
