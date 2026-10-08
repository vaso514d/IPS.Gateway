using IPS.Middleware.Application.Proxy;
using IPS.MiidleWear.Contracts.Proxy;

namespace IPS.Middleware.Api.Proxy;

// Maps the imported Contracts to the Application requests. Missing parts stay missing; validation reports them.
internal static class ProxyRequestMapping
{
    public static RegisterProxyRequest Map(ProxyRegisterRequestDto request) => new(
        Holder(request.AccountHolder),
        Account(request.Account),
        Identifiers(request.ProxyIdentifiers),
        Persons(request.AuthorizedPersons),
        Persons(request.BeneficialOwners));

    public static UpdateProxyRequest Map(ProxyUpdateRequestDto request) => new(
        request.AccountHolderIdentifier,
        request.AccountHolderType,
        Holder(request.UpdatedAccountHolder),
        request.AccountIdentifier,
        request.AccountCurrency,
        Account(request.UpdatedAccount),
        Identifiers(request.ProxyIdentifiers),
        Persons(request.AuthorizedPersons),
        Persons(request.BeneficialOwners));

    public static RemoveProxyRequest Map(ProxyRemoveRequestDto request) => new(
        request.AccountHolderIdentifier,
        request.AccountHolderType,
        request.AccountIdentifier,
        request.AccountCurrency,
        request.KeepAccountActive,
        Identifiers(request.ProxyIdentifiersToRemove),
        request.AuthorizedPersonIdsToRemove,
        request.BeneficialOwnerIdsToRemove);

    private static ProxyAccountHolder? Holder(ProxyAccountHolderDto? holder) => holder is null
        ? null
        : new(holder.Identifier, holder.Type, holder.GivenNameKa, holder.SurnameKa, holder.MiddleNameKa, holder.Woman,
            holder.Citizenship, holder.CountryOfResidence, holder.LegalFormKa, holder.LegalNameKa, holder.WomenLedBusiness,
            holder.CountryOfRegistration);

    private static ProxyAccount? Account(ProxyAccountDto? account) => account is null
        ? null
        : new(account.Identifier, account.IsIban, account.Currency, account.Type, account.OpeningDate, account.ClosingDate);

    private static IReadOnlyList<ProxyIdentifier?>? Identifiers(IReadOnlyList<ProxyIdentifierDto>? identifiers) => identifiers?
        .Select(identifier => identifier is null ? null : new ProxyIdentifier(identifier.Type, identifier.Alias))
        .ToArray();

    private static IReadOnlyList<ProxyPerson?>? Persons<T>(IReadOnlyList<T>? persons) where T : IProxyPersonDto => persons?
        .Select(person => person is null
            ? null
            : new ProxyPerson(person.Id, person.GivenNameKa, person.SurnameKa, person.MiddleNameKa, person.Woman,
                person.Citizenship, person.CountryOfResidence, person.FromDate, person.ToDate))
        .ToArray();
}
