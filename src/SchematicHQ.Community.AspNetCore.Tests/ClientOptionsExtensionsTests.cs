using SchematicHQ.Client;
using SchematicHQ.Community.DependencyInjection;
using Shouldly;

namespace SchematicHQ.Community.AspNetCore.Tests;

internal sealed class ClientOptionsExtensionsTests
{
    [Test]
    public void FailOpenFor_defaults_the_given_flags_to_true()
    {
        var options = new ClientOptions().FailOpenFor("reports", "exports");

        options.FlagDefaults.ShouldNotBeNull();
        options.FlagDefaults["reports"].ShouldBeTrue();
        options.FlagDefaults["exports"].ShouldBeTrue();
    }

    [Test]
    public void FailOpenFor_keeps_defaults_already_set()
    {
        var options = new ClientOptions { FlagDefaults = new Dictionary<string, bool> { ["beta"] = false } };

        options.FailOpenFor(["reports"]);

        options.FlagDefaults["beta"].ShouldBeFalse();
        options.FlagDefaults["reports"].ShouldBeTrue();
    }

    [Test]
    public void FailOpenFor_rejects_a_blank_flag_key()
    {
        Should.Throw<ArgumentException>(() => new ClientOptions().FailOpenFor("reports", " "));
    }
}
