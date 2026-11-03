namespace VoiceAgent.Core.Cases;

public interface ICaseRepository
{
    Task<ConveyancingCase?> FindByCaseNumberAsync(string caseNumber, CancellationToken ct = default);

    /// <summary>
    /// Cases where someone on the account has this surname and the postcode matches either the
    /// property or the client's correspondence address. May return more than one.
    /// </summary>
    Task<IReadOnlyList<ConveyancingCase>> FindByPostcodeAndSurnameAsync(string postcode, string surname, CancellationToken ct = default);

    Task<ConveyancingCase?> FindByPhoneNumberAsync(string phoneNumber, CancellationToken ct = default);
    Task<IReadOnlyList<ConveyancingCase>> ListAsync(CancellationToken ct = default);
}
