using VoiceAgent.Core.Verification;

namespace VoiceAgent.Tests.Core;

public class IdentityVerifierTests
{
    private static readonly SecurityAnswers Correct = new("14th March 1985", "ls1 4ap", "Lighthouse");

    [Fact]
    public void Correct_answers_pass_with_tolerant_formatting()
    {
        var result = IdentityVerifier.Evaluate(TestHarness.Amelia, Correct, previousFailedAttempts: 0);
        Assert.Equal(VerificationDecision.Passed, result.Decision);
    }

    [Theory]
    [InlineData("15th March 1985", "LS1 4AP", "lighthouse")]
    [InlineData("14th March 1985", "LS1 4AQ", "lighthouse")]
    [InlineData("14th March 1985", "LS1 4AP", "lightning")]
    [InlineData("14th March 1985", "", "lighthouse")]
    [InlineData(null, null, null)]
    public void Any_wrong_answer_fails(string? dob, string? postcode, string? word) =>
        Assert.False(IdentityVerifier.AnswersMatch(TestHarness.Amelia, new SecurityAnswers(dob, postcode, word)));

    [Theory]
    [InlineData(0, VerificationDecision.RetryAllowed, 2)]
    [InlineData(1, VerificationDecision.RetryAllowed, 1)]
    [InlineData(2, VerificationDecision.TransferToAgent, 0)]
    public void Failures_allow_retries_then_transfer_to_an_agent_instead_of_locking_out(int previous, VerificationDecision expected, int remaining)
    {
        var result = IdentityVerifier.Evaluate(TestHarness.Amelia, new SecurityAnswers("1 Jan 2000", "X", "Y"), previous);
        Assert.Equal(expected, result.Decision);
        Assert.Equal(remaining, result.AttemptsRemaining);
    }

    [Fact]
    public void Caller_can_still_pass_on_the_last_attempt()
    {
        var result = IdentityVerifier.Evaluate(TestHarness.Amelia, Correct, previousFailedAttempts: 2);
        Assert.Equal(VerificationDecision.Passed, result.Decision);
    }
}
