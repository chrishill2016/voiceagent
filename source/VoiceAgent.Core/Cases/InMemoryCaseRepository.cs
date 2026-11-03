namespace VoiceAgent.Core.Cases;

/// <summary>Case store backed by a fixed list. Used for local development, tests and small datasets.</summary>
public sealed class InMemoryCaseRepository(IEnumerable<ConveyancingCase> cases) : ICaseRepository
{
    private readonly IReadOnlyList<ConveyancingCase> _cases = cases.ToList();

    public Task<ConveyancingCase?> FindByCaseNumberAsync(string caseNumber, CancellationToken ct = default)
    {
        var wanted = Identifiers.NormaliseCaseNumber(caseNumber);
        return Task.FromResult(wanted.Length == 0
            ? null
            : _cases.FirstOrDefault(c => Identifiers.NormaliseCaseNumber(c.CaseNumber) == wanted));
    }

    public Task<IReadOnlyList<ConveyancingCase>> FindByPostcodeAndSurnameAsync(string postcode, string surname, CancellationToken ct = default)
    {
        var wantedPostcode = Identifiers.NormalisePostcode(postcode);
        var wantedSurname = Identifiers.NormaliseName(surname);
        if (wantedPostcode.Length == 0 || wantedSurname.Length == 0)
            return Task.FromResult<IReadOnlyList<ConveyancingCase>>([]);

        return Task.FromResult<IReadOnlyList<ConveyancingCase>>(_cases
            .Where(c => Identifiers.NormalisePostcode(c.PropertyPostcode) == wantedPostcode
                     || Identifiers.NormalisePostcode(c.Postcode) == wantedPostcode)
            .Where(c => c.Parties.Any(p => Identifiers.NormaliseName(p.LastName) == wantedSurname))
            .ToList());
    }

    public Task<ConveyancingCase?> FindByPhoneNumberAsync(string phoneNumber, CancellationToken ct = default)
    {
        var wanted = Identifiers.NormalisePhoneNumber(phoneNumber);
        return Task.FromResult(wanted.Length == 0
            ? null
            : _cases.FirstOrDefault(c => c.Parties.Any(p => Identifiers.NormalisePhoneNumber(p.PhoneNumber) == wanted)));
    }

    public Task<IReadOnlyList<ConveyancingCase>> ListAsync(CancellationToken ct = default) => Task.FromResult(_cases);
}
