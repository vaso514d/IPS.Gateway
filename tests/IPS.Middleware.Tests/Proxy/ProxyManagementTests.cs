using IPS.Middleware.Application.Proxy;
using Xunit;

namespace IPS.Middleware.Tests.Proxy;

public sealed class ProxyManagementTests
{
    private const string Iban = "GE29NB0000000101904917";

    private static ProxyAccountHolder Holder() => new("01001011111", "Individual", "Given", "Surname", null, null, null, null, null, null, null, null);

    private static RegisterProxyRequest Register() => new(Holder(), new ProxyAccount(Iban, true, "GEL", "CACC", null, null), [new("MBNO", "+995599123456")], null, null);

    private static UpdateProxyRequest Update() => new("01001011111", "Individual", Holder(), Iban, "GEL", null, null, null, null);

    private static RemoveProxyRequest Remove() => new("01001011111", "Individual", Iban, "GEL", false, [new("MBNO", "+995599123456")], ["auth-1"], ["owner-1"]);

    [Fact]
    public async Task A_valid_request_is_prepared_sent_once_and_its_answer_is_read_with_the_operation_id()
    {
        var protocol = new Protocol();
        var client = new Client(ProxyReply.Replied("<answer />"));

        var result = await new ProxyManagement(protocol, client).RegisterAsync(Register(), default);

        Assert.Equal(ProxyOutcome.Accept(), result.Outcome);
        Assert.Empty(result.Errors);
        Assert.Equal([(ProxyOperation.Register, "<signed register />")], client.Sent);
        var ids = Assert.Single(protocol.Prepared);
        Assert.Equal(ids.OperationId, protocol.ReadWithOperationId);
        Assert.NotEqual(ids.OperationId, ids.BulkMessageId);
    }

    [Fact]
    public async Task Update_and_remove_are_prepared_and_sent_as_their_own_operation()
    {
        var client = new Client(ProxyReply.Replied("<answer />"));
        var management = new ProxyManagement(new Protocol(), client);

        await management.UpdateAsync(Update(), default);
        await management.RemoveAsync(Remove(), default);

        Assert.Equal([(ProxyOperation.Update, "<signed update />"), (ProxyOperation.Remove, "<signed remove />")], client.Sent);
    }

    [Theory]
    [InlineData(ProxyDelivery.TimedOut)]
    [InlineData(ProxyDelivery.Failed)]
    public async Task No_answer_is_reported_without_an_outcome_and_without_a_second_attempt(ProxyDelivery delivery)
    {
        var client = new Client(new ProxyReply(delivery, null));

        var result = await new ProxyManagement(new Protocol(), client).RegisterAsync(Register(), default);

        Assert.Null(result.Outcome);
        Assert.Equal(delivery, result.Delivery);
        Assert.Single(client.Sent);
    }

    [Fact]
    public async Task Every_call_uses_fresh_references()
    {
        var protocol = new Protocol();
        var management = new ProxyManagement(protocol, new Client(ProxyReply.Replied("<answer />")));

        await management.RegisterAsync(Register(), default);
        await management.RegisterAsync(Register(), default);

        Assert.Equal(2, protocol.Prepared.Select(ids => ids.OperationId).Distinct().Count());
    }

    public static TheoryData<string, string, RegisterProxyRequest> InvalidRegistrations() => new()
    {
        { "no holder", "accountHolder", Register() with { AccountHolder = null } },
        { "empty holder id", "accountHolder.identifier", Register() with { AccountHolder = Holder() with { Identifier = "" } } },
        { "long holder id", "accountHolder.identifier", Register() with { AccountHolder = Holder() with { Identifier = new string('1', 257) } } },
        { "bad holder type", "accountHolder.type", Register() with { AccountHolder = Holder() with { Type = "Company" } } },
        { "control character", "accountHolder.givenName", Register() with { AccountHolder = Holder() with { GivenName = "bad\u0001name" } } },
        { "bad iban", "account.identifier", Register() with { Account = new(  "GE29", true, "GEL", null, null, null) } },
        { "long other account", "account.identifier", Register() with { Account = new(new string('x', 35), false, "GEL", null, null, null) } },
        { "empty account id", "account.identifier", Register() with { Account = new("", false, "GEL", null, null, null) } },
        { "bad currency", "account.currency", Register() with { Account = new(Iban, true, "gel", null, null, null) } },
        { "long account type", "account.type", Register() with { Account = new(Iban, true, "GEL", new string('x', 36), null, null) } },
        { "no channel type", "proxyIdentifiers.type", Register() with { ProxyIdentifiers = [new(null, "alias")] } },
        { "long channel type", "proxyIdentifiers.type", Register() with { ProxyIdentifiers = [new("MBNOX", "alias")] } },
        { "long alias", "proxyIdentifiers.alias", Register() with { ProxyIdentifiers = [new("MBNO", new string('a', 129))] } },
        { "null identifier", "proxyIdentifiers", Register() with { ProxyIdentifiers = [null] } },
        { "person without id", "authorizedPersons.id", Register() with { AuthorizedPersons = [new(null, null, null, null, null, null, null, null, null)] } },
        { "owner with bad text", "beneficialOwners.surname", Register() with { BeneficialOwners = [new("o", null, "bad\u0002", null, null, null, null, null, null)] } }
    };

