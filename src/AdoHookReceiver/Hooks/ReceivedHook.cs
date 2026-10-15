using System.Text.Json;

namespace AdoHookReceiver.Hooks;

/// <summary>One accepted service hook: envelope fields lifted out, plus the raw body untouched.</summary>
public sealed record ReceivedHook(
    string ReceiptId,              // Guid.CreateVersion7()
    DateTimeOffset ReceivedAt,
    string Source,                 // "ado"
    string EventId,                // payload.id (stable across ADO retries)
    string EventType,              // payload.eventType, e.g. git.pullrequest.created
    string? ResourceVersion,       // payload.resourceVersion
    string? PublisherId,           // payload.publisherId
    string? SubscriptionId,        // payload.subscriptionId
    long? NotificationId,          // payload.notificationId
    DateTimeOffset? CreatedDate,   // payload.createdDate
    string? OrgUrl,                // payload.resourceContainers.account.baseUrl
    string? TraceParent,           // Activity.Current?.Id
    JsonElement Raw);              // clone of the parsed body; stays valid after the request ends
