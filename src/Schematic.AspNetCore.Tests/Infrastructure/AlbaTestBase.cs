using Alba;

namespace Schematic.AspNetCore.Tests.Infrastructure;

internal abstract class AlbaTestBase
{
    private readonly AlbaBootstrap _bootstrap;

    protected AlbaTestBase(AlbaBootstrap bootstrap)
    {
        _bootstrap = bootstrap;
    }

    protected IAlbaHost Host => _bootstrap.Host;
    protected FakeGateClient FakeClient => _bootstrap.FakeClient;
    protected StubFlagContextResolver Resolver => _bootstrap.Resolver;

    [Before(Test)]
    public void ResetBetweenTests()
    {
        FakeClient.Reset();
        Resolver.Reset();
    }
}
