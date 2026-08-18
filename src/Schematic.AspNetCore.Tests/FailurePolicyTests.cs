using System.Text.Json;
using Alba;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schematic.AspNetCore.Options;
using Schematic.AspNetCore.Resolvers;
using Schematic.AspNetCore.TestApp;
using Schematic.DependencyInjection;
using Schematic.AspNetCore.Tests.Infrastructure;
using Shouldly;

namespace Schematic.AspNetCore.Tests;

[ClassDataSource<AlbaBootstrap>(Shared = SharedType.PerTestSession)]
[NotInParallel(nameof(AlbaBootstrap))]
internal sealed class FailurePolicyTests : AlbaTestBase
{
    public FailurePolicyTests(AlbaBootstrap bootstrap) : base(bootstrap) { }

    [Test]
    public async Task Check_failure_with_default_policy_returns_503_problem_details()
    {
        FakeClient.RespondToCheck(_ => throw new HttpRequestException("schematic unreachable"));

        var result = await Host.Scenario(_ =>
        {
            _.Get.Url("/min/gate");
            _.StatusCodeShouldBe(503);
        });

        var problem = JsonDocument.Parse(result.ReadAsText()).RootElement;
        problem.GetProperty("title").GetString().ShouldBe("Entitlement check failed");
        problem.GetProperty("featureId").GetString().ShouldBe(TestEndpoints.MinimalAllowedFlag);
        FakeClient.TrackCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Check_failure_with_fail_open_continues_pipeline_without_auto_track()
    {
        var fake = new FakeGateClient();
        fake.RespondToCheck(_ => throw new HttpRequestException("schematic unreachable"));

        await using var host = await AlbaHost.For<Program>(webHost =>
        {
            webHost.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ISchematicGateClient>(fake));
                services.AddSingleton<ISchematicFlagContextResolver>(new StubFlagContextResolver());
                services.Configure<SchematicAspNetCoreOptions>(o => o.FailurePolicy = SchematicFailurePolicy.FailOpen);
            });
        });

        await host.Scenario(_ =>
        {
            _.Get.Url("/min/auto-track");
            _.StatusCodeShouldBeOk();
        });

        fake.CheckCalls.Count.ShouldBe(1);
        fake.TrackCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Track_failure_does_not_affect_successful_response()
    {
        FakeClient.ThrowOnTrack = true;

        await Host.Scenario(_ =>
        {
            _.Get.Url("/min/track");
            _.StatusCodeShouldBeOk();
        });

        FakeClient.TrackCalls.ShouldBeEmpty();
    }
}
