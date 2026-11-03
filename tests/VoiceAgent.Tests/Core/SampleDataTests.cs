using VoiceAgent.Core.Customers;

namespace VoiceAgent.Tests.Core;

public class SampleDataTests
{
    [Fact]
    public void Every_sample_client_has_conveyancing_matters_with_unique_references()
    {
        var clients = SampleData.LoadCustomers();
        var matters = clients.SelectMany(c => c.Cases).ToList();

        Assert.True(clients.Count >= 5);
        Assert.All(clients, c => Assert.NotEmpty(c.Cases));
        Assert.Equal(matters.Count, matters.Select(m => m.Reference).Distinct().Count());
        Assert.All(matters, m => Assert.Matches("^CV[0-9]{4}$", m.Reference));
        Assert.All(matters, m => Assert.NotEmpty(m.Milestones));
        Assert.All(matters.Where(m => !m.IsCompleted), m => Assert.False(string.IsNullOrWhiteSpace(m.NextStep)));
    }
}
