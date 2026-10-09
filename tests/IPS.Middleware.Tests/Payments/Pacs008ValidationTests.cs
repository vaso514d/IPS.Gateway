using IPS.Middleware.Application.Payments.Pacs008;
using Xunit;

namespace IPS.Middleware.Tests.Payments;

public sealed class Pacs008ValidationTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly Pacs008Policy Policy = new("BAGAGE22", "TRESGE22",
        [new("GEL", MinAmount: 0.01m, MaxAmount: 9999999999999.99999m)], ["INDIRECT"]);
    public static Pacs008Request Minimal() => new()
    {
        ClientReference = "CORE-1",
        InstructionId = "INSTRUCTION-1",
        EndToEndId = "E2E-1",
        CreationDateTime = Created,
        AcceptanceDateTime = Created.AddMilliseconds(500),
        Amount = 10,
        Currency = "GEL",
        InstructionPriority = "NORM",
        Debtor = new() { Type = 1, Name = "Debtor", Account = "GE95TB0000000123456789" },
        Creditor = new() { Type = 1, Name = "Creditor", ParticipantBic = "TBCBGE22", Account = "GE29NB0000000101904917" }
    };
    public static IEnumerable<object[]> InvalidCases()
    {
        var r = Minimal();
        yield return [r with
        {
            ClientReference = null
        }, "clientReference"];
        yield return [r with
        {
            ClientReference = new string ('x', 36)
        }, "clientReference"];
        yield return [r with
        {
            InstructionId = "ქართული"
        }, "instructionId"];
        yield return [r with
        {
            EndToEndId = "PSP-"
        }, "endToEndId"];
        yield return [r with
        {
            CreationDateTime = null
        }, "creationDateTime"];
        yield return [r with
        {
            AcceptanceDateTime = null
        }, "acceptanceDateTime"];
        yield return [r with
        {
            AcceptanceDateTime = Created.AddMilliseconds(-1)
        }, "acceptanceDateTime"];
        yield return [r with
        {
            AcceptanceDateTime = Created.AddMilliseconds(1001)
        }, "acceptanceDateTime"];
        yield return [r with
        {
            EndToEndId = "RTP-original"
        }, "acceptanceDateTime"];
        yield return [r with
        {
            Amount = decimal.MinValue
        }, "amount"];
        yield return [r with
        {
            Amount = 0
        }, "amount"];
        yield return [r with
        {
            Amount = 0.123456m
        }, "amount"];
        yield return [r with
        {
            Amount = 10000000000000m
        }, "amount"];
        yield return [r with
        {
            Currency = "gel"
        }, "currency"];
        yield return [r with
        {
            Currency = "USD"
        }, "currency"];
        yield return [r with
        {
            InstructionPriority = "normal"
        }, "instructionPriority"];
        yield return [r with
        {
            CategoryPurposeCode = "ABCDE"
        }, "categoryPurposeCode"];
        yield return [r with
        {
            Debtor = null
        }, "debtor"];
        yield return [r with
        {
            Creditor = null
        }, "creditor"];
        yield return [r with
        {
            Debtor = r.Debtor! with
            {
                Type = 2
            }
        }, "debtor.type"];
        yield return [r with
        {
            Debtor = r.Debtor! with
            {
                Type = 0,
                Identifier = "400000001"
            }
        }, "debtor.identifier"];
        yield return [r with
        {
            Creditor = r.Creditor! with
            {
                Type = 0,
                Identifier = null
            }
        }, "creditor.identifier"];
        yield return [r with
        {
            Debtor = r.Debtor! with
            {
                ParticipantBic = "OTHER"
            }
        }, "debtor.participantBic"];
        yield return [r with
        {
            Debtor = r.Debtor! with
            {
                IndirectParticipantBic = "UNKNOWN"
            }
        }, "debtor.indirectParticipantBic"];
        yield return [r with
        {
            Debtor = r.Debtor! with
            {
                Account = "GE95TB0000000123456780"
            }
        }, "debtor.account"];
        yield return [r with
        {
            Creditor = r.Creditor! with
            {
                Account = r.Debtor!.Account
            }
        }, "creditor.account"];
        yield return [r with
        {
            Creditor = r.Creditor! with
            {
                ParticipantBic = "BAGAGE22"
            }
        }, "creditor.participantBic"];
        yield return [r with
        {
            Creditor = r.Creditor! with
            {
                Account = "300773150"
            }
        }, "creditor.account"];
        yield return [r with
        {
            Debtor = r.Debtor! with
            {
                Address = new()
                {
                    AddressLines = new string ('x', 491)
                }
            }
        }, "debtor.address.addressLines"];
        yield return [r with
        {
            UltimateDebtor = new()
            {
                Name = "Missing type"
            }
        }, "ultimateDebtor.type"];
        yield return [r with
        {
            InitiationChannelInstrument = new()
            {
                ChannelCode = "MOBL",
                InstrumentCodes = []
            }
        }, "initiationChannelInstrument.instrumentCodes"];
        yield return [r with
        {
            InitiationChannelInstrument = new()
            {
                ChannelCode = "MOBL",
                InstrumentCodes = Enumerable.Repeat("CARD", 11).ToArray()
            }
        }, "initiationChannelInstrument.instrumentCodes"];
        yield return [r with
        {
            Remittance = new()
            {
                Structured = [new()
                {
                    ReferenceType = "MCC",
                    Reference = "abc"
                }

                ]
            }
        }, "remittance.structured.reference"];
        yield return [r with
        {
            Remittance = new()
            {
                Structured = [new()
                {
                    ReferenceType = "SERV",
                    Reference = "abc",
                    AdditionalInformation = new string ('x', 421)
                }

                ]
            }
        }, "remittance.structured.additionalInformation"];
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Invalid_requests_return_field_errors_and_no_validated_payment(Pacs008Request input, string field)
    {
        var result = ValidatedPacs008.Validate(input, Policy);
        Assert.Null(result.Payment);
        Assert.Contains(result.Errors, e => e.Field == field);
    }

    [Fact]
    public void Ordinary_timing_boundaries_and_initiated_payments_are_accepted()
    {
        var request = Minimal();
        foreach (var accepted in new[]
        {
            Created,
            Created.AddSeconds(1)
        }

        )
        {
            AssertValid(request with { AcceptanceDateTime = accepted });
        }

        foreach (var prefix in new[]
        {
            "PSP-",
            "RTP-"
        }

        )
        {
            AssertValid(request with { EndToEndId = prefix + "original", AcceptanceDateTime = Created.AddMinutes(-1) });
        }
    }

    [Fact]
    public void Treasury_and_resident_tax_code_rules_follow_source_scenarios()
    {
        var request = Minimal();
        AssertValid(request with { Creditor = request.Creditor! with { ParticipantBic = "TRESGE22", Account = "300773150" } });
        AssertValid(request with { Debtor = request.Debtor! with { Type = 0, Identifier = "400000002" } });
        AssertValid(request with { Debtor = request.Debtor! with { IndirectParticipantBic = "INDIRECT" } });
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(9999999999999)]
    public void Amount_boundaries_are_accepted(double amount) => AssertValid(Minimal() with { Amount = (decimal)amount });
    [Fact]
    public void Currency_enablement_and_limits_come_from_policy()
    {
        var policy = new Pacs008Policy("BAGAGE22", null, [new("GEL", MinAmount: 10, MaxAmount: 20), new("USD", false)]);
        foreach (var amount in new[]
        {
            10m,
            20m
        }

        )
        {
            Assert.NotNull(ValidatedPacs008.Validate(Minimal() with { Amount = amount }, policy).Payment);
        }

        foreach (var amount in new[]
        {
            9m,
            21m
        }

        )
        {
            Assert.Null(ValidatedPacs008.Validate(Minimal() with { Amount = amount }, policy).Payment);
        }

        Assert.Null(ValidatedPacs008.Validate(Minimal() with { Currency = "USD" }, policy).Payment);
    }

    [Fact]
    public void Collection_mutation_after_validation_cannot_change_the_payment()
    {
        var geo = new List<string>
        {
            "41.7,44.8"
        };
        var instruments = new List<string>
        {
            "CARD"
        };
        var references = new List<Pacs008StructuredRemittanceInput>
        {
            new()
            {
                ReferenceType = "SERV",
                Reference = "original"
            }
        };
        var input = Minimal() with
        {
            PaymentInitiation = new() { Geolocation = geo },
            InitiationChannelInstrument = new() { ChannelCode = "MOBL", InstrumentCodes = instruments },
            Remittance = new() { Structured = references, Unstructured = new string('u', 1000) }
        };
        var result = ValidatedPacs008.Validate(input, Policy);
        Assert.NotNull(result.Payment);
        geo.Clear();
        instruments.Clear();
        references.Clear();
        Assert.Single(result.Payment.PaymentInitiation!.Geolocation);
        Assert.Single(result.Payment.InitiationChannel!.InstrumentCodes);
        Assert.Single(result.Payment.Remittance!.Structured);
    }

    [Fact]
    public void Successful_validation_normalizes_required_values_and_resolves_account_kind()
    {
        var input = Minimal();
        var result = ValidatedPacs008.Validate(input with { InstructionId = " instruction ", CategoryPurposeCode = "othr", CreationDateTime = Created.ToOffset(TimeSpan.FromHours(4)), Debtor = input.Debtor! with { Name = " Debtor ", BillIdentifier = " bill " }, Creditor = input.Creditor! with { ParticipantBic = "TRESGE22", Account = "300773150" }, Remittance = new() { Structured = [new() { ReferenceType = "serv", Reference = " ref ", ReferenceIssuer = " issuer " }] } }, Policy);
        Assert.Empty(result.Errors);
        var payment = Assert.IsType<ValidatedPacs008>(result.Payment);
        Assert.Equal("instruction", payment.InstructionId);
        Assert.Equal("OTHR", payment.CategoryPurposeCode);
        Assert.Equal(TimeSpan.Zero, payment.CreationDateTime.Offset);
        Assert.Equal("Debtor", payment.Debtor.Name);
        Assert.Equal("bill", payment.Debtor.BillIdentifier);
        Assert.Equal(PaymentAccountKind.Treasury, payment.CreditorAccount.Kind);
        Assert.Equal(PaymentAccountKind.Iban, payment.DebtorAccount.Kind);
        Assert.Equivalent(new PaymentRemittanceReference("SERV", "ref", "issuer", null), Assert.Single(payment.Remittance!.Structured), strict: true);
    }

    [Fact]
    public void Child_validation_accumulates_errors_with_existing_paths_and_messages()
    {
        var result = ValidatedPacs008.Validate(Minimal() with
        {
            Debtor = null,
            Creditor = null,
            InitiationChannelInstrument = new() { ChannelCode = "MOBL", InstrumentCodes = [null!] },
            Remittance = new() { Structured = [null!, new() { ReferenceType = "MCC", Reference = "abc" }] }
        }, Policy);
        Assert.Null(result.Payment);
        Assert.Equal(new[]
        {
            "debtor:Debtor is required.",
            "creditor:Creditor is required.",
            "initiationChannelInstrument.instrumentCodes:A value is required.",
            "remittance.structured:A reference cannot be null.",
            "remittance.structured.reference:The value has an invalid format or length."
        }, result.Errors.Select(error => error.Field + ":" + error.Message));
    }

    private static void AssertValid(Pacs008Request request)
    {
        var result = ValidatedPacs008.Validate(request, Policy);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Payment);
    }
}
