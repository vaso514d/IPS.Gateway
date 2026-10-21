using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Api.Configuration;

// The IPS counterparty in every message header is the NBG code of the bank identity (decision 016).
internal sealed class ProtocolSettings
{
    public string ServiceLevelCode { get; set; } = Pacs008ProtocolProfile.DefaultServiceLevel;
    public RemittanceDeliveryMethod RemittanceMethod { get; set; } = RemittanceDeliveryMethod.Uri;

    internal static Pacs008ProtocolProfile ReadProfile(IServiceProvider services, string section)
    {
        var settings = services.ReadSection<ProtocolSettings>(section) ?? new ProtocolSettings();
        var bank = services.GetRequiredService<BankIdentity>();
        return new Pacs008ProtocolProfile(bank.NbgBic, settings.ServiceLevelCode, settings.RemittanceMethod);
    }
}
