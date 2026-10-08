using System.Globalization;
using System.Xml.Linq;
using IPS.Middleware.Application.Proxy;

namespace IPS.Middleware.Infrastructure.Proxy;

// Builds the acmt.022.001.04 IdentificationModificationAdvice of Annex E 2.3.2 for register, update and remove. Child order
// follows the XSD, which is authoritative where it and the PDF disagree (the ISO Document root, Agt beside Acct, a mandatory
// UpdtdPtyAndAcctId, optional CtctDtls). SplmtryData/Envlp is xs:any, so its content follows the PDF field tables; nothing
// the caller did not supply is defaulted. Null children are omitted.
internal static class Acmt022Message
{
    private static readonly XNamespace H = ProxyXml.HeaderNamespace;
    private static readonly XNamespace A = ProxyXml.DocumentNamespace;
    private static readonly XNamespace Wrapper = ProxyXml.WrapperNamespace;

    internal static XElement BuildRegister(RegisterProxyRequest request, ProxyIds ids, ProxySettings settings, DateTimeOffset now)
    {
        var modification = new XElement(A + "Mod",
            new XElement(A + "Id", ids.OperationId),
            UpdatedPartyAndAccount(request.AccountHolder!, request.Account, request.ProxyIdentifiers, settings));
        return Envelope(modification, RegistrationData(request), ids, settings, now);
    }

    internal static XElement BuildUpdate(UpdateProxyRequest request, ProxyIds ids, ProxySettings settings, DateTimeOffset now)
    {
        // The holder's identity must exactly match the registered one (Annex E p. 38), so it always comes from the
        // identifier and type; UpdatedAccountHolder only carries changed details.
        var identity = new ProxyAccountHolder(request.AccountHolderIdentifier, request.AccountHolderType, null, null, null, null, null, null, null, null, null, null);
        var modification = new XElement(A + "Mod",
            new XElement(A + "Id", ids.OperationId),
            new XElement(A + "OrgnlPtyAndAcctId", new XElement(A + "Pty", new XElement(A + "Id", PartyIdentity(identity)))),
            UpdatedPartyAndAccount(identity, request.UpdatedAccount, request.ProxyIdentifiers, settings, request.AccountIdentifier, request.AccountCurrency));
        return Envelope(modification, UpdateData(request), ids, settings, now);
    }

    internal static XElement BuildRemove(RemoveProxyRequest request, ProxyIds ids, ProxySettings settings, DateTimeOffset now)
    {
        var identity = new ProxyAccountHolder(request.AccountHolderIdentifier, request.AccountHolderType, null, null, null, null, null, null, null, null, null, null);
        var original = new XElement(A + "OrgnlPtyAndAcctId",
            new XElement(A + "Pty", new XElement(A + "Id", PartyIdentity(identity)), ContactDetails(request.ProxyIdentifiersToRemove)),
            Account(request.AccountIdentifier!, isIban: true, request.AccountCurrency, type: null),
            Agent(settings));
        // An account under UpdtdPtyAndAcctId means "keep the account active"; none means "remove the account". The element
        // itself is mandatory even for a full removal (Annex E p. 40-43).
        var updated = new XElement(A + "UpdtdPtyAndAcctId",
            request.KeepAccountActive ? Account(request.AccountIdentifier!, isIban: true, request.AccountCurrency, type: null) : null);
        var modification = new XElement(A + "Mod", new XElement(A + "Id", ids.OperationId), original, updated);
        return Envelope(modification, RemovalData(request), ids, settings, now);
    }

    private static XElement UpdatedPartyAndAccount(
        ProxyAccountHolder holder,
        ProxyAccount? account,
        IReadOnlyList<ProxyIdentifier?>? identifiers,
        ProxySettings settings,
        string? accountIdentifierOverride = null,
        string? accountCurrencyOverride = null)
    {
        var party = new XElement(A + "Pty", new XElement(A + "Id", PartyIdentity(holder)), ContactDetails(identifiers));
        // A blank override means no account, so an empty IBAN is never sent.
        var accountIdentifier = account?.Identifier ?? (string.IsNullOrWhiteSpace(accountIdentifierOverride) ? null : accountIdentifierOverride);
        var accountElement = accountIdentifier is null
            ? null
            : Account(accountIdentifier, account?.IsIban ?? true, account?.Currency ?? accountCurrencyOverride, account?.Type);
        return new XElement(A + "UpdtdPtyAndAcctId", party, accountElement, accountElement is null ? null : Agent(settings));
    }

