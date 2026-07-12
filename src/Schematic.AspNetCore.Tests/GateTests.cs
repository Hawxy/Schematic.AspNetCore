using System.Net;
using System.Text.Json;
using Alba;
using Schematic.AspNetCore.Tests.Infrastructure;
using Schematic.AspNetCore.TestApp;
using Shouldly;

namespace Schematic.AspNetCore.Tests;

[ClassDataSource<AlbaBootstrap>(Shared = SharedType.PerTestSession)]
[NotInParallel(nameof(AlbaBootstrap))]
internal sealed class GateTests : AlbaTestBase
{
    public GateTests(AlbaBootstrap bootstrap) : base(bootstrap) { }

    [Test]
    public async Task RequireFeature_returns_200_when_flag_allows()
    {
        FakeClient.RespondToCheck(flag => CheckResponses.Allow(flag));

        await Host.Scenario(_ =>
        {
            _.Get.Url("/min/gate");
            _.StatusCodeShouldBeOk();
        });

        FakeClient.CheckCalls.Count.ShouldBe(1);
        FakeClient.CheckCalls[0].FlagKey.ShouldBe(TestEndpoints.MinimalAllowedFlag);
        FakeClient.CheckCalls[0].Company["id"].ShouldBe("company_test");
        FakeClient.CheckCalls[0].User["id"].ShouldBe("user_test");
    }

    [Test]
    public async Task RequireFeature_returns_403_problem_details_when_flag_denies()
    {
        FakeClient.RespondToCheck(flag => CheckResponses.Deny(flag, reason: "feature_not_entitled"));

        var result = await Host.Scenario(_ =>
        {
            _.Get.Url("/min/gate");
            _.StatusCodeShouldBe(403);
        });

        var problem = JsonDocument.Parse(result.ReadAsText()).RootElement;
        problem.GetProperty("status").GetInt32().ShouldBe(403);
        problem.GetProperty("title").GetString().ShouldBe("Entitlement denied");
        problem.GetProperty("featureId").GetString().ShouldBe(TestEndpoints.MinimalAllowedFlag);
        problem.GetProperty("accessDeniedReason").GetString().ShouldBe("feature_not_entitled");
    }

    [Test]
    public async Task RequireFeature_returns_401_when_flag_context_is_unresolvable()
    {
        Resolver.SetContext(null);

        await Host.Scenario(_ =>
        {
            _.Get.Url("/min/gate");
            _.StatusCodeShouldBe(401);
        });

        FakeClient.CheckCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Endpoint_without_RequireFeature_metadata_skips_check_and_returns_200()
    {
        await Host.Scenario(_ =>
        {
            _.Get.Url("/min/no-meta");
            _.StatusCodeShouldBeOk();
        });

        FakeClient.CheckCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Controller_RequireFeature_attribute_returns_403_when_denied()
    {
        FakeClient.RespondToCheck(flag => CheckResponses.Deny(flag));

        await Host.Scenario(_ =>
        {
            _.Get.Url("/api/test/gate");
            _.StatusCodeShouldBe(403);
        });

        FakeClient.CheckCalls.Count.ShouldBe(1);
        FakeClient.CheckCalls[0].FlagKey.ShouldBe(TestEndpoints.ControllerFlag);
    }

    [Test]
    public async Task Controller_RequireFeature_attribute_returns_200_when_allowed()
    {
        FakeClient.RespondToCheck(flag => CheckResponses.Allow(flag));

        await Host.Scenario(_ =>
        {
            _.Get.Url("/api/test/gate");
            _.StatusCodeShouldBeOk();
        });

        FakeClient.CheckCalls.Count.ShouldBe(1);
    }

    [Test]
    public async Task OPTIONS_preflight_bypasses_gate_and_track_filters()
    {
        // if the gate filter ran, the FakeGateClient would throw.
        using var client = Host.Server.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/min/cors-preflight");
        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        FakeClient.CheckCalls.ShouldBeEmpty();
        FakeClient.TrackCalls.ShouldBeEmpty();
    }
}
