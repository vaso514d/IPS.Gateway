using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

/// <summary>Independent pacs.002 reply fixtures. Text templates follow Annex D 8.1.8; Java JSR105 signs them.</summary>
internal static class IpsReplies
{
    internal sealed record Reply
    {
        public required string MessageId { get; init; }
        public required string TransactionId { get; init; }
        public required string EndToEndId { get; init; }
        public string? GroupStatus { get; init; } = "ACCP";
        public string? TransactionStatus { get; init; } = "ACCP";
        public string OriginalMessageName { get; init; } = "pacs.008.001.12";
        public string? ReasonCode { get; init; }
        public string? AdditionalInformation { get; init; }
        public bool IncludeTransaction { get; init; } = true;
    }

    internal static string Unsigned(Reply reply)
    {
        static string Status(string element, string? value) => value is null ? "" : $"<pacs:{element}>{value}</pacs:{element}>";
        var reason = reply.ReasonCode is null && reply.AdditionalInformation is null ? "" :
            "<pacs:StsRsnInf>" + (reply.ReasonCode is null ? "" : $"<pacs:Rsn><pacs:Cd>{reply.ReasonCode}</pacs:Cd></pacs:Rsn>") +
            (reply.AdditionalInformation is null ? "" : $"<pacs:AddtlInf>{SecurityElement.Escape(reply.AdditionalInformation)}</pacs:AddtlInf>") +
            "</pacs:StsRsnInf>";
        var transaction = !reply.IncludeTransaction ? "" :
            "<pacs:TxInfAndSts><pacs:StsId>IPS-STS-1</pacs:StsId>" +
            $"<pacs:OrgnlEndToEndId>{reply.EndToEndId}</pacs:OrgnlEndToEndId><pacs:OrgnlTxId>{reply.TransactionId}</pacs:OrgnlTxId>" +
            Status("TxSts", reply.TransactionStatus) + reason + "<pacs:AccptncDtTm>2026-10-04T14:00:01.000Z</pacs:AccptncDtTm></pacs:TxInfAndSts>";
        return "<Message xmlns:head=\"urn:iso:std:iso:20022:tech:xsd:head.001.001.03\" xmlns:pacs=\"urn:iso:std:iso:20022:tech:xsd:pacs.002.001.14\">" +
            "<head:AppHdr><head:Fr><head:FIId><head:FinInstnId><head:BICFI>NBGEGE22</head:BICFI></head:FinInstnId></head:FIId></head:Fr>" +
            "<head:To><head:FIId><head:FinInstnId><head:BICFI>BAGAGE22</head:BICFI></head:FinInstnId></head:FIId></head:To>" +
            "<head:BizMsgIdr>IPS-REPLY-1</head:BizMsgIdr><head:MsgDefIdr>pacs.002.001.14</head:MsgDefIdr>" +
            "<head:CreDt>2026-10-04T14:00:01.000Z</head:CreDt><head:Sgntr/></head:AppHdr>" +
            "<pacs:Document><pacs:FIToFIPmtStsRpt><pacs:GrpHdr><pacs:MsgId>IPS-REPLY-1</pacs:MsgId><pacs:CreDtTm>2026-10-04T14:00:01.000Z</pacs:CreDtTm></pacs:GrpHdr>" +
            $"<pacs:OrgnlGrpInfAndSts><pacs:OrgnlMsgId>{reply.MessageId}</pacs:OrgnlMsgId><pacs:OrgnlMsgNmId>{reply.OriginalMessageName}</pacs:OrgnlMsgNmId>" +
            Status("GrpSts", reply.GroupStatus) + (reply.IncludeTransaction ? "" : reason) + "</pacs:OrgnlGrpInfAndSts>" +
            transaction + "</pacs:FIToFIPmtStsRpt></pacs:Document></Message>";
    }

    internal static X509Certificate2 Certificate(string subject = "CN=Simulated IPS")
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    /// <summary>Signs every message in one JVM run; certificates must carry an ECDSA private key.</summary>
    internal static async Task<string[]> SignAsync(X509Certificate2 certificate, params string[] messages)
    {
        var directory = Path.Combine(Path.GetTempPath(), "IPS-ReplySigning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var keyPath = Path.Combine(directory, "key.p8");
            var certificatePath = Path.Combine(directory, "certificate.cer");
            using (var key = certificate.GetECDsaPrivateKey()!) await File.WriteAllBytesAsync(keyPath, key.ExportPkcs8PrivateKey());
            await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Cert));
            var arguments = new List<string> { Path.Combine(AppContext.BaseDirectory, "Payments", "Fixtures", "SignXmlReply.java"), keyPath, certificatePath };
            for (var index = 0; index < messages.Length; index++)
            {
                var input = Path.Combine(directory, $"{index}.xml");
                await File.WriteAllTextAsync(input, messages[index]);
                arguments.AddRange([input, input + ".signed"]);
            }
            await RunJavaAsync(arguments);
            return await Task.WhenAll(messages.Select((_, index) => File.ReadAllTextAsync(Path.Combine(directory, $"{index}.xml.signed"))));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task RunJavaAsync(IEnumerable<string> arguments)
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        var start = new ProcessStartInfo(string.IsNullOrWhiteSpace(javaHome) ? "java" :
            Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Java. Reply tests require JDK17+.");
        var error = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await error);
    }
}
