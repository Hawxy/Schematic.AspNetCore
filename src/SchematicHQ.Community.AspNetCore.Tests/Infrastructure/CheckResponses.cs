using SchematicHQ.Client.RulesEngine;

namespace SchematicHQ.Community.AspNetCore.Tests.Infrastructure;

internal static class CheckResponses
{
    public static CheckFlagWithEntitlementResponse Allow(string flag, string reason = "ok") =>
        new() { FlagKey = flag, Value = true, Reason = reason };

    public static CheckFlagWithEntitlementResponse Deny(string flag, string reason = "not_entitled") =>
        new() { FlagKey = flag, Value = false, Reason = reason };
}
