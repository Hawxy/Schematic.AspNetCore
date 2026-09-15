using SchematicHQ.Client;
using SchematicHQ.Client.RulesEngine;

namespace SchematicHQ.Community.AspNetCore.Tests.Infrastructure;

internal static class CheckResponses
{
    public static CheckFlagWithEntitlementResponse Allow(string flag, string reason = "ok") =>
        new() { FlagKey = flag, Value = true, Reason = reason };

    public static CheckFlagWithEntitlementResponse Deny(string flag, string reason = "not_entitled") =>
        new() { FlagKey = flag, Value = false, Reason = reason };

    /// <summary>An allow backed by a credit-burndown entitlement, as the rules engine reports it.</summary>
    public static CheckFlagWithEntitlementResponse AllowWithCredits(
        string flag,
        double consumptionRate,
        string companyId = "company_1",
        string creditId = "credit_1",
        double creditRemaining = 1_000) =>
        new()
        {
            FlagKey = flag,
            Value = true,
            Reason = "ok",
            CompanyId = companyId,
            Entitlement = new RulesengineFeatureEntitlement
            {
                FeatureId = "feat_1",
                FeatureKey = flag,
                ValueType = RulesengineEntitlementValueType.Credit,
                CreditId = creditId,
                ConsumptionRate = consumptionRate,
                CreditRemaining = creditRemaining,
            },
        };
}
