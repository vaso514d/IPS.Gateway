namespace IPS.Middleware.Application.Proxy;

// The holder is an individual (PrvtId) or a legal entity (OrgId).
public static class ProxyHolderTypes
{
    public static bool IsIndividual(string? type) => string.Equals(type, "Individual", StringComparison.OrdinalIgnoreCase);

    public static bool IsHolderType(string? type) => IsIndividual(type) || string.Equals(type, "LegalEntity", StringComparison.OrdinalIgnoreCase);
}

// The Proxy Solution requests as the core system sends them (Annex E 1.2.1 and 1.2.2). Values are validated, not trusted.
public sealed record ProxyIdentifier(string? Type, string? Alias);

// An authorized person or a beneficial owner; both have the same fields.
public sealed record ProxyPerson(
    string? Id,
    string? GivenName,
    string? Surname,
    string? MiddleName,
    bool? Woman,
    string? Citizenship,
    string? CountryOfResidence,
    DateOnly? FromDate,
    DateOnly? ToDate);

public sealed record ProxyAccountHolder(
    string? Identifier,
    string? Type,
    string? GivenName,
    string? Surname,
    string? MiddleName,
    bool? Woman,
    string? Citizenship,
    string? CountryOfResidence,
    string? LegalForm,
    string? LegalName,
    bool? WomenLedBusiness,
    string? CountryOfRegistration);

public sealed record ProxyAccount(
    string? Identifier,
    bool IsIban,
    string? Currency,
    string? Type,
    DateOnly? OpeningDate,
    DateOnly? ClosingDate);

public sealed record RegisterProxyRequest(
    ProxyAccountHolder? AccountHolder,
    ProxyAccount? Account,
    IReadOnlyList<ProxyIdentifier?>? ProxyIdentifiers,
    IReadOnlyList<ProxyPerson?>? AuthorizedPersons,
    IReadOnlyList<ProxyPerson?>? BeneficialOwners);

public sealed record UpdateProxyRequest(
    string? AccountHolderIdentifier,
    string? AccountHolderType,
    ProxyAccountHolder? UpdatedAccountHolder,
    string? AccountIdentifier,
    string? AccountCurrency,
    ProxyAccount? UpdatedAccount,
    IReadOnlyList<ProxyIdentifier?>? ProxyIdentifiers,
    IReadOnlyList<ProxyPerson?>? AuthorizedPersons,
    IReadOnlyList<ProxyPerson?>? BeneficialOwners);

// KeepAccountActive removes only the listed sub-items; otherwise the whole account is removed.
public sealed record RemoveProxyRequest(
    string? AccountHolderIdentifier,
    string? AccountHolderType,
    string? AccountIdentifier,
    string? AccountCurrency,
    bool KeepAccountActive,
    IReadOnlyList<ProxyIdentifier?>? ProxyIdentifiersToRemove,
    IReadOnlyList<string?>? AuthorizedPersonIdsToRemove,
    IReadOnlyList<string?>? BeneficialOwnerIdsToRemove);
