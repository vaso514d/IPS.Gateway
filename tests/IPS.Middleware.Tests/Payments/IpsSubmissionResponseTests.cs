using IPS.Middleware.Application.Payments.Pacs008;
using Xunit;

namespace IPS.Middleware.Tests.Payments;

public sealed class IpsSubmissionResponseTests
{
    [Fact]
    public void Response_snapshots_headers_preserving_repeated_names_empty_body_and_values()
    {
        var headers = new List<IpsResponseHeader> { new("X-Test", "one"), new("X-Test", "") };
        var response = new IpsSubmissionResponse(204, "", headers);
        headers[0] = new("X-Test", "changed");
        headers.Clear();
        Assert.Equal("one", response.Headers[0].Value);
        Assert.Equal("", response.Headers[1].Value);
        Assert.Equal("", response.Body);
        Assert.Throws<NotSupportedException>(() => ((IList<IpsResponseHeader>)response.Headers).Clear());
    }
}
