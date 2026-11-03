using VoiceAgent.Core.Verification;

namespace VoiceAgent.Tests.Core;

public class SpokenDateParserTests
{
    [Theory]
    [InlineData("14th of March 1985")]
    [InlineData("14 March 1985")]
    [InlineData("the 14th of March, 1985")]
    [InlineData("March 14th 1985")]
    [InlineData("14/03/1985")]
    [InlineData("14-3-1985")]
    [InlineData("1985-03-14")]
    [InlineData("14 Mar 1985")]
    public void Parses_the_ways_callers_say_dates(string spoken)
    {
        Assert.True(SpokenDateParser.TryParse(spoken, out var date));
        Assert.Equal(new DateOnly(1985, 3, 14), date);
    }

    [Fact]
    public void Numeric_dates_are_day_first()
    {
        Assert.True(SpokenDateParser.TryParse("02/11/1972", out var date));
        Assert.Equal(new DateOnly(1972, 11, 2), date);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("sometime in spring")]
    [InlineData("31/02/1990")]
    public void Rejects_things_that_are_not_dates(string? spoken) =>
        Assert.False(SpokenDateParser.TryParse(spoken, out _));
}