    // CtctDtls and its Othr list are optional in the XSD, so contact details are sent only when there is a proxy identifier.
    private static XElement? ContactDetails(IReadOnlyList<ProxyIdentifier?>? identifiers) =>
        identifiers is not { Count: > 0 }
            ? null
            : new XElement(A + "CtctDtls",
                identifiers.Select(identifier => new XElement(A + "Othr", new XElement(A + "ChanlTp", identifier!.Type), Optional("Id", identifier.Alias))));

    private static XElement PartyIdentity(ProxyAccountHolder holder) =>
        new(A + (ProxyHolderTypes.IsIndividual(holder.Type) ? "PrvtId" : "OrgId"), new XElement(A + "Othr", new XElement(A + "Id", holder.Identifier)));

    private static XElement Account(string identifier, bool isIban, string? currency, string? type) =>
        new(A + "Acct",
            new XElement(A + "Id", isIban ? new XElement(A + "IBAN", identifier) : new XElement(A + "Othr", new XElement(A + "Id", identifier))),
            string.IsNullOrWhiteSpace(type) ? null : new XElement(A + "Tp", new XElement(A + "Prtry", type)),
            string.IsNullOrWhiteSpace(currency) ? null : new XElement(A + "Ccy", currency));

    private static XElement Agent(ProxySettings settings) => Agent(settings.ParticipantBic);

    // Registration supplementary data (Annex E p. 35-38): party, account dates, authorized persons and beneficial owners.
    private static XElement RegistrationData(RegisterProxyRequest request)
    {
        var holder = request.AccountHolder!;
        var party = ProxyHolderTypes.IsIndividual(holder.Type)
            ? new XElement(A + "IndvPrsn", Individual(holder))
            : new XElement(A + "Org", Organisation(holder));
        var account = request.Account is null
            ? null
            : new XElement(A + "Acct", Date("OpngDt", request.Account.OpeningDate), Date("ClsgDt", request.Account.ClosingDate));
        var details = new XElement(A + "ModAddtlInf",
            new XElement(A + "Id", 1),
            new XElement(A + "Pty", party),
            account,
            (request.AuthorizedPersons ?? []).Select(person => RegisteredPerson("AuthPer", person!)),
            (request.BeneficialOwners ?? []).Select(owner => RegisteredPerson("Bnfcry", owner!)));
        return SupplementaryData(details);
    }

    // Authorized persons and beneficial owners share one field order (Annex E 4.29-4.56).
    private static XElement RegisteredPerson(string name, ProxyPerson person) =>
        new(A + name,
            new XElement(A + "Id", person.Id),
            Optional("GvnNm", person.GivenName),
            Optional("MddlNm", person.MiddleName),
            Optional("Srnm", person.Surname),
            Optional("Gndr", Gender(person.Woman)),
            Optional("CtryOfRes", person.CountryOfResidence),
            Optional("Ctznsh", person.Citizenship),
            Date("FrDt", person.FromDate),
            Date("ToDt", person.ToDate));

    // Update supplementary data (Annex E p. 39-41): every field means "change this value", so only what was supplied is sent.
    // Authorized persons and beneficial owners use the update shape (Nm, Id, dates).
    private static XElement UpdateData(UpdateProxyRequest request)
    {
        var holder = request.UpdatedAccountHolder;
        var party = holder is null
            ? null
            : ProxyHolderTypes.IsIndividual(request.AccountHolderType)
                ? Container("IndvPrsn", Individual(holder))
                : Container("Org", Organisation(holder));
        var account = Container("Acct", Date("OpngDt", request.UpdatedAccount?.OpeningDate), Date("ClsgDt", request.UpdatedAccount?.ClosingDate));
        var details = new List<XElement>();
        if (party is not null)
        {
            details.Add(new XElement(A + "Pty", party));
        }

        if (account is not null)
        {
            details.Add(account);
        }

        details.AddRange((request.AuthorizedPersons ?? []).Select(person => UpdatedPerson("AuthPer", person!)));
        details.AddRange((request.BeneficialOwners ?? []).Select(owner => UpdatedPerson("Bnfcry", owner!)));
        // Dtls is mandatory even when nothing changes; ModAddtlInf is optional.
        return SupplementaryData(details.Count == 0 ? null : new XElement(A + "ModAddtlInf", new XElement(A + "Id", 1), details));
    }

