using Alba;
using Schematic.AspNetCore.Tests.Infrastructure;
using Schematic.AspNetCore.TestApp;
using Shouldly;

namespace Schematic.AspNetCore.Tests;

[ClassDataSource<AlbaBootstrap>(Shared = SharedType.PerTestSession)]
[NotInParallel(nameof(AlbaBootstrap))]
internal sealed class TrackTests : AlbaTestBase
{
    public TrackTests(AlbaBootstrap bootstrap) : base(bootstrap) { }

    [Test]
    public async Task RequireFeature_with_track_emits_one_track_call_named_after_flag()
    {
        FakeClient.RespondToCheck(flag => CheckResponses.Allow(flag));

        await Host.Scenario(_ =>
        {
            _.Get.Url("/min/auto-track");
            _.StatusCodeShouldBeOk();
        });

        FakeClient.CheckCalls.Count.ShouldBe(1);
        FakeClient.TrackCalls.Count.ShouldBe(1);
        FakeClient.TrackCalls[0].EventName.ShouldBe(TestEndpoints.MinimalAutoTrackFlag);
        FakeClient.TrackCalls[0].Quantity.ShouldBeNull();
    }

    [Test]
    public async Task TrackFeature_emits_track_call_on_2xx_with_configured_quantity()
    {
        await Host.Scenario(_ =>
        {
            _.Get.Url("/min/track");
            _.StatusCodeShouldBeOk();
        });

        FakeClient.CheckCalls.ShouldBeEmpty();
        FakeClient.TrackCalls.Count.ShouldBe(1);
        FakeClient.TrackCalls[0].EventName.ShouldBe(TestEndpoints.ExplicitTrackEvent);
        FakeClient.TrackCalls[0].Quantity.ShouldBe(7);
    }

    [Test]
    public async Task TrackFeature_does_not_track_when_endpoint_returns_5xx()
    {
        await Host.Scenario(_ =>
        {
            _.Get.Url("/min/track-on-error");
            _.StatusCodeShouldBe(500);
        });

        FakeClient.TrackCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Endpoint_without_track_metadata_does_not_track()
    {
        await Host.Scenario(_ =>
        {
            _.Get.Url("/min/no-meta");
            _.StatusCodeShouldBeOk();
        });

        FakeClient.TrackCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Controller_TrackFeature_attribute_emits_track_call_on_success()
    {
        await Host.Scenario(_ =>
        {
            _.Get.Url("/api/test/track");
            _.StatusCodeShouldBeOk();
        });

        FakeClient.TrackCalls.Count.ShouldBe(1);
        FakeClient.TrackCalls[0].EventName.ShouldBe(TestEndpoints.ControllerTrackEvent);
        FakeClient.TrackCalls[0].Quantity.ShouldBe(3);
    }
}
