using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IPS.MiidleWear.Contracts.Transactions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static IPS.Middleware.IntegrationTests.Payments.OutgoingHostFixture;

namespace IPS.Middleware.IntegrationTests.Payments;

public sealed class OutgoingBindingTests
{
    private const string Send = "/api/ips/pacs008/send";

    [Theory]
    [InlineData("{", "application/json", 400)]
    [InlineData("null", "application/json", 400)]
    [InlineData("[]", "application/json", 400)]
    [InlineData("", "application/json", 400)]
    [InlineData("{}", "text/plain", 415)]
    [InlineData("{}", "text/json", 415)]
    [InlineData("", "text/plain", 415)]
    [InlineData("{\"amount\":\"invalid\"}", "application/json", 400)]
    public async Task Invalid_body_never_reaches_intake(string body, string contentType, int expected)
    {
        await using var fixture = await CreateAsync();
        using var host = fixture.Host();
        using var client = host.CreateClient();
        using var response = await client.PostAsync(Send, new StringContent(body, Encoding.UTF8, contentType));
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Empty(fixture.Submissions);
        await using var db = fixture.Database.Context();
        Assert.Empty(await db.Payments.ToListAsync());
    }

    [Fact]
    public async Task A_missing_body_is_rejected_before_intake()
    {
        await using var fixture = await CreateAsync();
        using var host = fixture.Host();
        using var client = host.CreateClient();
        using var response = await client.PostAsync(Send, null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(fixture.Submissions);
    }

    [Fact]
    public async Task Application_validation_retains_problem_shape_for_an_empty_object()
    {
        await using var fixture = await CreateAsync();
        using var host = fixture.Host();
        using var client = host.CreateClient();
        using var response = await client.PostAsJsonAsync(Send, new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problem = document.RootElement;
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("One or more validation errors occurred.", problem.GetProperty("title").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("clientReference", out _));
        Assert.False(problem.TryGetProperty("traceId", out _));
    }

    [Theory]
    [InlineData("999", 400)]
    [InlineData("", 400)]
    [InlineData("Pacs009", 404)]
    [InlineData("Pacs008&messageKind=Pacs009", 404)]
    [InlineData("Pacs008&messageKind=invalid", 400)]
    public async Task Query_binding_preserves_message_kind_validation(string kind, int expected)
    {
        await using var fixture = await CreateAsync();
        using var host = fixture.Host();
        using var client = host.CreateClient();
        using var response = await client.GetAsync($"/api/ips/transactions/status?messageKind={kind}&clientReference=missing");
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected == 404)
        {
            Assert.Equal("", await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("utf-8", 0)]
    [InlineData("utf-16", 0)]
    [InlineData("iso-8859-1", 0)]
    [InlineData("utf-8", 40)]
    public async Task Json_reader_preserves_charsets_quoted_numbers_and_ignored_property_depth(string encoding, int depth)
    {
        await using var fixture = await CreateAsync();
        using var host = fixture.Host();
        using var client = host.CreateClient();
        var document = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Request(), JsonSerializerOptions.Web))!;
        document["amount"] = Request().Amount!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var nested = string.Concat(Enumerable.Repeat("{\"a\":", depth)) + "0" + new string('}', depth);
        document["unknown"] = System.Text.Json.Nodes.JsonNode.Parse(nested);
        using var response = await client.PostAsync(Send, new StringContent(document.ToJsonString(), Encoding.GetEncoding(encoding), "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(fixture.Submissions);
    }

    [Fact]
    public async Task Enabled_openapi_retains_routes_operation_names_and_response_contracts()
    {
        await using var fixture = await CreateAsync();
        using var host = fixture.Host();
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var send = paths.GetProperty(Send).GetProperty("post");
        Assert.Equal("SendPacs008", send.GetProperty("operationId").GetString());
        var responses = send.GetProperty("responses");
        foreach (var code in new[] { "200", "504" })
        {
            var content = responses.GetProperty(code).GetProperty("content");
            Assert.Equal("application/json", Assert.Single(content.EnumerateObject()).Name);
        }
        Assert.True(responses.GetProperty("400").GetProperty("content").TryGetProperty("application/problem+json", out _));
        Assert.Equal("GetTransactionStatus", paths.GetProperty("/api/ips/transactions/status").GetProperty("get").GetProperty("operationId").GetString());
        var pacs009 = paths.GetProperty("/api/ips/pacs009/send").GetProperty("post");
        Assert.Equal("SendPacs009", pacs009.GetProperty("operationId").GetString());
        foreach (var code in new[] { "200", "504", "400" })
        {
            Assert.True(pacs009.GetProperty("responses").TryGetProperty(code, out _));
        }

        Assert.Equal(4, paths.EnumerateObject().Count());
    }

    [Fact]
    public async Task Json_binding_retains_web_defaults_and_response_shape()
    {
        await using var fixture = await CreateAsync();
        using var host = fixture.Host();
        using var client = host.CreateClient();
        var request = JsonSerializer.Serialize(Request(), JsonSerializerOptions.Web);
        using var response = await client.PostAsync(Send, new StringContent(request.Replace("\"clientReference\"", "\"CLIENTREFERENCE\"", StringComparison.Ordinal), Encoding.UTF8, "application/vendor+json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Accepted", document.RootElement.GetProperty("status").GetString());
        Assert.False(document.RootElement.TryGetProperty("failureReason", out _));
        using var multipleReferences = await client.GetAsync("/api/ips/transactions/status?messageKind=Pacs008&clientReference=outgoing&clientReference=another");
        Assert.Equal(HttpStatusCode.NotFound, multipleReferences.StatusCode);
        using var wrongMethod = await client.GetAsync(Send);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
    }
}
