namespace IPS.Middleware.Infrastructure.Transport;

// HTTP headers of the Montran IPS interface (Annex D).
internal static class IpsHeaders
{
    internal const string Channel = "X-MONTRAN-IPS-Channel";
    internal const string Version = "X-MONTRAN-IPS-Version";
    internal const string RequestStatus = "X-MONTRAN-IPS-ReqSts";
    internal const string MessageType = "X-MONTRAN-IPS-MessageType";
    internal const string MessageSequence = "X-MONTRAN-IPS-MessageSeq";
    internal const string PossibleDuplicate = "X-MONTRAN-IPS-PossibleDuplicate";
    // Marks a message the participant repeats because the outcome of the previous attempt is unknown (Annex D p. 94).
    internal const string ResendPossibleDuplicate = "X-MONTRAN-RTP-PossibleDuplicate";
    internal const string IdempotencyKey = "Idempotency-Key";
}
