namespace IPS.Middleware.Domain.Transactions;

// What the gateway does when IPS answers with a given internal error code (Annex D, Annex 2).
public enum IpsStatusCodeAction
{
    Accept = 0,
    FinalReject = 1,
    // IPS never processed the transaction: send it again (same MsgId/TxId).
    Resend = 2,
    // IPS is still processing it: investigate again later.
    InvestigateLater = 3,
    Investigate = 4
}

// An IPS internal error code, the pacs.002 reason code it maps to (null where Annex 2 gives none) and the gateway's action.
public sealed record IpsStatusCode(int InternalCode, string? IsoCode, string Description, IpsStatusCodeAction Action);

// Annex D v1.02, Annex 2 "Error Codes", pp. 204-206, as catalogued by the original gateway. Codes and descriptions are
// paired in table order, which matches every ISO mapping (e.g. 1015 TM01 = invalid time, 1016/1017 AG09 = original not
// found / not in final status, as 3.2.9.5 on p. 60 also states).
public static class IpsStatusCodes
{
    public static IReadOnlyList<IpsStatusCode> All { get; } =
    [
        new(0, null, "No error.", IpsStatusCodeAction.Accept),
        new(100, "MS03", "Internal processing error.", IpsStatusCodeAction.Investigate),
        new(101, null, "Message Identification.", IpsStatusCodeAction.FinalReject),
        new(500, "AB05", "Timeout error.", IpsStatusCodeAction.FinalReject),
        new(501, "MS02", "Payment declined by the beneficiary Participant.", IpsStatusCodeAction.FinalReject),
        new(502, "AB08", "The recipient Participant is disconnected.", IpsStatusCodeAction.FinalReject),
        new(1000, "MS03", "Generic error.", IpsStatusCodeAction.FinalReject),
        new(1001, "MS03", "Generic validation error.", IpsStatusCodeAction.FinalReject),
        new(1002, "FF01", "The currency is invalid.", IpsStatusCodeAction.FinalReject),
        new(1003, "FF01", "Amount is invalid (less than or equal to zero).", IpsStatusCodeAction.FinalReject),
        new(1004, "DNOR", "The value in the DbtrAgt field is invalid: no ACTIVE Participant with this BIC found, or the value is different from InstgAgt.", IpsStatusCodeAction.FinalReject),
        new(1005, "CNOR", "The value in the CdtrAgt field is invalid: no ACTIVE Participant with this BIC found.", IpsStatusCodeAction.FinalReject),
        new(1006, "RC01", "The value in the InstgAgt field is invalid: no ACTIVE Participant with this BIC found, or the value is different from the ordering Participant detected at channel transmission level.", IpsStatusCodeAction.FinalReject),
        new(1007, "RC01", "Unused.", IpsStatusCodeAction.FinalReject),
        new(1008, "AC01", "The IBAN code of the debtor is invalid.", IpsStatusCodeAction.FinalReject),
        new(1009, "AC01", "The creditor IBAN code is invalid.", IpsStatusCodeAction.FinalReject),
        new(1010, "AM05", "A payment with the same reference is currently processed.", IpsStatusCodeAction.InvestigateLater),
        new(1011, "AM05", "A payment with the same reference was already processed.", IpsStatusCodeAction.Investigate),
        new(1012, "FF01", "The settlement date is invalid (IntrBkSttlmDt).", IpsStatusCodeAction.FinalReject),
        new(1013, "FF01", "Invalid XML format.", IpsStatusCodeAction.FinalReject),
        new(1014, null, "Unused.", IpsStatusCodeAction.FinalReject),
        new(1015, "TM01", "Invalid Time: the Acceptance Date Time is older than the current time by more than the timeout configured in the payment schema.", IpsStatusCodeAction.FinalReject),
        new(1016, "AG09", "Invalid original reference: no transaction with the original reference was found.", IpsStatusCodeAction.Resend),
        new(1017, "AG09", "Invalid status of the original transaction: the referenced transaction is not in the proper status (still being processed).", IpsStatusCodeAction.InvestigateLater),
        new(1018, "RC01", "Invalid sender: the sender Participant is not ACTIVE.", IpsStatusCodeAction.FinalReject),
        new(1019, "RR04", "The payment schema is not defined for the currency specified by the message.", IpsStatusCodeAction.FinalReject),
        new(1020, "AM02", "The amount is too high compared to the parameter defined in the payment schema.", IpsStatusCodeAction.FinalReject),
        new(1021, "AG10", "Sender is blocked (temporarily disabled).", IpsStatusCodeAction.FinalReject),
        new(1022, "RR04", "Payment schema not mapped (either for debtor or creditor).", IpsStatusCodeAction.FinalReject),
        new(1026, "FF01", "Invalid XML field (XML validations beyond the default XSD, e.g. SvcLvl, CtgyPurp codes).", IpsStatusCodeAction.FinalReject),
        new(1027, "RC01", "Invalid receiver (the receiver is not the system BIC for messages sent by participants).", IpsStatusCodeAction.FinalReject),
        new(1028, "FF01", "Invalid original settlement date (recalls that point to an original date in the future).", IpsStatusCodeAction.FinalReject),
        new(1029, "RC01", "Invalid on us (intra-bank payments sent to IPS).", IpsStatusCodeAction.FinalReject),
        new(1030, "RC01", "Invalid originator (originator BIC missing or invalid).", IpsStatusCodeAction.FinalReject),
        new(1033, "AB05", "Past requested execution day (RTP flow).", IpsStatusCodeAction.FinalReject),
        new(1040, "AB05", "Missing requested execution day (RTP message).", IpsStatusCodeAction.FinalReject),
        new(1041, "MS03", "RTP flow is not enabled in the payment schema.", IpsStatusCodeAction.FinalReject),
        new(1042, "AB05", "Requested execution date is after the maximum allowed in the payment schema.", IpsStatusCodeAction.FinalReject),
        new(1043, "MS03", "PmtInfId from RTP is invalid.", IpsStatusCodeAction.FinalReject),
        new(1044, "MS03", "Invalid date in PmtInfId from RTP (the id must start with the current date).", IpsStatusCodeAction.FinalReject),
        new(1045, "MS03", "PmtTpInf field present in both group and item description.", IpsStatusCodeAction.FinalReject),
        new(2000, "AM23", "Insufficient funds to clear the transaction.", IpsStatusCodeAction.FinalReject),
        new(3000, "FF01", "The Participant does not have any active certificate of DS type for validating messages.", IpsStatusCodeAction.FinalReject),
        new(3001, "FF01", "The XML message does not contain the digital signature in the specified format.", IpsStatusCodeAction.FinalReject),
        new(3002, "FF01", "Digital signature does not protect the entire XML message.", IpsStatusCodeAction.FinalReject),
        new(3003, "FF01", "Digital signature is invalid.", IpsStatusCodeAction.FinalReject),
        new(3004, "FF01", "The certificate used for signing has expired or has been revoked.", IpsStatusCodeAction.FinalReject)
    ];

    private static readonly IReadOnlyDictionary<int, IpsStatusCode> ByInternalCode = All.ToDictionary(code => code.InternalCode);

    // Null for a code Annex 2 does not list.
    public static IpsStatusCode? Find(int internalCode) => ByInternalCode.GetValueOrDefault(internalCode);
}
