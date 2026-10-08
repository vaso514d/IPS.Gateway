using System.Xml;
using FluentValidation;

namespace IPS.Middleware.Application.Proxy.Validation;

// These rules guarantee that the request produces acmt.022.001.04-valid XML, so a request the Proxy Solution would reject at
// schema validation (pacs.002 FF01) never leaves this service. Every limit is the XSD simple type of the element the value
// lands in. Business rules (allowed alias types and formats, mandatory holder details, person counts) belong to the Proxy
// Solution and come back as a reject. SplmtryData/Envlp is xs:any, so its content only has to be XML-safe.
internal static class ProxyRules
{
    // IBAN2007Identifier.
    internal const string IbanPattern = @"\A[A-Z]{2}[0-9]{2}[a-zA-Z0-9]{1,30}\z";
    // ActiveOrHistoricCurrencyCode.
    internal const string CurrencyPattern = @"\A[A-Z]{3}\z";
    // Party identification (PrvtId/OrgId Othr/Id).
    internal const int PartyIdentifierMaxLength = 256;
    // GenericAccountIdentification1/Id.
    internal const int OtherAccountIdentifierMaxLength = 34;
    // CashAccountType2Choice/Prtry.
    internal const int AccountTypeMaxLength = 35;
    // OtherContact1/ChanlTp.
    internal const int ChannelTypeMaxLength = 4;
    // OtherContact1/Id.
    internal const int AliasMaxLength = 128;

