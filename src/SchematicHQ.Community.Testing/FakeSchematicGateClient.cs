using SchematicHQ.Client;
using SchematicHQ.Client.RulesEngine;
using SchematicHQ.Community.DependencyInjection;

namespace SchematicHQ.Community.Testing;

/// <summary>
/// <see cref="ISchematicGateClient"/> for tests. Every check is allowed until a test says otherwise —
/// <see cref="Deny"/> for a company-wide denial, <see cref="DenyForUser"/> to deny only when the check is made
/// as a given user (so a scenario can deny its acting user while the administrator that seeds its data stays
/// entitled), <see cref="RespondToCheck"/> to script the full response, <see cref="ThrowOnCheck"/> to stand in
/// for an unreachable Schematic. Every call is recorded. Track and Identify are recorded and discarded; credit
/// leases are granted in full unless <see cref="RejectLease"/> or <see cref="ThrowOnAcquireLease"/> is set.
/// Safe to share across parallel tests within one host: all state is guarded by one lock.
/// </summary>
public sealed class FakeSchematicGateClient : ISchematicGateClient
{
    public sealed record CheckCall(string FlagKey, Dictionary<string, string> Company, Dictionary<string, string> User);

    public sealed record TrackCall(
        string EventName,
        Dictionary<string, string> Company,
        Dictionary<string, string> User,
        Dictionary<string, object?> Traits,
        int? Quantity,
        TrackOptions? Options);

    public sealed record IdentifyCall(
        Dictionary<string, string> Keys,
        EventBodyIdentifyCompany? Company,
        string? Name,
        Dictionary<string, object?>? Traits,
        IdentifyOptions? Options);

    public sealed record LeaseCall(string CompanyId, string CreditTypeId, double RequestedAmount, DateTime? ExpiresAt);

    public sealed record ExtendLeaseCall(string LeaseId, double AdditionalAmount);

    public sealed record LeaseTrackCall(
        string LeaseId,
        string EventName,
        Dictionary<string, string> Company,
        Dictionary<string, string> User,
        Dictionary<string, object?> Traits,
        long Quantity);

    private const string UserIdKey = "id";

    private readonly object _lock = new();
    private readonly HashSet<string> _denied = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _deniedByUser = new(StringComparer.Ordinal);
    private readonly List<CheckCall> _checkCalls = [];
    private readonly List<TrackCall> _trackCalls = [];
    private readonly List<IdentifyCall> _identifyCalls = [];
    private readonly List<LeaseCall> _leaseCalls = [];
    private readonly List<ExtendLeaseCall> _extendLeaseCalls = [];
    private readonly List<string> _releasedLeases = [];
    private readonly List<LeaseTrackCall> _leaseTrackCalls = [];
    private Func<string, CheckFlagWithEntitlementResponse>? _checkResponder;
    private int _leaseCounter;

    /// <summary>Every check made, in order.</summary>
    public IReadOnlyList<CheckCall> CheckCalls => Snapshot(_checkCalls);

    /// <summary>Every buffered Track sent, in order.</summary>
    public IReadOnlyList<TrackCall> TrackCalls => Snapshot(_trackCalls);

    /// <summary>Every Identify sent, in order.</summary>
    public IReadOnlyList<IdentifyCall> IdentifyCalls => Snapshot(_identifyCalls);

    /// <summary>Every lease requested, in order.</summary>
    public IReadOnlyList<LeaseCall> LeaseCalls => Snapshot(_leaseCalls);

    /// <summary>Every lease extension requested, in order.</summary>
    public IReadOnlyList<ExtendLeaseCall> ExtendLeaseCalls => Snapshot(_extendLeaseCalls);

    /// <summary>Ids of the leases released, in order.</summary>
    public IReadOnlyList<string> ReleasedLeases => Snapshot(_releasedLeases);

    /// <summary>Every Track sent against a lease, in order.</summary>
    public IReadOnlyList<LeaseTrackCall> LeaseTrackCalls => Snapshot(_leaseTrackCalls);

    /// <summary>When set, every check throws it after being recorded — an unreachable Schematic.</summary>
    public Exception? ThrowOnCheck { get; set; }

    /// <summary>When set, every buffered Track throws it.</summary>
    public Exception? ThrowOnTrack { get; set; }

    /// <summary>When set, every Identify throws it.</summary>
    public Exception? ThrowOnIdentify { get; set; }

    /// <summary>When set, every Track against a lease throws it.</summary>
    public Exception? ThrowOnLeaseTrack { get; set; }

    /// <summary>When set, every lease request throws it after being recorded.</summary>
    public Exception? ThrowOnAcquireLease { get; set; }

    /// <summary>Answer every lease request with "cannot fund" (a <c>null</c> lease).</summary>
    public bool RejectLease { get; set; }

