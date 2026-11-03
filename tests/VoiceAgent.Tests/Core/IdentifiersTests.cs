using VoiceAgent.Core.Customers;

namespace VoiceAgent.Tests.Core;

public class IdentifiersTests
{
    [Theory]
    [InlineData("1023 4567", "10234567")]
    [InlineData("one oh two three four five six seven", "10234567")]
    [InlineData("10-23-45-67", "10234567")]
    public void Account_numbers_are_normalised_from_speech(string spoken, string expected) =>
        Assert.Equal(expected, Identifiers.NormaliseClientNumber(spoken));

    [Theory]
    [InlineData("CV2041")]
    [InlineData("cv 2041")]
    [InlineData("C V two oh four one")]
    [InlineData("CV-2041.")]
    public void Case_references_are_normalised_from_speech(string spoken) =>
        Assert.Equal("CV2041", Identifiers.NormaliseCaseReference(spoken));

    [Theory]
    [InlineData("+447700900101")]
    [InlineData("07700 900101")]
    [InlineData("447700900101")]
    public void Uk_phone_numbers_match_in_any_format(string number) =>
        Assert.Equal(Identifiers.NormalisePhoneNumber("+447700900101"), Identifiers.NormalisePhoneNumber(number));

    [Fact]
    public void Account_numbers_are_masked() =>
        Assert.Equal("****4567", TestHarness.Amelia.MaskedClientNumber);
}
