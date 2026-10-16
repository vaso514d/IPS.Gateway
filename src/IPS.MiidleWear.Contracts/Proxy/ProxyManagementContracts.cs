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
/// <param name="Type">The proxy channel type (CtctDtls/Othr/ChanlTp), for example a mobile number or an e-mail.</param>
/// <param name="Alias">The proxy value itself (CtctDtls/Othr/Id): the number, address or other alias.</param>
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

/// <summary>A person authorized on the account (doc §1.1.3, Annex E p. 11-12), sent as AuthPer.</summary>
/// <param name="Id">The person's identifier (Id).</param>
/// <param name="GivenNameKa">Given name in Georgian.</param>
/// <param name="SurnameKa">Surname in Georgian.</param>
/// <param name="MiddleNameKa">Middle name in Georgian, when the person has one.</param>
/// <param name="Woman">Gender (Gndr): true for a woman, false for a man, null when not given.</param>
/// <param name="Citizenship">Citizenship country code (Ctznsh), when given.</param>
/// <param name="CountryOfResidence">Country of residence code (CtryOfRes), when given.</param>
/// <param name="FromDate">Start of the person's authorization or ownership (FrDt), when given.</param>
/// <param name="ToDate">End of the person's authorization or ownership (ToDt), when given.</param>
public sealed record ProxyAuthorizedPersonDto(
    string Id, string GivenNameKa, string SurnameKa, string? MiddleNameKa,
    bool? Woman, string? Citizenship, string? CountryOfResidence,
    DateOnly? FromDate, DateOnly? ToDate) : IProxyPersonDto;

/// <summary>A beneficial owner of the account holder (doc §1.1.4, Annex E p. 12-13), sent as BfyOwnr.</summary>
/// <param name="Id">The person's identifier (Id).</param>
/// <param name="GivenNameKa">Given name in Georgian.</param>
/// <param name="SurnameKa">Surname in Georgian.</param>
/// <param name="MiddleNameKa">Middle name in Georgian, when the person has one.</param>
/// <param name="Woman">Gender (Gndr): true for a woman, false for a man, null when not given.</param>
/// <param name="Citizenship">Citizenship country code (Ctznsh), when given.</param>
/// <param name="CountryOfResidence">Country of residence code (CtryOfRes), when given.</param>
/// <param name="FromDate">Start of the person's authorization or ownership (FrDt), when given.</param>
/// <param name="ToDate">End of the person's authorization or ownership (ToDt), when given.</param>
public sealed record ProxyBeneficialOwnerDto(
    string Id, string GivenNameKa, string SurnameKa, string? MiddleNameKa,
    bool? Woman, string? Citizenship, string? CountryOfResidence,
    DateOnly? FromDate, DateOnly? ToDate) : IProxyPersonDto;

/// <summary>Identifies the account holder being registered/updated/removed (doc §1.1.1, Annex E p. 9-10). Type is "Individual" or "LegalEntity"; the Individual-only or LegalEntity-only fields are simply left null for the other kind.</summary>
/// <param name="Identifier">The holder's identification (PrvtId or OrgId Othr/Id): personal number or registration number.</param>
/// <param name="Type">Individual or LegalEntity.</param>
/// <param name="GivenNameKa">Individual: given name in Georgian.</param>
/// <param name="SurnameKa">Individual: surname in Georgian.</param>
/// <param name="MiddleNameKa">Individual: middle name in Georgian, when given.</param>
/// <param name="Woman">Individual: gender (Gndr), true for a woman, false for a man, when given.</param>
/// <param name="Citizenship">Individual: citizenship country code (Ctznsh), when given.</param>
/// <param name="CountryOfResidence">Individual: country of residence code (CtryOfRes), when given.</param>
/// <param name="LegalFormKa">Legal entity: legal form in Georgian (LglForm), when given.</param>
/// <param name="LegalNameKa">Legal entity: legal name in Georgian.</param>
/// <param name="WomenLedBusiness">Legal entity: whether it is a women-led business (WlB), when given.</param>
/// <param name="CountryOfRegistration">Legal entity: country of registration code (RegnCtry), when given.</param>
public sealed record ProxyAccountHolderDto(
    string Identifier,
    string Type,
    string? GivenNameKa, string? SurnameKa, string? MiddleNameKa,
    bool? Woman, string? Citizenship, string? CountryOfResidence,
    string? LegalFormKa, string? LegalNameKa,
    bool? WomenLedBusiness, string? CountryOfRegistration);

