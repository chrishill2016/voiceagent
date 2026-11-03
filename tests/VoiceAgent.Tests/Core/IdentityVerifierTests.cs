using VoiceAgent.Core.Verification;

namespace VoiceAgent.Tests.Core;

/// <summary>Security: full name of anyone on the account, previous lender, date of birth of anyone on the account.</summary>
public class IdentityVerifierTests
{
    // Case 1042/2617: Amelia Hughes (14/03/1985) and Tom Hughes (02/08/1983), previous lender Nationwide Building Society.
    private static readonly VoiceAgent.Core.Cases.ConveyancingCase Hughes = TestHarness.HughesPurchase;

    private static readonly SecurityAnswers Correct = new("Amelia Hughes", "Nationwide", "14th March 1985");

    [Fact]
    public void Correct_answers_pass()
    {
        var result = IdentityVerifier.Evaluate(Hughes, Correct, previousFailedAttempts: 0);
        Assert.Equal(VerificationDecision.Passed, result.Decision);
    }

    [Fact]
    public void Name_and_date_of_birth_can_each_be_for_anyone_on_the_account() =>
        Assert.True(IdentityVerifier.AnswersMatch(Hughes, new SecurityAnswers("Tom Hughes", "Nationwide Building Society", "14/03/1985")));

    [Theory]
    [InlineData("Amelia Hughes")]
    [InlineData("amelia hughes")]
    [InlineData("Mrs Amelia Jane Hughes")]
    [InlineData("Tom Hughes")]
    public void Full_name_accepts_titles_middle_names_and_any_party(string name) =>
        Assert.True(IdentityVerifier.NameMatches(Hughes, name));

    [Theory]
    [InlineData("Hughes")]
    [InlineData("Amelia")]
    [InlineData("Hughes Amelia")]
    [InlineData("Amelia Smith")]
    [InlineData("")]
    public void Full_name_needs_first_name_then_surname_of_someone_on_the_account(string name) =>
        Assert.False(IdentityVerifier.NameMatches(Hughes, name));

    [Theory]
    [InlineData("Nationwide Building Society", "Nationwide", true)]
    [InlineData("Nationwide Building Society", "nationwide building society", true)]
    [InlineData("Nationwide Building Society", "the Nationwide", true)]
    [InlineData("Royal Bank of Scotland", "RBS", true)]
    [InlineData("Royal Bank of Scotland", "Royal Bank of Scotland plc", true)]
    [InlineData("Bank of Ireland", "Bank of Ireland", true)]
    [InlineData("Santander", "Santander UK", true)]
    [InlineData("Santander", "Halifax", false)]
    [InlineData("Nationwide Building Society", "Nat", false)]
    [InlineData("Nationwide Building Society", "Bank", false)]
    [InlineData("Barclays", "", false)]
    public void Previous_lender_matching_is_tolerant_of_how_people_say_it(string expected, string said, bool matches) =>
        Assert.Equal(matches, IdentityVerifier.LenderMatches(expected, said));

    [Theory]
    [InlineData("none", true)]
    [InlineData("I've never had a mortgage", true)]
    [InlineData("first-time buyer", true)]
    [InlineData("Halifax", false)]
    public void Clients_who_never_had_a_mortgage_answer_none(string said, bool matches) =>
        Assert.Equal(matches, IdentityVerifier.LenderMatches(null, said));

    [Theory]
    [InlineData("Amelia Hughes", "Nationwide", "15th March 1985")]
    [InlineData("Amelia Hughes", "Halifax", "14th March 1985")]
    [InlineData("Amelia Smith", "Nationwide", "14th March 1985")]
    [InlineData(null, null, null)]
    public void Any_wrong_answer_fails(string? name, string? lender, string? dob) =>
        Assert.False(IdentityVerifier.AnswersMatch(Hughes, new SecurityAnswers(name, lender, dob)));

    [Theory]
    [InlineData(0, VerificationDecision.RetryAllowed, 2)]
    [InlineData(1, VerificationDecision.RetryAllowed, 1)]
    [InlineData(2, VerificationDecision.TransferToAgent, 0)]
    public void Failures_allow_retries_then_transfer_to_an_agent_instead_of_locking_out(int previous, VerificationDecision expected, int remaining)
    {
        var result = IdentityVerifier.Evaluate(Hughes, new SecurityAnswers("X Y", "Z", "1 Jan 2000"), previous);
        Assert.Equal(expected, result.Decision);
        Assert.Equal(remaining, result.AttemptsRemaining);
    }

    [Fact]
    public void Caller_can_still_pass_on_the_last_attempt() =>
        Assert.Equal(VerificationDecision.Passed, IdentityVerifier.Evaluate(Hughes, Correct, previousFailedAttempts: 2).Decision);
}
