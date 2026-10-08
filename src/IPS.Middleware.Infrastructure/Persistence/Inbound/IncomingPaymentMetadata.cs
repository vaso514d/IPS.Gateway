using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Infrastructure.Persistence.Inbound;

internal sealed class IncomingPaymentMetadata
{
    public Guid Id { get; set; }
    public IncomingPayment Payment { get; set; } = null!;
    public string RequestJson { get; set; } = null!;
    public string ContextJson { get; set; } = null!;
    public long CheckpointVersion { get; set; }
    public DateTimeOffset? FollowUpAtUtc { get; set; }
    public DateTimeOffset? ReconciliationDeadlineUtc { get; set; }
    public Guid? ClaimToken { get; set; }
    public DateTimeOffset? ClaimExpiresAtUtc { get; set; }
    public DateTimeOffset? NextActionAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public bool HasLiveClaim(IncomingPaymentClaim claim, DateTimeOffset now) =>
        ClaimToken == claim.Token && ClaimExpiresAtUtc > now;

    public void SetClaim(Guid? token, DateTimeOffset? expiresAtUtc)
    {
        ClaimToken = token;
        ClaimExpiresAtUtc = expiresAtUtc;
    }
}
