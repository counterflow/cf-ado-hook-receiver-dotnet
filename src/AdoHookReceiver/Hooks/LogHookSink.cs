namespace AdoHookReceiver.Hooks;

/// <summary>
/// Default sink: writes one <c>ado.hook.received</c> entry with every <see cref="ReceivedHook"/>
/// field, including the raw payload, which holds ADO users' names and email addresses.
/// </summary>
public sealed class LogHookSink(ILogger<LogHookSink> logger) : IHookSink
{
    public Task HandleAsync(ReceivedHook hook, CancellationToken cancellationToken)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return Task.CompletedTask;
        }

        // Custom state rather than a message template, so the large raw payload appears once as
        // its own field instead of also being formatted into the message text.
        var state = new ReceivedState($"{HookLog.Received.Name}: {hook.EventType} {hook.EventId} (receipt {hook.ReceiptId})")
        {
            new(nameof(hook.ReceiptId), hook.ReceiptId),
            new(nameof(hook.ReceivedAt), hook.ReceivedAt.ToString("O")),
            new(nameof(hook.Source), hook.Source),
            new(nameof(hook.EventId), hook.EventId),
            new(nameof(hook.EventType), hook.EventType),
            new(nameof(hook.ResourceVersion), hook.ResourceVersion),
            new(nameof(hook.PublisherId), hook.PublisherId),
            new(nameof(hook.SubscriptionId), hook.SubscriptionId),
            new(nameof(hook.NotificationId), hook.NotificationId),
            new(nameof(hook.CreatedDate), hook.CreatedDate?.ToString("O")),
            new(nameof(hook.OrgUrl), hook.OrgUrl),
            new(nameof(hook.TraceParent), hook.TraceParent),
            new(nameof(hook.Raw), hook.Raw.GetRawText()),
        };

        logger.Log(LogLevel.Information, HookLog.Received, state, exception: null, (s, _) => s.ToString());

        return Task.CompletedTask;
    }

    /// <summary>Structured log state whose text form is the log message.</summary>
    private sealed class ReceivedState(string message) : List<KeyValuePair<string, object?>>
    {
        public override string ToString() => message;
    }
}