/// <summary>The account being linked to the account holder (doc §1.1.2, Annex E p. 10-11). IsIban selects whether Identifier is rendered as an IBAN or an Othr/Id element in the outbound XML.</summary>
/// <param name="Identifier">The account number: an IBAN, or another identifier when IsIban is false.</param>
/// <param name="IsIban">true: Identifier is sent as IBAN; false: as Othr/Id.</param>
/// <param name="Currency">The account currency code (Ccy).</param>
/// <param name="Type">The account type (Tp/Prtry), when given.</param>
/// <param name="OpeningDate">When the account was opened (OpngDt), when given.</param>
/// <param name="ClosingDate">When the account was or will be closed (ClsgDt), when given.</param>
public sealed record ProxyAccountDto(
    string Identifier, bool IsIban, string Currency, string Type,
    DateOnly? OpeningDate, DateOnly? ClosingDate);

/// <summary>Doc §1.2.1 Registration Operation request (Annex E p. 17-19). Account, ProxyIdentifiers, AuthorizedPersons and BeneficialOwners are all optional — a call can register just the account holder, or the holder plus everything else, in one shot.</summary>
/// <param name="AccountHolder">The account holder to register.</param>
/// <param name="Account">The account to link to the holder, when registered in the same call.</param>
/// <param name="ProxyIdentifiers">The proxy identifiers (aliases) to register, when any.</param>
/// <param name="AuthorizedPersons">The persons authorized on the account, when any.</param>
/// <param name="BeneficialOwners">The beneficial owners of the holder, when any.</param>
public sealed record ProxyRegisterRequestDto(
    ProxyAccountHolderDto AccountHolder,
    ProxyAccountDto? Account,
    IReadOnlyList<ProxyIdentifierDto>? ProxyIdentifiers,
    IReadOnlyList<ProxyAuthorizedPersonDto>? AuthorizedPersons,
    IReadOnlyList<ProxyBeneficialOwnerDto>? BeneficialOwners);

/// <summary>Doc §1.2.2 Update Operation request (Annex E p. 19-21). AccountHolderIdentifier/AccountHolderType identify the existing record to update; every other field is optional and only sent when the caller actually wants to change it.</summary>
/// <param name="AccountHolderIdentifier">The identification of the registered holder to update.</param>
/// <param name="AccountHolderType">The registered holder's type: Individual or LegalEntity.</param>
/// <param name="UpdatedAccountHolder">The holder's new details, when they change.</param>
/// <param name="AccountIdentifier">The IBAN of the registered account the update is about, when it is about an account.</param>
/// <param name="AccountCurrency">The currency of that account.</param>
/// <param name="UpdatedAccount">The account's new details, when they change.</param>
/// <param name="ProxyIdentifiers">The proxy identifiers to register on the account, when any.</param>
/// <param name="AuthorizedPersons">The authorized persons to add or change, when any.</param>
/// <param name="BeneficialOwners">The beneficial owners to add or change, when any.</param>
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
/// <param name="AccountHolderIdentifier">The identification of the registered holder.</param>
/// <param name="AccountHolderType">The registered holder's type: Individual or LegalEntity.</param>
/// <param name="AccountIdentifier">The IBAN of the account.</param>
/// <param name="AccountCurrency">The currency of the account.</param>
/// <param name="KeepAccountActive">true: remove only the listed items and keep the account; false: remove the whole account with everything linked to it.</param>
/// <param name="ProxyIdentifiersToRemove">With KeepAccountActive, the proxy identifiers to remove.</param>
/// <param name="AuthorizedPersonIdsToRemove">With KeepAccountActive, the ids of the authorized persons to remove.</param>
/// <param name="BeneficialOwnerIdsToRemove">With KeepAccountActive, the ids of the beneficial owners to remove.</param>
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
/// <param name="Accepted">true when the Proxy Solution accepted the operation.</param>
/// <param name="ErrorCode">The Proxy Solution's reason code of a rejection (StsRsnInf).</param>
/// <param name="Description">The Proxy Solution's description of a rejection, when given.</param>
public sealed record ProxyOperationResultDto(bool Accepted, string? ErrorCode, string? Description)
{
    public static ProxyOperationResultDto Accept() => new(true, null, null);
    public static ProxyOperationResultDto Reject(string? errorCode, string? description) => new(false, errorCode, description);
}
