using System.Security.Cryptography.X509Certificates;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

// The configured IPS signature certificates and the clock they are judged by. Annex C 2.2: the certificate that signed
// a message must be within its validity period when the signature is verified, not only when it was loaded.
public sealed class IpsSignatureTrust(IReadOnlyCollection<X509Certificate2> certificates, TimeProvider time)
{
    // A reply to our own send is verified when it is interpreted.
    internal IpsSignatureCheck Check(string xml) => Check(xml, time.GetUtcNow());

    // An incoming message is verified as of its receipt, so a later processing or reply step reaches the same verdict.
    internal IpsSignatureCheck Check(string xml, DateTimeOffset at) => IpsSignatureVerifier.Check(xml, certificates, at);
}

internal abstract record IpsSignatureCheck
{
    public sealed record Trusted : IpsSignatureCheck;

    // Missing, invalid, or not from a trusted certificate.
    public sealed record Untrusted : IpsSignatureCheck;

    // Signed by a trusted certificate that is not valid at the moment of verification.
    public sealed record OutsideValidity(string Detail) : IpsSignatureCheck;
}
