using System.Text.Json;

namespace AdoHookReceiver.Hooks;

/// <summary>The ADO envelope fields the receiver needs. Nothing inside <c>resource</c> is read.</summary>
public sealed record Envelope(
    string EventId,
    string EventType,
    string? ResourceVersion,
    string? PublisherId,
    string? SubscriptionId,
    long? NotificationId,
    DateTimeOffset? CreatedDate,
    string? AccountId,
    string? OrgUrl,
    string? ProjectId,
    JsonElement Raw)
{
    /// <summary>
    /// Parses the body and checks it is a JSON object with a string <c>id</c>, a string
    /// <c>eventType</c> and an object <c>resource</c>. Optional fields of the wrong type read as null.
    /// </summary>
    public static bool TryParse(ReadOnlyMemory<byte> body, out Envelope? envelope, out string? error)
    {
        envelope = null;

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            error = "Body is not valid JSON.";
            return false;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "Body is not a JSON object.";
            return false;
        }

        var eventId = GetString(root, "id");
        if (string.IsNullOrEmpty(eventId))
        {
            error = "Body has no 'id'.";
            return false;
        }

        var eventType = GetString(root, "eventType");
        if (string.IsNullOrEmpty(eventType))
        {
            error = "Body has no 'eventType'.";
            return false;
        }

        if (!root.TryGetProperty("resource", out var resource) || resource.ValueKind != JsonValueKind.Object)
        {
            error = "Body has no 'resource' object.";
            return false;
        }

        var containers = GetObject(root, "resourceContainers");
        var account = GetObject(containers, "account");
        var project = GetObject(containers, "project");

        envelope = new Envelope(
            eventId,
            eventType,
            GetString(root, "resourceVersion"),
            GetString(root, "publisherId"),
            GetString(root, "subscriptionId"),
            GetInt64(root, "notificationId"),
            GetDateTimeOffset(root, "createdDate"),
            GetString(account, "id"),
            GetString(account, "baseUrl"),
            GetString(project, "id"),
            root);
        error = null;
        return true;
    }

    private static JsonElement? GetProperty(JsonElement? element, string name, JsonValueKind kind) =>
        element is { } e && e.TryGetProperty(name, out var value) && value.ValueKind == kind ? value : null;

    private static JsonElement? GetObject(JsonElement? element, string name) =>
        GetProperty(element, name, JsonValueKind.Object);

    private static string? GetString(JsonElement? element, string name) =>
        GetProperty(element, name, JsonValueKind.String)?.GetString();

    private static long? GetInt64(JsonElement? element, string name) =>
        GetProperty(element, name, JsonValueKind.Number) is { } value && value.TryGetInt64(out var result) ? result : null;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement? element, string name) =>
        GetProperty(element, name, JsonValueKind.String) is { } value && value.TryGetDateTimeOffset(out var result) ? result : null;
}
