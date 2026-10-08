namespace IPS.MiidleWear.Contracts.Proxy;

/// <summary>
/// The "easy access" simple-JSON layer the core banking system talks to (doc §1.2.1 Registration,
/// Annex E p. 17-19 / §1.2.2 Update-Removal, p. 19-22 / §1.2.3 Account Holder Inquiry, p. 22-24,
/// mapped to plain DTOs) — this project builds the acmt.022 XML, signs it, and talks to the Proxy
/// Solution so the core system never has to. Scoped for the first vertical slice: Georgian-language
/// name fields only (the "Other Language" variants doc §1.1.1 Account Holder (p. 9-10) / §1.1.3
/// Authorized Person (p. 11-12) / §1.1.4 Beneficial Owner (p. 12-13) also define are modeled in
/// IPS.MiidleWear.MockProxy already and can be added here later without changing the wire shape,
/// since they're purely additive optional fields).
///
/// document: GE_IPS_Inception_Report_Annex_E_Proxy-Participant_Interface_v.1.00.pdf, page: 9-24
/// </summary>
public sealed record ProxyIdentifierDto(string Type, string Alias);

/// <summary>Shared shape of authorized persons (doc §1.1.3) and beneficial owners (doc §1.1.4) so both can be validated by one rule set; not part of the JSON contract.</summary>
public interface IProxyPersonDto
{
    string Id { get; }
    string GivenNameKa { get; }
    string SurnameKa { get; }
    string? MiddleNameKa { get; }
    bool? Woman { get; }
    string? Citizenship { get; }
    string? CountryOfResidence { get; }
    DateOnly? FromDate { get; }
    DateOnly? ToDate { get; }
}

public sealed record ProxyAuthorizedPersonDto(
    string Id, string GivenNameKa, string SurnameKa, string? MiddleNameKa,
    bool? Woman, string? Citizenship, string? CountryOfResidence,
    DateOnly? FromDate, DateOnly? ToDate) : IProxyPersonDto;

public sealed record ProxyBeneficialOwnerDto(
    string Id, string GivenNameKa, string SurnameKa, string? MiddleNameKa,
    bool? Woman, string? Citizenship, string? CountryOfResidence,
    DateOnly? FromDate, DateOnly? ToDate) : IProxyPersonDto;

/// <summary>Identifies the account holder being registered/updated/removed (doc §1.1.1, Annex E p. 9-10). Type is "Individual" or "LegalEntity"; the Individual-only or LegalEntity-only fields are simply left null for the other kind.</summary>
public sealed record ProxyAccountHolderDto(
    string Identifier,
    string Type,
    string? GivenNameKa, string? SurnameKa, string? MiddleNameKa,
    bool? Woman, string? Citizenship, string? CountryOfResidence,
    string? LegalFormKa, string? LegalNameKa,
    bool? WomenLedBusiness, string? CountryOfRegistration);

/// <summary>The account being linked to the account holder (doc §1.1.2, Annex E p. 10-11). IsIban selects whether Identifier is rendered as an IBAN or an Othr/Id element in the outbound XML.</summary>
public sealed record ProxyAccountDto(
    string Identifier, bool IsIban, string Currency, string Type,
    DateOnly? OpeningDate, DateOnly? ClosingDate);

/// <summary>Doc §1.2.1 Registration Operation request (Annex E p. 17-19). Account, ProxyIdentifiers, AuthorizedPersons and BeneficialOwners are all optional — a call can register just the account holder, or the holder plus everything else, in one shot.</summary>
public sealed record ProxyRegisterRequestDto(
    ProxyAccountHolderDto AccountHolder,
    ProxyAccountDto? Account,
    IReadOnlyList<ProxyIdentifierDto>? ProxyIdentifiers,
    IReadOnlyList<ProxyAuthorizedPersonDto>? AuthorizedPersons,
    IReadOnlyList<ProxyBeneficialOwnerDto>? BeneficialOwners);

/// <summary>Doc §1.2.2 Update Operation request (Annex E p. 19-21). AccountHolderIdentifier/AccountHolderType identify the existing record to update; every other field is optional and only sent when the caller actually wants to change it.</summary>
public sealed record ProxyUpdateRequestDto(
    string AccountHolderIdentifier,
    string AccountHolderType,
    ProxyAccountHolderDto? UpdatedAccountHolder,
    string? AccountIdentifier,
    string? AccountCurrency,
    ProxyAccountDto? UpdatedAccount,
    IReadOnlyList<ProxyIdentifierDto>? ProxyIdentifiers,
    IReadOnlyList<ProxyAuthorizedPersonDto>? AuthorizedPersons,
    IReadOnlyList<ProxyBeneficialOwnerDto>? BeneficialOwners);

/// <summary>Doc §1.2.2 Removal Operation request (Annex E p. 21-22).</summary>
public sealed record ProxyRemoveRequestDto(
    string AccountHolderIdentifier,
    string AccountHolderType,
    string AccountIdentifier,
    string AccountCurrency,
    // true: remove only the listed sub-items, account stays active. false: remove the whole account
    // (doc §3.26, Annex E acmt.022 Removal field table p. 40-43).
    bool KeepAccountActive,
    IReadOnlyList<ProxyIdentifierDto>? ProxyIdentifiersToRemove,
    IReadOnlyList<string>? AuthorizedPersonIdsToRemove,
    IReadOnlyList<string>? BeneficialOwnerIdsToRemove);

/// <summary>Plain accept/reject result for a Register/Update/Remove call — Accepted mirrors the acmt.022/pacs.002 TxSts, ErrorCode/Description are populated from the pacs.002 StsRsnInf when Accepted is false.</summary>
public sealed record ProxyOperationResultDto(bool Accepted, string? ErrorCode, string? Description)
{
    public static ProxyOperationResultDto Accept() => new(true, null, null);
    public static ProxyOperationResultDto Reject(string? errorCode, string? description) => new(false, errorCode, description);
}
