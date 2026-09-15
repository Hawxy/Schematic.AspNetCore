using SchematicHQ.Client;
using SchematicHQ.Client.RulesEngine;
using SchematicClient = SchematicHQ.Client.Schematic;

namespace SchematicHQ.Community.DependencyInjection;

internal sealed class SchematicGateClient : ISchematicGateClient
{
    private readonly SchematicClient _client;

    public SchematicGateClient(SchematicClient client)
    {
        _client = client;
    }

    // The SDK method does not accept a CancellationToken; WaitAsync at least stops awaiting
    // (e.g. when the request is aborted) even though the underlying call runs to completion.
    public Task<CheckFlagWithEntitlementResponse> CheckFlagWithEntitlementAsync(
        string flagKey,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        CancellationToken cancellationToken)
        => _client.CheckFlagWithEntitlement(flagKey, company, user).WaitAsync(cancellationToken);

    public void Track(
        string eventName,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        Dictionary<string, object?> traits,
        int? quantity,
        TrackOptions? options = null)
        => _client.Track(eventName, company, user, traits, quantity, options);

    public void Identify(
        Dictionary<string, string> keys,
        EventBodyIdentifyCompany? company,
        string? name,
        Dictionary<string, object?>? traits,
        IdentifyOptions? options = null)
        => _client.Identify(keys, company, name, traits, options);

    public async Task<SchematicCreditLease> AcquireCreditLeaseAsync(
        string companyId,
        string creditTypeId,
        double requestedAmount,
        DateTime? expiresAt,
        CancellationToken cancellationToken)
    {
        var response = await _client.Credits.AcquireCreditLeaseAsync(
            new AcquireCreditLeaseRequestBody
            {
                CompanyId = companyId,
                CreditTypeId = creditTypeId,
                RequestedAmount = requestedAmount,
                ExpiresAt = expiresAt,
            },
            cancellationToken: cancellationToken);

        return ToLease(response.Data);
    }

    public async Task<SchematicCreditLease> ExtendCreditLeaseAsync(
        string leaseId,
        double additionalAmount,
        CancellationToken cancellationToken)
    {
        var response = await _client.Credits.ExtendCreditLeaseAsync(
            leaseId,
            new ExtendCreditLeaseRequestBody { AdditionalAmount = additionalAmount },
            cancellationToken: cancellationToken);

        return ToLease(response.Data);
    }

    public async Task ReleaseCreditLeaseAsync(string leaseId, CancellationToken cancellationToken)
        => await _client.Credits.ReleaseCreditLeaseAsync(leaseId, cancellationToken: cancellationToken);

    public async Task TrackAgainstLeaseAsync(
        string leaseId,
        string eventName,
        Dictionary<string, string> company,
        Dictionary<string, string> user,
        Dictionary<string, object?> traits,
        long quantity,
        CancellationToken cancellationToken)
    {
        await _client.Events.CreateEventAsync(
            new CreateEventRequestBody
            {
                EventType = EventType.Track,
                Body = new EventBodyTrack
                {
                    Event = eventName,
                    Company = company,
                    User = user,
                    Traits = traits,
                    Quantity = quantity,
                    LeaseId = leaseId,
                },
            },
            cancellationToken: cancellationToken);
    }

    private static SchematicCreditLease ToLease(CreditLeaseResponseData data)
        => new(data.Id, data.CompanyId, data.CreditTypeId, data.GrantedAmount, data.TrackedAmount, data.ExpiresAt);
}
