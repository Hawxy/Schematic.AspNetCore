using SchematicHQ.Community.DependencyInjection;
using SchematicHQ.Client;
using SchematicHQ.Client.RulesEngine;

namespace SchematicHQ.Community.AspNetCore.Tests.Infrastructure;

internal sealed record IdentifyCall(
    Dictionary<string, string> Keys,
    EventBodyIdentifyCompany? Company,
    string? Name,
    Dictionary<string, object?>? Traits,
    IdentifyOptions? Options = null);

internal sealed record TrackCall(
    string EventName,
    Dictionary<string, string> Company,
    Dictionary<string, string> User,
    Dictionary<string, object?> Traits,
    int? Quantity,
    TrackOptions? Options = null);

internal sealed record CheckCall(
    string FlagKey,
    Dictionary<string, string> Company,
    Dictionary<string, string> User);

internal sealed record LeaseCall(
    string CompanyId,
    string CreditTypeId,
    double RequestedAmount,
    DateTime? ExpiresAt);

internal sealed record ExtendLeaseCall(string LeaseId, double AdditionalAmount);

internal sealed record LeaseTrackCall(
    string LeaseId,
    string EventName,
    Dictionary<string, string> Company,
    Dictionary<string, string> User,
    Dictionary<string, object?> Traits,
    long Quantity);

internal sealed class FakeGateClient : ISchematicGateClient
{
    private readonly object _lock = new();
    private Func<string, CheckFlagWithEntitlementResponse>? _checkResponder;
    private int _leaseCounter;

    public List<CheckCall> CheckCalls { get; } = new();
    public List<TrackCall> TrackCalls { get; } = new();
    public List<IdentifyCall> IdentifyCalls { get; } = new();
    public List<LeaseCall> LeaseCalls { get; } = new();
    public List<ExtendLeaseCall> ExtendLeaseCalls { get; } = new();
    public List<string> ReleasedLeases { get; } = new();
    public List<LeaseTrackCall> LeaseTrackCalls { get; } = new();
    public bool ThrowOnTrack { get; set; }
    public bool ThrowOnIdentify { get; set; }
    public bool ThrowOnLeaseTrack { get; set; }

    /// <summary>Answer every lease request with "insufficient credit" (a null lease).</summary>
    public bool RejectLease { get; set; }

    /// <summary>Thrown from <see cref="AcquireCreditLeaseAsync"/> when set.</summary>
    public Exception? ThrowOnAcquireLease { get; set; }

    public void Reset()
    {
        lock (_lock)
        {
            _checkResponder = null;
            ThrowOnTrack = false;
            ThrowOnIdentify = false;
            ThrowOnLeaseTrack = false;
            RejectLease = false;
            ThrowOnAcquireLease = null;
            CheckCalls.Clear();
            TrackCalls.Clear();
            IdentifyCalls.Clear();
            LeaseCalls.Clear();
            ExtendLeaseCalls.Clear();
            ReleasedLeases.Clear();
            LeaseTrackCalls.Clear();
        }
    }

    public void RespondToCheck(Func<string, CheckFlagWithEntitlementResponse> responder)
    {
        lock (_lock) _checkResponder = responder;
    }

    public Task<CheckFlagWithEntitlementResponse> CheckFlagWithEntitlementAsync(
        string flagKey,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        CancellationToken cancellationToken)
    {
        Func<string, CheckFlagWithEntitlementResponse>? responder;
        lock (_lock)
        {
            CheckCalls.Add(new CheckCall(flagKey, new(company), new(user)));
            responder = _checkResponder;
        }

        if (responder is null)
            throw new InvalidOperationException(
                $"FakeGateClient has no canned response for flag '{flagKey}'. Call RespondToCheck() in your test setup.");

        return Task.FromResult(responder(flagKey));
    }

    public void Track(
        string eventName,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        Dictionary<string, object?> traits,
        int? quantity,
        TrackOptions? options = null)
    {
        lock (_lock)
        {
            if (ThrowOnTrack)
                throw new InvalidOperationException("FakeGateClient.ThrowOnTrack is enabled.");
            TrackCalls.Add(new TrackCall(eventName, new(company), new(user), new(traits), quantity, options));
        }
    }

    public void Identify(
        Dictionary<string, string> keys,
        EventBodyIdentifyCompany? company,
        string? name,
        Dictionary<string, object?>? traits,
        IdentifyOptions? options = null)
    {
        lock (_lock)
        {
            if (ThrowOnIdentify)
                throw new InvalidOperationException("FakeGateClient.ThrowOnIdentify is enabled.");
            IdentifyCalls.Add(new IdentifyCall(new(keys), company, name, traits is null ? null : new(traits), options));
        }
    }

    public Task<SchematicCreditLease?> AcquireCreditLeaseAsync(
        string companyId,
        string creditTypeId,
        double requestedAmount,
        DateTime? expiresAt,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            LeaseCalls.Add(new LeaseCall(companyId, creditTypeId, requestedAmount, expiresAt));
            if (ThrowOnAcquireLease is not null)
                throw ThrowOnAcquireLease;
            if (RejectLease)
                return Task.FromResult<SchematicCreditLease?>(null);

            return Task.FromResult<SchematicCreditLease?>(new($"lease_{++_leaseCounter}", requestedAmount));
        }
    }

    public Task ExtendCreditLeaseAsync(string leaseId, double additionalAmount, CancellationToken cancellationToken)
    {
        lock (_lock) ExtendLeaseCalls.Add(new ExtendLeaseCall(leaseId, additionalAmount));
        return Task.CompletedTask;
    }

    public Task ReleaseCreditLeaseAsync(string leaseId, CancellationToken cancellationToken)
    {
        lock (_lock) ReleasedLeases.Add(leaseId);
        return Task.CompletedTask;
    }

    public Task TrackAgainstLeaseAsync(
        string leaseId,
        string eventName,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        Dictionary<string, object?> traits,
        long quantity,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (ThrowOnLeaseTrack)
                throw new InvalidOperationException("FakeGateClient.ThrowOnLeaseTrack is enabled.");
            LeaseTrackCalls.Add(new LeaseTrackCall(leaseId, eventName, new(company), new(user), new(traits), quantity));
        }

        return Task.CompletedTask;
    }
}
