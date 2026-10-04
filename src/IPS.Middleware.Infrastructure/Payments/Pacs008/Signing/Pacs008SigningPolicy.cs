namespace IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

public sealed class Pacs008SigningPolicy
{
    public const string AllowUnsignedConfigurationKey = "Payments:Signing:AllowUnsignedInDevelopment";

    public Pacs008SigningPolicy(bool allowUnsignedInDevelopment, bool isDevelopment)
    {
        if (allowUnsignedInDevelopment && !isDevelopment)
            throw new InvalidOperationException("Unsigned payment messages can only be enabled in Development.");
        AllowUnsignedWithoutCertificate = allowUnsignedInDevelopment && isDevelopment;
    }

    public bool AllowUnsignedWithoutCertificate { get; }
}
