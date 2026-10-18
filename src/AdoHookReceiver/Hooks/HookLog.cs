namespace AdoHookReceiver.Hooks;

/// <summary>
/// Log entries for the hook endpoint. Each message starts with its event name, because the JSON
/// console formatter writes only the numeric event id. None carries the Authorization header or,
/// for rejections, the body.
/// </summary>
internal static partial class HookLog
{
    public static readonly EventId Received = new(1, "ado.hook.received");

    [LoggerMessage(EventId = 2, EventName = "ado.hook.unauthorized", Level = LogLevel.Warning,
        Message = "ado.hook.unauthorized: no valid credentials from {ClientIp} to {Path}")]
    public static partial void Unauthorized(ILogger logger, string? clientIp, string path);

    [LoggerMessage(EventId = 3, EventName = "ado.hook.rejected", Level = LogLevel.Warning,
        Message = "ado.hook.rejected: {Reason}")]
    public static partial void Rejected(ILogger logger, string reason);

    [LoggerMessage(EventId = 4, EventName = "ado.hook.forbidden", Level = LogLevel.Warning,
        Message = "ado.hook.forbidden: {EventType} refused by {RefusedBy} allow-list; org {OrgId} {OrgUrl}, project {ProjectId}, subscription {SubscriptionId}")]
    public static partial void Forbidden(ILogger logger, string eventType, string refusedBy, string? orgId, string? orgUrl, string? projectId, string? subscriptionId);

    [LoggerMessage(EventId = 5, EventName = "ado.hook.sink_failed", Level = LogLevel.Error,
        Message = "ado.hook.sink_failed: {EventType} {EventId} (receipt {ReceiptId}); returning 503 so ADO retries")]
    public static partial void SinkFailed(ILogger logger, Exception exception, string eventType, string eventId, string receiptId);
}
