using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

// The moments at which an IPS signature is verified against its certificate (012b): both bounds of the validity
// period are inclusive, one tick outside either bound is refused.
internal static class SignatureValidity
{
    internal const string IncomingReason = "IPS certificate outside its validity period: ";
    internal const string ReplyReason = "The IPS reply was signed by a trusted IPS certificate outside its validity period: ";

    public static TheoryData<string, bool> Moments() => new()
    {
        { "not-before", true },
        { "not-after", true },
        { "tick-before-start", false },
        { "tick-after-end", false }
    };

    internal static DateTimeOffset At(X509Certificate2 certificate, string moment)
    {
        var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime());
        var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
        return moment switch
        {
            "not-before" => notBefore,
            "not-after" => notAfter,
            "tick-before-start" => notBefore.AddTicks(-1),
            "tick-after-end" => notAfter.AddTicks(1),
            _ => throw new ArgumentOutOfRangeException(nameof(moment), moment, "Unknown moment.")
        };
    }

    // A processing time at which the certificate has long expired, to show that an incoming message is judged at receipt.
    internal static DateTimeOffset LongAfterExpiry(X509Certificate2 certificate) => At(certificate, "tick-after-end").AddDays(1);

    // Written independently of the verifier: the period in whole UTC seconds first, then the subject.
    internal static string Detail(X509Certificate2 certificate) => string.Create(CultureInfo.InvariantCulture,
        $"valid {Utc(certificate.NotBefore)} to {Utc(certificate.NotAfter)}, {certificate.Subject}");

    internal static string IncomingHold(X509Certificate2 certificate) => IncomingReason + Detail(certificate);

    internal static string ReplyUnresolved(X509Certificate2 certificate) => ReplyReason + Detail(certificate);

    // The receipt journal keeps the start of a hold reason.
    internal static string Stored(string reason) =>
        reason.Length > InboundJournalEntry.HoldReasonLimit ? reason[..InboundJournalEntry.HoldReasonLimit] : reason;

    private static string Utc(DateTime value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