    private static XElement UpdatedPerson(string name, ProxyPerson person) =>
        new(A + name,
            Optional("Nm", JoinName(person.GivenName, person.MiddleName, person.Surname)),
            new XElement(A + "Id", person.Id),
            Date("FrDt", person.FromDate),
            Date("ToDt", person.ToDate));

    // The source writes BfyOwnr here but Bnfcry in registration and update; kept as the source sends it.
    private static XElement? RemovalData(RemoveProxyRequest request)
    {
        var authorized = request.AuthorizedPersonIdsToRemove ?? [];
        var owners = request.BeneficialOwnerIdsToRemove ?? [];
        if (authorized.Count == 0 && owners.Count == 0)
        {
            return null;
        }

        return SupplementaryData(new XElement(A + "ModAddtlInf",
            new XElement(A + "Id", 1),
            authorized.Select(id => new XElement(A + "AuthPer", new XElement(A + "Id", id))),
            owners.Select(id => new XElement(A + "BfyOwnr", new XElement(A + "Id", id)))));
    }

    private static XElement SupplementaryData(XElement? details) =>
        new(A + "SplmtryData", new XElement(A + "Envlp", new XElement(A + "Dtls", details)));

    // Annex E 2.3.2 field tables: the order is given-, middle-, surname, gender, residence, citizenship.
    private static XElement?[] Individual(ProxyAccountHolder holder) =>
    [
        Optional("GvnNm", holder.GivenName),
        Optional("MddlNm", holder.MiddleName),
        Optional("Srnm", holder.Surname),
        Optional("Gndr", Gender(holder.Woman)),
        Optional("CtryOfRes", holder.CountryOfResidence),
        Optional("Ctznsh", holder.Citizenship)
    ];

    private static XElement?[] Organisation(ProxyAccountHolder holder) =>
    [
        Optional("LglForm", holder.LegalForm),
        Optional("Nm", holder.LegalName),
        Optional("WlB", holder.WomenLedBusiness is null ? null : holder.WomenLedBusiness.Value ? "true" : "false"),
        Optional("RegnCtry", holder.CountryOfRegistration)
    ];

    // Gender1Code: FEMA or MALE (Annex E p. 35).
    private static string? Gender(bool? woman) => woman is null ? null : woman.Value ? "FEMA" : "MALE";

    private static XElement? Container(string name, params XElement?[] children) =>
        children.Any(child => child is not null) ? new XElement(A + name, children) : null;

    private static XElement? Optional(string name, string? value) => string.IsNullOrWhiteSpace(value) ? null : new XElement(A + name, value);

    private static XElement? Date(string name, DateOnly? value) =>
        value is null ? null : new XElement(A + name, value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    private static string? JoinName(params string?[] parts)
    {
        var name = string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
        return name.Length == 0 ? null : name;
    }

    // The Annex E envelope: hdr:Message holding the application header and the document. The signer fills in Sgntr.
    private static XElement Envelope(XElement modification, XElement? supplementaryData, ProxyIds ids, ProxySettings settings, DateTimeOffset now)
    {
        var timestamp = now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var assignment = new XElement(A + "Assgnmt",
            new XElement(A + "MsgId", ids.BulkMessageId),
            new XElement(A + "CreDtTm", timestamp),
            new XElement(A + "Assgnr", Agent(settings.ParticipantBic)),
            new XElement(A + "Assgne", Agent(settings.ProxyBic)));
        return new XElement(Wrapper + "Message",
            new XAttribute(XNamespace.Xmlns + "hdr", Wrapper.NamespaceName),
            new XElement(H + "AppHdr",
                new XAttribute("xmlns", H.NamespaceName),
                new XElement(H + "Fr", new XElement(H + "FIId", new XElement(H + "FinInstnId", new XElement(H + "BICFI", settings.ParticipantBic)))),
                new XElement(H + "To", new XElement(H + "FIId", new XElement(H + "FinInstnId", new XElement(H + "BICFI", settings.ProxyBic)))),
                new XElement(H + "BizMsgIdr", ids.BulkMessageId),
                new XElement(H + "MsgDefIdr", ProxyXml.MessageDefinition),
                new XElement(H + "CreDt", timestamp)),
            new XElement(A + "Document",
                new XAttribute("xmlns", A.NamespaceName),
                new XElement(A + "IdModAdvc", assignment, modification, supplementaryData)));
    }

    private static XElement Agent(string bic) => new(A + "Agt", new XElement(A + "FinInstnId", new XElement(A + "BICFI", bic)));
}