    /// <summary>Denies <paramref name="flagKeys"/> for every identity.</summary>
    public FakeSchematicGateClient Deny(params string[] flagKeys)
    {
        lock (_lock)
            _denied.UnionWith(flagKeys);
        return this;
    }

    /// <summary>
    /// Denies <paramref name="flagKeys"/> only when the check carries <paramref name="userId"/> as the user's
    /// <c>id</c> key — the same per-user targeting a Schematic rule can express.
    /// </summary>
    public FakeSchematicGateClient DenyForUser(string userId, params string[] flagKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        lock (_lock)
        {
            if (!_deniedByUser.TryGetValue(userId, out var denied))
                _deniedByUser[userId] = denied = new HashSet<string>(StringComparer.Ordinal);
            denied.UnionWith(flagKeys);
        }

        return this;
    }

    /// <summary>
    /// Scripts the full response per flag — to hand back an entitlement with a credit balance, or a specific
    /// reason. Takes precedence over <see cref="Deny"/> and <see cref="DenyForUser"/> while set; the responder
    /// may itself throw to fail the check.
    /// </summary>
    public FakeSchematicGateClient RespondToCheck(Func<string, CheckFlagWithEntitlementResponse>? responder)
    {
        lock (_lock)
            _checkResponder = responder;
        return this;
    }

    /// <summary>Clears every denial, scripted response, failure switch and recorded call.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _denied.Clear();
            _deniedByUser.Clear();
            _checkResponder = null;
            _leaseCounter = 0;
            ThrowOnCheck = null;
            ThrowOnTrack = null;
            ThrowOnIdentify = null;
            ThrowOnLeaseTrack = null;
            ThrowOnAcquireLease = null;
            RejectLease = false;
            _checkCalls.Clear();
            _trackCalls.Clear();
            _identifyCalls.Clear();
            _leaseCalls.Clear();
            _extendLeaseCalls.Clear();
            _releasedLeases.Clear();
            _leaseTrackCalls.Clear();
        }
    }

    public Task<CheckFlagWithEntitlementResponse> CheckFlagWithEntitlementAsync(
        string flagKey,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        CancellationToken cancellationToken)
    {
        Func<string, CheckFlagWithEntitlementResponse>? responder;
        bool denied;
        lock (_lock)
        {
            _checkCalls.Add(new CheckCall(flagKey, new(company), new(user)));
            if (ThrowOnCheck is { } failure)
                throw failure;

            responder = _checkResponder;
            denied = _denied.Contains(flagKey)
                || (user.TryGetValue(UserIdKey, out var userId)
                    && _deniedByUser.TryGetValue(userId, out var deniedForUser)
                    && deniedForUser.Contains(flagKey));
        }

        if (responder is not null)
            return Task.FromResult(responder(flagKey));

        return Task.FromResult(new CheckFlagWithEntitlementResponse
        {
            FlagKey = flagKey,
            Value = !denied,
            Reason = denied ? "denied by FakeSchematicGateClient" : "allowed by FakeSchematicGateClient",
        });
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
            if (ThrowOnTrack is { } failure)
                throw failure;
            _trackCalls.Add(new TrackCall(eventName, new(company), new(user), new(traits), quantity, options));
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
            if (ThrowOnIdentify is { } failure)
                throw failure;
            _identifyCalls.Add(new IdentifyCall(new(keys), company, name, traits is null ? null : new(traits), options));
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
            _leaseCalls.Add(new LeaseCall(companyId, creditTypeId, requestedAmount, expiresAt));
            if (ThrowOnAcquireLease is { } failure)
                throw failure;
            if (RejectLease)
                return Task.FromResult<SchematicCreditLease?>(null);

            return Task.FromResult<SchematicCreditLease?>(new($"lease_{++_leaseCounter}", requestedAmount));
        }
    }

    public Task ExtendCreditLeaseAsync(string leaseId, double additionalAmount, CancellationToken cancellationToken)
    {
        lock (_lock)
            _extendLeaseCalls.Add(new ExtendLeaseCall(leaseId, additionalAmount));
        return Task.CompletedTask;
    }

    public Task ReleaseCreditLeaseAsync(string leaseId, CancellationToken cancellationToken)
    {
        lock (_lock)
            _releasedLeases.Add(leaseId);
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
            if (ThrowOnLeaseTrack is { } failure)
                throw failure;
            _leaseTrackCalls.Add(new LeaseTrackCall(leaseId, eventName, new(company), new(user), new(traits), quantity));
        }

        return Task.CompletedTask;
    }

    private T[] Snapshot<T>(List<T> list)
    {
        lock (_lock)
            return list.ToArray();
    }
}
