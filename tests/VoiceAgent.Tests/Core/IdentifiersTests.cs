using VoiceAgent.Core.Cases;

namespace VoiceAgent.Tests.Core;

public class IdentifiersTests
{
    [Theory]
    [InlineData("1042/2617")]
    [InlineData("1042 2617")]
    [InlineData("10422617")]
    [InlineData("1042-2617")]
    [InlineData("one oh four two stroke two six one seven")]
    [InlineData("one zero four two slash two six one seven")]
    public void Case_numbers_are_normalised_from_speech(string spoken)
    {
        Assert.Equal("10422617", Identifiers.NormaliseCaseNumber(spoken));
        Assert.Equal("1042/2617", Identifiers.FormatCaseNumber(spoken));
    }

    [Theory]
    [InlineData("ls1 4ap")]
    [InlineData("LS1 4AP")]
    [InlineData("L S 1 4 A P")]
    public void Postcodes_ignore_spacing_and_case(string spoken) =>
        Assert.Equal("LS14AP", Identifiers.NormalisePostcode(spoken));

    [Theory]
    [InlineData("O'Connor", "oconnor")]
    [InlineData("o connor", "oconnor")]
    [InlineData("Zoë", "zoe")]
    public void Names_ignore_punctuation_spacing_and_accents(string spoken, string expected) =>
        Assert.Equal(expected, Identifiers.NormaliseName(spoken));

    [Theory]
    [InlineData("+447700900101")]
    [InlineData("07700 900101")]
    [InlineData("447700900101")]
    public void Uk_phone_numbers_match_in_any_format(string number) =>
        Assert.Equal(Identifiers.NormalisePhoneNumber("+447700900101"), Identifiers.NormalisePhoneNumber(number));
}

public class CaseRepositoryTests
{
    private readonly InMemoryCaseRepository _repo = new(SampleData.LoadCases());

    [Fact]
    public async Task Finds_a_case_by_case_number() =>
        Assert.Equal("Purchase", (await _repo.FindByCaseNumberAsync("1042 2617"))!.MatterType);

    [Theory]
    [InlineData("LS6 2QT", "Hughes")]   // property postcode
    [InlineData("ls1 4ap", "hughes")]   // home postcode
    public async Task Finds_a_case_by_property_or_home_postcode_and_surname(string postcode, string surname)
    {
        var found = Assert.Single(await _repo.FindByPostcodeAndSurnameAsync(postcode, surname));
        Assert.Equal("1042/2617", found.CaseNumber);
    }

    [Fact]
    public async Task Surname_can_belong_to_any_party_on_a_joint_account()
    {
        var found = Assert.Single(await _repo.FindByPostcodeAndSurnameAsync("CF14 5RA", "Williams"));
        Assert.Equal(2, found.Parties.Count);
    }

    [Fact]
    public async Task Postcode_and_surname_must_both_match()
    {
        Assert.Empty(await _repo.FindByPostcodeAndSurnameAsync("LS6 2QT", "Patel"));
        Assert.Empty(await _repo.FindByPostcodeAndSurnameAsync("", "Hughes"));
    }

    [Fact]
    public async Task Caller_id_matches_any_party_on_the_account() =>
        Assert.Equal("1042/2617", (await _repo.FindByPhoneNumberAsync("07700 900101"))!.CaseNumber);
}
