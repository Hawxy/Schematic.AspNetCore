using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchematicHQ.Community.AspNetCore.Options;
using SchematicHQ.Community.DependencyInjection;
using SchematicHQ.Community.Extensions.AI;
using SchematicHQ.Community.Testing;
using Shouldly;

namespace SchematicHQ.Community.AspNetCore.Tests;

/// <summary>
/// A denied AI call inside a request handler surfaces as an exception. The middleware turns it into the same
/// response the endpoint gate writes, so a client sees one shape of denial whichever layer produced it.
/// </summary>
internal sealed class FeatureDeniedHandlerTests
{
    private static async Task<WebApplication> StartAsync(Action<SchematicAspNetCoreOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSchematicFake(new FakeSchematicGateClient());
        builder.Services.AddSchematicAspNetCore(configure);
        var app = builder.Build();
        app.UseSchematicFeatureDeniedResponses();
        app.MapGet("/denied", IResult () => throw new SchematicFeatureDeniedException("ai-chat", "insufficient_credits"));
        app.MapGet("/ok", () => Results.Ok());
        await app.StartAsync();
        return app;
    }

    [Test]
    public async Task Denied_exception_becomes_the_gate_403_problem_details()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        var denied = await client.GetAsync("/denied");

        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var problem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("title").GetString().ShouldBe("Entitlement denied");
        problem.GetProperty("featureId").GetString().ShouldBe("ai-chat");
        problem.GetProperty("accessDeniedReason").GetString().ShouldBe("insufficient_credits");
    }

    [Test]
    public async Task Requests_without_a_denial_are_untouched()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        (await client.GetAsync("/ok")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Custom_OnDenied_is_honoured()
    {
        await using var app = await StartAsync(o => o.OnDenied = (http, denial) =>
            Results.Json(new { denied = denial.FeatureId }, statusCode: 402).ExecuteAsync(http));
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/denied");

        response.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("denied").GetString().ShouldBe("ai-chat");
    }
}
