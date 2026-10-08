using FluentValidation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008.Validation;

namespace IPS.Middleware.Application.Payments.Pain002.Validation;

// The source's content rules for an outgoing pain.002 (Annex D 3.2.12): the identifiers are those of the refused pain.001.
// Nothing is checked against the initiation itself.
internal sealed class Pain002Validator : AbstractValidator<Pain002Request>
{
    public Pain002Validator()
    {
        RuleFor(x => x.ClientReference).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.Id).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.OriginalMessageId).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.OriginalPaymentInformationId).ProtocolText(Pacs008Text.AsciiId(35), required: true);
        RuleFor(x => x.ReasonCode).ProtocolText("^[A-Z0-9]{1,4}$", required: true);
        RuleFor(x => x.AdditionalInformation).ProtocolText(Pacs008Text.FreeText(105));
        RuleFor(x => x.OriginatorName).ProtocolText(Pacs008Text.FreeText(140));
    }
}
