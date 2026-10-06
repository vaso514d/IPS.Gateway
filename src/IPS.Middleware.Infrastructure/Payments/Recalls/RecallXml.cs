using System.Globalization;
using System.Xml.Linq;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Recalls;

namespace IPS.Middleware.Infrastructure.Payments.Recalls;

// The parts a camt.056 and a camt.029 write alike (IPS v1, Annex D 3.2.4 and 3.2.5). The caller passes the document
// namespace; child order follows the XSD sequence and null children are omitted.
internal static class RecallXml
{
    private const string ClearingSystem = "IPS";
    private const string LocalInstrument = "INST";

    internal static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string Timestamp(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    internal static XElement Amount(XName name, decimal amount, string currency) =>
        new(name, new XAttribute("Ccy", currency), amount.ToString("0.#####", CultureInfo.InvariantCulture));

    // The assigner is our participant and the assignee is IPS.
    internal static XElement Assignment(XNamespace ns, string messageId, string participantBic, string ipsBic, DateTimeOffset createdAt) =>
        new(ns + "Assgnmt",
            new XElement(ns + "Id", messageId),
            new XElement(ns + "Assgnr", AssignmentAgent(ns, participantBic)),
            new XElement(ns + "Assgne", AssignmentAgent(ns, ipsBic)),
            new XElement(ns + "CreDtTm", Timestamp(createdAt)));

    // The originator is our participant: the debtor's for a recall (Annex D 3.2.4.i), the creditor's for the answer (3.2.5.j).
    internal static XElement Reason(XNamespace ns, string elementName, string participantBic, string reasonCode, string? additionalInformation) =>
        new(ns + elementName,
            new XElement(ns + "Orgtr", new XElement(ns + "Id", new XElement(ns + "OrgId", new XElement(ns + "AnyBIC", participantBic)))),
            new XElement(ns + "Rsn", new XElement(ns + "Cd", reasonCode)),
            Optional(ns, "AddtlInf", additionalInformation));

    // A camt.029 also states the recalled amount first (Annex D 3.2.5); a camt.056 does not.
    internal static XElement OriginalReference(XNamespace ns, RecalledTransaction original, IpsMessageProfile profile, XElement? amount) =>
        new(ns + "OrgnlTxRef",
            amount,
            new XElement(ns + "IntrBkSttlmDt", Date(original.SettlementDate)),
            // SettlementInstruction15 requires the settlement method, so it is sent with the clearing system.
            new XElement(ns + "SttlmInf",
                new XElement(ns + "SttlmMtd", "CLRG"),
                new XElement(ns + "ClrSys", new XElement(ns + "Cd", ClearingSystem))),
            new XElement(ns + "PmtTpInf",
                Code(ns, "SvcLvl", profile.ServiceLevelCode),
                Code(ns, "LclInstrm", LocalInstrument)),
            Remittance(ns, original.Remittance),
            Ultimate(ns, "UltmtDbtr", original.UltimateDebtor),
            Party(ns, "Dbtr", original.Debtor),
            Account(ns, "DbtrAcct", original.Debtor.Account),
            Agent(ns, "DbtrAgt", original.DebtorAgent),
            Agent(ns, "CdtrAgt", original.CreditorAgent),
            Party(ns, "Cdtr", original.Creditor),
            Account(ns, "CdtrAcct", original.Creditor.Account),
            Ultimate(ns, "UltmtCdtr", original.UltimateCreditor));

    private static XElement AssignmentAgent(XNamespace ns, string bic) =>
        new(ns + "Agt", new XElement(ns + "FinInstnId", new XElement(ns + "BICFI", bic)));

    // Ustrd, then a single structured creditor reference with the SCOR code (Annex D 8.1.5).
    private static XElement? Remittance(XNamespace ns, RecallRemittance? remittance) =>
        remittance is null
            ? null
            : new(ns + "RmtInf",
                Optional(ns, "Ustrd", remittance.Unstructured),
                remittance.CreditorReference is not { } reference
                    ? null
                    : new XElement(ns + "Strd",
                        new XElement(ns + "CdtrRefInf",
                            new XElement(ns + "Tp",
                                new XElement(ns + "CdOrPrtry", new XElement(ns + "Cd", "SCOR")),
                                Optional(ns, "Issr", reference.Issuer)),
                            new XElement(ns + "Ref", reference.Reference))));

    private static XElement Party(XNamespace ns, string name, RecallParty party) =>
        new(ns + name,
            new XElement(ns + "Pty",
                new XElement(ns + "Nm", party.Name),
                Address(ns, party.Address),
                PartyIdentification(ns, party.Kind, party.Identifier)));

    private static XElement? Ultimate(XNamespace ns, string name, RecallUltimateParty? party) =>
        party is null
            ? null
            : new(ns + name,
                new XElement(ns + "Pty",
                    new XElement(ns + "Nm", party.Name),
                    PartyIdentification(ns, party.Kind, party.Identifier)));

    private static XElement? PartyIdentification(XNamespace ns, PaymentPartyKind? kind, string? identifier) =>
        identifier is null
            ? null
            : new(ns + "Id",
                new XElement(ns + (kind == PaymentPartyKind.Individual ? "PrvtId" : "OrgId"),
                    new XElement(ns + "Othr", new XElement(ns + "Id", identifier))));

    private static XElement? Address(XNamespace ns, RecallAddress? address) =>
        address is null
            ? null
            : new(ns + "PstlAdr",
                Optional(ns, "StrtNm", address.StreetName),
                Optional(ns, "BldgNb", address.BuildingNumber),
                Optional(ns, "PstCd", address.PostCode),
                Optional(ns, "TwnNm", address.TownName),
                Optional(ns, "CtrySubDvsn", address.CountrySubdivision),
                Optional(ns, "Ctry", address.Country),
                address.AddressLines.Select(line => new XElement(ns + "AdrLine", line)));

    private static XElement Agent(XNamespace ns, string name, RecallAgent agent) =>
        new(ns + name, new XElement(ns + "FinInstnId", new XElement(ns + "BICFI", agent.Bic), Optional(ns, "Nm", agent.Name)));

    private static XElement Account(XNamespace ns, string name, string iban) =>
        new(ns + name, new XElement(ns + "Id", new XElement(ns + "IBAN", iban)));

    private static XElement Code(XNamespace ns, string name, string code) => new(ns + name, new XElement(ns + "Cd", code));

    // new XElement(name, null) would emit an empty element; absent values must be omitted.
    private static XElement? Optional(XNamespace ns, string name, string? value) => value is null ? null : new(ns + name, value);
}
