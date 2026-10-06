using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Infrastructure.Persistence.Inbound;

internal sealed class IncomingTransferMetadata
{
    public Guid Id { get; set; }
    public IncomingFiTransfer Transfer { get; set; } = null!;
    public string RequestJson { get; set; } = null!;
    public DateTimeOffset DeadlineUtc { get; set; }
    public Guid? ClaimToken { get; set; }
    public DateTimeOffset? ClaimExpiresAtUtc { get; set; }
    public DateTimeOffset? NextActionAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public bool HasLiveClaim(IncomingTransferClaim claim, DateTimeOffset now) =>
        ClaimToken == claim.Token && ClaimExpiresAtUtc > now;

    public void SetClaim(Guid? token, DateTimeOffset? expiresAtUtc)
    {
        ClaimToken = token;
        ClaimExpiresAtUtc = expiresAtUtc;
    }
}
