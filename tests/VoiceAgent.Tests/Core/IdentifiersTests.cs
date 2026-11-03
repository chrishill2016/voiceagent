using VoiceAgent.Core.Customers;

namespace VoiceAgent.Tests.Core;

public class IdentifiersTests
{
    [Theory]
    [InlineData("1023 4567", "10234567")]
    [InlineData("one oh two three four five six seven", "10234567")]
    [InlineData("10-23-45-67", "10234567")]
    public void Account_numbers_are_normalised_from_speech(string spoken, string expected) =>
        Assert.Equal(expected, Identifiers.NormaliseAccountNumber(spoken));

    [Theory]
    [InlineData("CS1042")]
    [InlineData("cs 1042")]
    [InlineData("C S one zero four two")]
    [InlineData("CS-1042.")]
    public void Case_references_are_normalised_from_speech(string spoken) =>
        Assert.Equal("CS1042", Identifiers.NormaliseCaseReference(spoken));

    [Theory]
    [InlineData("+447700900101")]
    [InlineData("07700 900101")]
    [InlineData("447700900101")]
    public void Uk_phone_numbers_match_in_any_format(string number) =>
        Assert.Equal(Identifiers.NormalisePhoneNumber("+447700900101"), Identifiers.NormalisePhoneNumber(number));

    [Fact]
    public void Account_numbers_are_masked() =>
        Assert.Equal("****4567", TestHarness.Amelia.MaskedAccountNumber);
}
