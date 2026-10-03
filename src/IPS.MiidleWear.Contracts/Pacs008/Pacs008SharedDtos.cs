using System.Text.Json.Serialization;

namespace IPS.MiidleWear.Contracts.Pacs008;

// Agent, code-choice and party shapes shared by the pacs.009 and pacs.004 contracts (pacs.008 has its own
// Bank/Core contract: Pacs008InstantPaymentRequestDto).

public sealed record Pacs008AgentDto
{
    /// <summary>FinInstnId/BICFI.</summary>
    [JsonPropertyName("BICFI")]
    public string? Bicfi { get; init; }

    /// <summary>FinInstnId/ClrSysMmbId/MmbId (ClrSysId/Cd is always "GE").</summary>
    [JsonPropertyName("ClrSysMmbId")]
    public string? ClrSysMmbId { get; init; }
}

/// <summary>Code or proprietary value (Type 0 = Cd, 1 = Prtry). Used by pacs.009.</summary>
public sealed record Pacs008CodeChoiceDto
{
    [JsonPropertyName("Type")]
    public int? Type { get; init; }

    [JsonPropertyName("Value")]
    public string? Value { get; init; }
}

/// <summary>Debtor or creditor: name, identification and IBAN account.</summary>
public sealed record Pacs008PartyDto
{
    /// <summary>Nm (Dbtr Max140Text, Cdtr Max70Text).</summary>
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    /// <summary>DbtrAcct/CdtrAcct Id/IBAN.</summary>
    [JsonPropertyName("Account")]
    public string? Account { get; init; }

    /// <summary>0 = legal entity (Id/OrgId/Othr/Id), 1 = individual (Id/PrvtId/Othr/Id).</summary>
    [JsonPropertyName("Type")]
    public int? Type { get; init; }

    /// <summary>Id/OrgId/Othr/Id or Id/PrvtId/Othr/Id (Max256Text), chosen by <see cref="Type"/>.</summary>
    [JsonPropertyName("Id")]
    public string? Id { get; init; }
}
