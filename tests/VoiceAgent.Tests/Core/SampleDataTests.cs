using VoiceAgent.Core.Cases;

namespace VoiceAgent.Tests.Core;

public class SampleDataTests
{
    [Fact]
    public void Sample_cases_use_the_firms_case_number_format_and_are_complete()
    {
        var cases = SampleData.LoadCases();

        Assert.True(cases.Count >= 5);
        Assert.Equal(cases.Count, cases.Select(c => c.CaseNumber).Distinct().Count());
        Assert.All(cases, c => Assert.Matches("^[0-9]{4}/[0-9]{4}$", c.CaseNumber));
        Assert.All(cases, c => Assert.NotEmpty(c.Parties));
        Assert.All(cases, c => Assert.NotEmpty(c.Milestones));
        Assert.All(cases.Where(c => !c.IsCompleted), c => Assert.False(string.IsNullOrWhiteSpace(c.NextStep)));
        Assert.Contains(cases, c => c.Parties.Count > 1);
        Assert.Contains(cases, c => c.PreviousLender is null);
    }

    [Fact]
    public void Dates_fall_on_working_days()
    {
        var dates = SampleData.LoadCases().SelectMany(c => new[] { c.OpenedOn, c.LastUpdatedOn, c.TargetExchangeDate, c.CompletionDate }
            .Concat(c.Milestones.Select(m => m.CompletedOn)))
            .OfType<DateOnly>();

        Assert.All(dates, d => Assert.DoesNotContain(d.DayOfWeek, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }));
    }
}