    internal static bool IsXmlText(string? value)
    {
        if (value is null)
        {
            return true;
        }

        try
        {
            XmlConvert.VerifyXmlChars(value);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    internal static IRuleBuilderOptions<T, string?> XmlText<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(IsXmlText).WithMessage("'{PropertyName}' contains characters that cannot be represented in XML.");

    internal static IRuleBuilderOptions<T, string?> HolderType<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(ProxyHolderTypes.IsHolderType).WithMessage("'{PropertyName}' must be Individual or LegalEntity.");

    internal static IRuleBuilderOptions<T, string?> PartyIdentifier<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty().MaximumLength(PartyIdentifierMaxLength).XmlText();
}

internal sealed class RegisterProxyValidator : AbstractValidator<RegisterProxyRequest>
{
    public RegisterProxyValidator()
    {
        RuleFor(request => request.AccountHolder).NotNull().SetValidator(new ProxyAccountHolderValidator(validateIdentity: true)!);
        RuleFor(request => request.Account).SetValidator(new ProxyAccountValidator()!);
        RuleForEach(request => request.ProxyIdentifiers).NotNull().SetValidator(new ProxyIdentifierValidator()!);
        RuleForEach(request => request.AuthorizedPersons).NotNull().SetValidator(new ProxyPersonValidator()!);
        RuleForEach(request => request.BeneficialOwners).NotNull().SetValidator(new ProxyPersonValidator()!);
    }
}

internal sealed class UpdateProxyValidator : AbstractValidator<UpdateProxyRequest>
{
    public UpdateProxyValidator()
    {
        RuleFor(request => request.AccountHolderIdentifier).PartyIdentifier();
        RuleFor(request => request.AccountHolderType).HolderType();
        // The holder's identity comes from AccountHolderIdentifier and Type; UpdatedAccountHolder only carries changed details.
        RuleFor(request => request.UpdatedAccountHolder).SetValidator(new ProxyAccountHolderValidator(validateIdentity: false)!);
        RuleFor(request => request.UpdatedAccount).SetValidator(new ProxyAccountValidator()!);
        // Without UpdatedAccount the identifier override is sent as an IBAN.
        RuleFor(request => request.AccountIdentifier)
            .Matches(ProxyRules.IbanPattern)
            .When(request => !string.IsNullOrEmpty(request.AccountIdentifier) && request.UpdatedAccount is null);
        RuleFor(request => request.AccountCurrency)
            .Matches(ProxyRules.CurrencyPattern)
            .When(request => !string.IsNullOrEmpty(request.AccountCurrency));
        RuleForEach(request => request.ProxyIdentifiers).NotNull().SetValidator(new ProxyIdentifierValidator()!);
        RuleForEach(request => request.AuthorizedPersons).NotNull().SetValidator(new ProxyPersonValidator()!);
        RuleForEach(request => request.BeneficialOwners).NotNull().SetValidator(new ProxyPersonValidator()!);
    }
}

internal sealed class RemoveProxyValidator : AbstractValidator<RemoveProxyRequest>
{
    public RemoveProxyValidator()
    {
        RuleFor(request => request.AccountHolderIdentifier).PartyIdentifier();
        RuleFor(request => request.AccountHolderType).HolderType();
        RuleFor(request => request.AccountIdentifier).NotEmpty().Matches(ProxyRules.IbanPattern);
        RuleFor(request => request.AccountCurrency)
            .Matches(ProxyRules.CurrencyPattern)
            .When(request => !string.IsNullOrEmpty(request.AccountCurrency));
        RuleForEach(request => request.ProxyIdentifiersToRemove).NotNull().SetValidator(new ProxyIdentifierValidator()!);
        RuleForEach(request => request.AuthorizedPersonIdsToRemove).NotNull().XmlText();
        RuleForEach(request => request.BeneficialOwnerIdsToRemove).NotNull().XmlText();
    }
}

// The identifier and type are the party identity (PrvtId or OrgId), checked only when the request addresses the holder
// (registration). Every other field is supplementary data.
internal sealed class ProxyAccountHolderValidator : AbstractValidator<ProxyAccountHolder>
{
    public ProxyAccountHolderValidator(bool validateIdentity)
    {
        if (validateIdentity)
        {
            RuleFor(holder => holder.Identifier).PartyIdentifier();
            RuleFor(holder => holder.Type).HolderType();
        }

        RuleFor(holder => holder.GivenName).XmlText();
        RuleFor(holder => holder.Surname).XmlText();
        RuleFor(holder => holder.MiddleName).XmlText();
        RuleFor(holder => holder.Citizenship).XmlText();
        RuleFor(holder => holder.CountryOfResidence).XmlText();
        RuleFor(holder => holder.LegalForm).XmlText();
        RuleFor(holder => holder.LegalName).XmlText();
        RuleFor(holder => holder.CountryOfRegistration).XmlText();
    }
}

// CashAccount40: Id is an IBAN or other identifier, the type is optional text and the currency an ISO code. Dates are
// supplementary data.
internal sealed class ProxyAccountValidator : AbstractValidator<ProxyAccount>
{
    public ProxyAccountValidator()
    {
        RuleFor(account => account.Identifier).NotEmpty().XmlText();
        RuleFor(account => account.Identifier).Matches(ProxyRules.IbanPattern).When(account => account.IsIban);
        RuleFor(account => account.Identifier).MaximumLength(ProxyRules.OtherAccountIdentifierMaxLength).When(account => !account.IsIban);
        RuleFor(account => account.Currency).Matches(ProxyRules.CurrencyPattern).When(account => !string.IsNullOrEmpty(account.Currency));
        RuleFor(account => account.Type).MaximumLength(ProxyRules.AccountTypeMaxLength).XmlText();
    }
}

// OtherContact1: the channel type is mandatory (4 characters), the alias optional (128).
internal sealed class ProxyIdentifierValidator : AbstractValidator<ProxyIdentifier>
{
    public ProxyIdentifierValidator()
    {
        RuleFor(identifier => identifier.Type).NotEmpty().MaximumLength(ProxyRules.ChannelTypeMaxLength).XmlText();
        RuleFor(identifier => identifier.Alias).MaximumLength(ProxyRules.AliasMaxLength).XmlText();
    }
}

// Authorized persons and beneficial owners live entirely in supplementary data, so they only have to be XML-safe.
internal sealed class ProxyPersonValidator : AbstractValidator<ProxyPerson>
{
    public ProxyPersonValidator()
    {
        RuleFor(person => person.Id).NotNull().XmlText();
        RuleFor(person => person.GivenName).XmlText();
        RuleFor(person => person.Surname).XmlText();
        RuleFor(person => person.MiddleName).XmlText();
        RuleFor(person => person.Citizenship).XmlText();
        RuleFor(person => person.CountryOfResidence).XmlText();
    }
}
