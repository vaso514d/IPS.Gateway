using System.Text.RegularExpressions;
using FluentValidation;

namespace IPS.Middleware.Application.Payments.Pacs008.Validation;

internal sealed class PaymentInitiationValidator : AbstractValidator<Pacs008PaymentInitiationInput>
{
    public PaymentInitiationValidator()
    {
        RuleFor(x => x.ChannelCode).ProtocolText(Pacs008Text.Text(10));
        RuleForEach(x => x.Geolocation).Must(value => value is null ||
            Regex.IsMatch(value, Pacs008Text.Text(35), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            .WithMessage("Geolocation has an invalid format or length.");
    }
}

internal sealed class InitiationChannelValidator : AbstractValidator<Pacs008InitiationChannelInstrumentInput>
{
    public InitiationChannelValidator()
    {
        RuleFor(x => x.ChannelCode).ProtocolText(Pacs008Text.Code4Upper, required: true);
        RuleFor(x => x.InstrumentCodes).Must(codes => codes is { Count: >= 1 and <= 10 })
            .WithMessage("Provide between 1 and 10 instruments.");
        RuleForEach(x => x.InstrumentCodes).ProtocolText(Pacs008Text.Code4Upper, required: true);
        RuleFor(x => x.ElectronicAddress).ProtocolText(Pacs008Text.Text(2048));
    }
}

internal sealed class RemittanceValidator : AbstractValidator<Pacs008RemittanceInput>
{
    public RemittanceValidator()
    {
        RuleFor(x => x.Unstructured).ProtocolText(Pacs008Text.TextAnyLength);
        RuleForEach(x => x.Structured).NotNull().WithMessage("A reference cannot be null.");
        RuleForEach(x => x.Structured).SetValidator(new StructuredRemittanceValidator());
    }
}

internal sealed class StructuredRemittanceValidator : AbstractValidator<Pacs008StructuredRemittanceInput>
{
    // CdtrRefInf/Tp/CdOrPrtry/Prtry codes of the NBG pacs.008 field workbook (row 42). Annex D 3.2.1 (4l) interprets only
    // MCC and SERV and passes other values through, so the gateway refuses the rest before they reach IPS.
    private static readonly string[] ReferenceTypes = ["MCC", "SERV", "BILL", "RSCUSORDER"];

    public StructuredRemittanceValidator()
    {
        RuleFor(x => x.ReferenceType).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.ReferenceType)
            .Must(type => ReferenceTypes.Contains(type!.Trim(), StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.ReferenceType))
            .WithMessage("Reference type must be MCC, SERV, BILL or RSCUSORDER.");
        RuleFor(x => x.Reference).ProtocolText(Pacs008Text.Text(35), required: true);
        RuleFor(x => x.Reference).ProtocolText("^[0-9]{4}$", required: true)
            .When(x => string.Equals(x.ReferenceType?.Trim(), "MCC", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.ReferenceIssuer).ProtocolText(Pacs008Text.Text(35));
        RuleFor(x => x.AdditionalInformation).ProtocolText(Pacs008Text.Text(420));
    }
}
