using System.Text.Json;
using Alba;
using Schematic.AspNetCore.Tests.Infrastructure;
using Schematic.AspNetCore.TestApp;
using SchematicHQ.Client.Webhooks.WebhookUtils;
using Shouldly;

namespace Schematic.AspNetCore.Tests;

[ClassDataSource<AlbaBootstrap>(Shared = SharedType.PerTestSession)]
[NotInParallel(nameof(AlbaBootstrap))]
internal sealed class WebhookSignatureTests : AlbaTestBase
{
    private const string Body = """{"type":"flag.updated","data":{"key":"test-flag"}}""";

    public WebhookSignatureTests(AlbaBootstrap bootstrap) : base(bootstrap) { }

    private static string Sign(string body, string timestamp)
        => WebhookVerifier.ComputeHexSignature(body, timestamp, TestEndpoints.WebhookSecret);

    [Test]
    public async Task Valid_signature_passes_and_body_remains_readable_by_the_endpoint()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        var result = await Host.Scenario(_ =>
        {
            _.Post.Text(Body).ToUrl("/webhook");
            _.WithRequestHeader(WebhookVerifier.WebhookSignatureHeader, Sign(Body, timestamp));
            _.WithRequestHeader(WebhookVerifier.WebhookTimestampHeader, timestamp);
            _.StatusCodeShouldBeOk();
        });

        result.ReadAsText().ShouldBe(Body);
    }

    [Test]
    public async Task Invalid_signature_returns_401_problem_details()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        var result = await Host.Scenario(_ =>
        {
            _.Post.Text(Body).ToUrl("/webhook");
            _.WithRequestHeader(WebhookVerifier.WebhookSignatureHeader, "deadbeef");
            _.WithRequestHeader(WebhookVerifier.WebhookTimestampHeader, timestamp);
            _.StatusCodeShouldBe(401);
        });

        var problem = JsonDocument.Parse(result.ReadAsText()).RootElement;
        problem.GetProperty("title").GetString().ShouldBe("Webhook signature verification failed");
    }

    [Test]
    public async Task Tampered_body_returns_401()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var signatureForDifferentBody = Sign("""{"type":"other"}""", timestamp);

        await Host.Scenario(_ =>
        {
            _.Post.Text(Body).ToUrl("/webhook");
            _.WithRequestHeader(WebhookVerifier.WebhookSignatureHeader, signatureForDifferentBody);
            _.WithRequestHeader(WebhookVerifier.WebhookTimestampHeader, timestamp);
            _.StatusCodeShouldBe(401);
        });
    }

    [Test]
    public async Task Missing_signature_headers_return_401()
    {
        await Host.Scenario(_ =>
        {
            _.Post.Text(Body).ToUrl("/webhook");
            _.StatusCodeShouldBe(401);
        });
    }
}
