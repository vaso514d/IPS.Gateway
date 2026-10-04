using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.XmlModels;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

internal static class Pacs008RemittanceMapping
{
    internal static RegulatoryXml? Regulatory(PaymentInitiation? initiation) =>
        initiation is null || (initiation.ChannelCode is null && initiation.Geolocation.Count == 0) ? null : new()
        {
            Details = new() { ChannelCode = initiation.ChannelCode, Geolocation = initiation.Geolocation.ToArray() }
        };

    internal static RelatedRemittanceXml[] Related(PaymentInitiationChannel? channel, RemittanceDeliveryMethod method) =>
        channel is null ? [] : channel.InstrumentCodes.Select(instrument => new RelatedRemittanceXml
        {
            Id = channel.ChannelCode + ":" + instrument,
            Location = channel.ElectronicAddress is { } address ? new() { Method = method, ElectronicAddress = address } : null
        }).ToArray();

    internal static RemittanceXml? Remittance(PaymentRemittance? remittance) =>
        remittance is null || (remittance.Unstructured is null && remittance.Structured.Count == 0) ? null : new()
        {
            Unstructured = ProtocolTextChunks.Split(remittance.Unstructured, ProtocolTextChunks.RemittanceLineLength).ToArray(),
            Structured = remittance.Structured.Select(Reference).ToArray()
        };

    private static StructuredRemittanceXml Reference(PaymentRemittanceReference reference) => new()
    {
        CreditorReference = new()
        {
            Type = new() { Code = new() { Value = reference.Type }, Issuer = reference.Issuer },
            Reference = reference.Reference
        },
        AdditionalInformation = ProtocolTextChunks.Split(reference.AdditionalInformation, ProtocolTextChunks.RemittanceLineLength).ToArray()
    };
}