    [Theory]
    [MemberData(nameof(InvalidRegistrations))]
    public async Task An_invalid_registration_is_reported_at_the_failing_field_and_nothing_is_sent(string name, string field, RegisterProxyRequest request)
    {
        var client = new Client(ProxyReply.Replied("<answer />"));
        var protocol = new Protocol();

        var result = await new ProxyManagement(protocol, client).RegisterAsync(request, default);

        Assert.Null(result.Outcome);
        Assert.Contains(result.Errors, error => error.Field == field);
        Assert.Empty(client.Sent);
        Assert.Empty(protocol.Prepared);
        Assert.NotEmpty(name);
    }

    [Fact]
    public async Task An_update_override_must_be_an_iban_unless_an_updated_account_is_given()
    {
        var management = new ProxyManagement(new Protocol(), new Client(ProxyReply.Replied("<answer />")));

        var invalid = await management.UpdateAsync(Update() with { AccountIdentifier = "PSP-1" }, default);
        var withAccount = await management.UpdateAsync(Update() with { AccountIdentifier = "PSP-1", UpdatedAccount = new("PSP-1", false, "GEL", null, null, null) }, default);

        Assert.Contains(invalid.Errors, error => error.Field == "accountIdentifier");
        Assert.Empty(withAccount.Errors);
    }

    [Fact]
    public async Task An_invalid_update_reports_the_holder_and_the_currency()
    {
        var management = new ProxyManagement(new Protocol(), new Client(ProxyReply.Replied("<answer />")));

        var result = await management.UpdateAsync(Update() with { AccountHolderIdentifier = null, AccountHolderType = "x", AccountCurrency = "GE" }, default);

        string[] expected = ["accountCurrency", "accountHolderIdentifier", "accountHolderType"];
        Assert.Equal(expected, result.Errors.Select(error => error.Field).Order().ToArray());
    }

    [Theory]
    [InlineData(null, "GEL")]
    [InlineData("not-an-iban", "GEL")]
    [InlineData(Iban, "GELL")]
    public async Task A_removal_needs_an_iban_and_a_valid_currency(string? account, string currency)
    {
        var management = new ProxyManagement(new Protocol(), new Client(ProxyReply.Replied("<answer />")));

        var result = await management.RemoveAsync(Remove() with { AccountIdentifier = account, AccountCurrency = currency }, default);

        Assert.NotEmpty(result.Errors);
        Assert.Null(result.Outcome);
    }

    [Fact]
    public async Task A_removal_rejects_a_null_sub_item_and_unsafe_ids()
    {
        var management = new ProxyManagement(new Protocol(), new Client(ProxyReply.Replied("<answer />")));

        var result = await management.RemoveAsync(
            Remove() with { ProxyIdentifiersToRemove = [null], AuthorizedPersonIdsToRemove = ["bad\u0001"], BeneficialOwnerIdsToRemove = [null] }, default);

        Assert.Equal(3, result.Errors.Count);
    }

    [Theory]
    [InlineData("Individual", true)]
    [InlineData("individual", true)]
    [InlineData("LegalEntity", false)]
    public void The_holder_type_is_case_insensitive(string type, bool individual)
    {
        Assert.True(ProxyHolderTypes.IsHolderType(type));
        Assert.Equal(individual, ProxyHolderTypes.IsIndividual(type));
    }

    private sealed class Protocol : IProxyProtocol
    {
        public List<ProxyIds> Prepared { get; } = [];
        public string? ReadWithOperationId { get; private set; }

        public Task<string> PrepareRegisterAsync(RegisterProxyRequest request, ProxyIds ids, CancellationToken cancellationToken) => Prepare(ids, "register");

        public Task<string> PrepareUpdateAsync(UpdateProxyRequest request, ProxyIds ids, CancellationToken cancellationToken) => Prepare(ids, "update");

        public Task<string> PrepareRemoveAsync(RemoveProxyRequest request, ProxyIds ids, CancellationToken cancellationToken) => Prepare(ids, "remove");

        public ProxyOutcome ReadReply(string xml, string operationId)
        {
            ReadWithOperationId = operationId;
            return ProxyOutcome.Accept();
        }

        private Task<string> Prepare(ProxyIds ids, string operation)
        {
            Prepared.Add(ids);
            return Task.FromResult($"<signed {operation} />");
        }
    }

    private sealed class Client(ProxyReply reply) : IProxyClient
    {
        public List<(ProxyOperation Operation, string Xml)> Sent { get; } = [];

        public Task<ProxyReply> SendAsync(ProxyOperation operation, string xml, CancellationToken cancellationToken)
        {
            Sent.Add((operation, xml));
            return Task.FromResult(reply);
        }
    }
}
