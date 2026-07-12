using Schematic.AspNetCore.Internal;
using SchematicHQ.Client.RulesEngine;

namespace Schematic.AspNetCore.Tests.Infrastructure;

internal sealed record TrackCall(
    string EventName,
    Dictionary<string, string> Company,
    Dictionary<string, string> User,
    Dictionary<string, object?> Traits,
    int? Quantity);

internal sealed record CheckCall(
    string FlagKey,
    Dictionary<string, string> Company,
    Dictionary<string, string> User);

internal sealed class FakeGateClient : ISchematicGateClient
{
    private readonly object _lock = new();
    private Func<string, CheckFlagWithEntitlementResponse>? _checkResponder;

    public List<CheckCall> CheckCalls { get; } = new();
    public List<TrackCall> TrackCalls { get; } = new();

    public void Reset()
    {
        lock (_lock)
        {
            _checkResponder = null;
            CheckCalls.Clear();
            TrackCalls.Clear();
        }
    }

    public void RespondToCheck(Func<string, CheckFlagWithEntitlementResponse> responder)
    {
        lock (_lock) _checkResponder = responder;
    }

    public Task<CheckFlagWithEntitlementResponse> CheckFlagWithEntitlementAsync(
        string flagKey,
        Dictionary<string, string> company,
        Dictionary<string, string> user)
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
        int? quantity)
    {
        lock (_lock)
        {
            TrackCalls.Add(new TrackCall(eventName, new(company), new(user), new(traits), quantity));
        }
    }
}
