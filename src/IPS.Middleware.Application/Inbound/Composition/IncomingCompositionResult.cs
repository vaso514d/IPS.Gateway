namespace IPS.Middleware.Application.Inbound.Composition;

public enum IncomingCompositionStatus { Terminal, Held, Deferred, OwnershipLost, ReplyReady }
public sealed record IncomingCompositionResult(IncomingCompositionStatus Status, Guid? PaymentId = null);
