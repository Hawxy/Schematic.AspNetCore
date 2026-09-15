namespace SchematicHQ.Community.DependencyInjection;

/// <summary>
/// A hold on a company's credit balance. Track events sent against the lease settle from the hold, and
/// releasing the lease returns whatever was not tracked.
/// </summary>
public sealed record SchematicCreditLease(string Id, double GrantedAmount);
