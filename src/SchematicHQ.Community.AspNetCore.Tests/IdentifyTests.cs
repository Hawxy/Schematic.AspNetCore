using System.Net;
using Alba;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchematicHQ.Community.AspNetCore.Options;
using SchematicHQ.Community.AspNetCore.Resolvers;
using SchematicHQ.Community.AspNetCore.TestApp;
using SchematicHQ.Community.DependencyInjection;
using SchematicHQ.Community.AspNetCore.Tests.Infrastructure;
using SchematicHQ.Client;
using Shouldly;

namespace SchematicHQ.Community.AspNetCore.Tests;

internal sealed class IdentifyTests
{
    private static SchematicIdentifyContext DefaultIdentity(string userId = "user_1") => new(
        Keys: new() { ["userId"] = userId },
        Company: new EventBodyIdentifyCompany { Keys = new() { ["companyId"] = "company_1" }, Name = "Acme" },
        Name: "User One",
        Traits: new() { ["plan"] = "pro" });

    private static Task<IAlbaHost> CreateHost(
        FakeGateClient fake,
        StubIdentifyContextResolver resolver,
        Action<SchematicAspNetCoreOptions>? configure = null)
        => AlbaHost.For<Program>(webHost =>
        {
            webHost.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ISchematicGateClient>(fake));
                services.AddSingleton<ISchematicFlagContextResolver>(new StubFlagContextResolver());
                services.AddSingleton<ISchematicIdentifyContextResolver>(resolver);
                services.AddSingleton<IStartupFilter>(new UseIdentifyStartupFilter());
                if (configure is not null)
                    services.Configure(configure);
            });
        });

    // Prepends UseSchematicIdentify to the TestApp pipeline without modifying the shared Program.
    private sealed class UseIdentifyStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.UseSchematicIdentify();
                next(app);
            };
    }

    [Test]
    public async Task Identify_fires_with_resolved_context()
    {
        var fake = new FakeGateClient();
        await using var host = await CreateHost(fake, new StubIdentifyContextResolver(DefaultIdentity()));

        await host.Scenario(_ =>
        {
            _.Get.Url("/min/no-meta");
            _.StatusCodeShouldBeOk();
        });

        fake.IdentifyCalls.Count.ShouldBe(1);
        var call = fake.IdentifyCalls[0];
        call.Keys["userId"].ShouldBe("user_1");
        call.Company!.Keys["companyId"].ShouldBe("company_1");
        call.Company.Name.ShouldBe("Acme");
        call.Name.ShouldBe("User One");
        call.Traits!["plan"].ShouldBe("pro");
    }

    [Test]
    public async Task No_identify_when_resolver_returns_null()
    {
        var fake = new FakeGateClient();
        await using var host = await CreateHost(fake, new StubIdentifyContextResolver(context: null));

        await host.Scenario(_ =>
        {
            _.Get.Url("/min/no-meta");
            _.StatusCodeShouldBeOk();
        });

        fake.IdentifyCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task OPTIONS_preflight_skips_identify()
    {
        var fake = new FakeGateClient();
        await using var host = await CreateHost(fake, new StubIdentifyContextResolver(DefaultIdentity()));

        using var client = host.Server.CreateClient();
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/min/cors-preflight"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        fake.IdentifyCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task Dedup_window_sends_one_identify_per_identity()
    {
        var fake = new FakeGateClient();
        var resolver = new StubIdentifyContextResolver(DefaultIdentity());
        await using var host = await CreateHost(fake, resolver,
            o => o.IdentifyDeduplicationWindow = TimeSpan.FromMinutes(5));

        for (var i = 0; i < 2; i++)
        {
            await host.Scenario(_ =>
            {
                _.Get.Url("/min/no-meta");
                _.StatusCodeShouldBeOk();
            });
        }

        fake.IdentifyCalls.Count.ShouldBe(1);

        resolver.SetContext(DefaultIdentity(userId: "user_2"));
        await host.Scenario(_ =>
        {
            _.Get.Url("/min/no-meta");
            _.StatusCodeShouldBeOk();
        });

        fake.IdentifyCalls.Count.ShouldBe(2);
        fake.IdentifyCalls[1].Keys["userId"].ShouldBe("user_2");
    }

    [Test]
    public async Task Identify_failure_does_not_fail_request()
    {
        var fake = new FakeGateClient { ThrowOnIdentify = true };
        await using var host = await CreateHost(fake, new StubIdentifyContextResolver(DefaultIdentity()));

        await host.Scenario(_ =>
        {
            _.Get.Url("/min/no-meta");
            _.StatusCodeShouldBeOk();
        });

        fake.IdentifyCalls.ShouldBeEmpty();
    }
}
