using SchematicHQ.Client;
using SchematicHQ.Client.RulesEngine;
using SchematicClient = SchematicHQ.Client.Schematic;

namespace SchematicHQ.Community.DependencyInjection;

internal sealed class SchematicGateClient : ISchematicGateClient
{
    private readonly SchematicClient _client;
    private readonly SchematicInferenceEventBuffer _inferenceBuffer;

    public SchematicGateClient(SchematicClient client, SchematicInferenceEventBuffer inferenceBuffer)
    {
        _client = client;
        _inferenceBuffer = inferenceBuffer;
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

    public async Task<SchematicCreditLease?> AcquireCreditLeaseAsync(
        string companyId,
        string creditTypeId,
        double requestedAmount,
        DateTime? expiresAt,
        CancellationToken cancellationToken)
    {
        AcquireCreditLeaseResponse response;
        try
        {
            response = await _client.Credits.AcquireCreditLeaseAsync(
                new AcquireCreditLeaseRequestBody
                {
                    CompanyId = companyId,
                    CreditTypeId = creditTypeId,
                    RequestedAmount = requestedAmount,
                    ExpiresAt = expiresAt,
                },
                cancellationToken: cancellationToken);
        }
        // A hold the API cannot fund comes back as a payment/conflict/unprocessable status.
        catch (SchematicApiException ex) when (ex.StatusCode is 402 or 409 or 422)
        {
            return null;
        }

        var lease = response.Data;
        if (lease.GrantedAmount > 0)
            return new SchematicCreditLease(lease.Id, lease.GrantedAmount);

        await _client.Credits.ReleaseCreditLeaseAsync(lease.Id, cancellationToken: cancellationToken);
        return null;
    }

    public async Task ExtendCreditLeaseAsync(string leaseId, double additionalAmount, CancellationToken cancellationToken)
        => await _client.Credits.ExtendCreditLeaseAsync(
            leaseId,
            new ExtendCreditLeaseRequestBody { AdditionalAmount = additionalAmount },
            cancellationToken: cancellationToken);

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

    public void TrackInference(EventBodyInference body, TrackOptions? options = null)
        => _inferenceBuffer.Enqueue(CreateInferenceRequest(body, options?.IdempotencyKey, options));

    public async Task TrackInferenceAgainstLeaseAsync(
        string leaseId,
        EventBodyInference body,
        string? idempotencyKey,
        CancellationToken cancellationToken)
        => await _client.Events.CreateEventAsync(
            CreateInferenceRequest(body with { LeaseId = leaseId }, idempotencyKey, options: null),
            cancellationToken: cancellationToken);

    private static CreateEventRequestBody CreateInferenceRequest(EventBodyInference body, string? idempotencyKey, TrackOptions? options)
        => new()
        {
            EventType = EventType.Inference,
            Body = body,
            IdempotencyKey = idempotencyKey,
            SentAt = options?.SentAt ?? DateTime.UtcNow,
            TrustedClientClock = options?.TrustedClientClock,
            Backfill = options?.Backfill,
        };
}
