using AdoHookReceiver.Hooks;
using Microsoft.Extensions.Logging;

namespace AdoHookReceiver.Tests;

public sealed class LogHookSinkTests
{
    [Fact]
    public async Task Writes_one_ado_hook_received_entry_with_every_field_and_the_raw_payload()
    {
        Assert.True(Envelope.TryParse(Fixtures.Load("build.complete"), out var envelope, out _));
        var hook = new ReceivedHook(
            "receipt-1", DateTimeOffset.UnixEpoch, "ado", envelope!.EventId, envelope.EventType, envelope.ResourceVersion,
            envelope.PublisherId, envelope.SubscriptionId, envelope.NotificationId, envelope.CreatedDate, envelope.OrgUrl,
            "00-trace-span-00", envelope.Raw);
        var logger = new CapturingLogger();

        await new LogHookSink(logger).HandleAsync(hook, TestContext.Current.CancellationToken);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(1, entry.EventId.Id);
        Assert.Equal("ado.hook.received", entry.EventId.Name);
        Assert.StartsWith("ado.hook.received: build.complete", entry.Message, StringComparison.Ordinal);

        var state = entry.State.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal(
            ["ReceiptId", "ReceivedAt", "Source", "EventId", "EventType", "ResourceVersion", "PublisherId", "SubscriptionId",
             "NotificationId", "CreatedDate", "OrgUrl", "TraceParent", "Raw"],
            state.Keys);
        Assert.Equal("1970-01-01T00:00:00.0000000+00:00", state["ReceivedAt"]);
        Assert.Equal(Fixtures.BuildSubscriptionId, state["SubscriptionId"]);
        Assert.Equal(Fixtures.Text("build.complete"), state["Raw"]);
    }

    private sealed record Entry(LogLevel Level, EventId EventId, string Message, IReadOnlyList<KeyValuePair<string, object?>> State);

    private sealed class CapturingLogger : ILogger<LogHookSink>
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(new Entry(logLevel, eventId, formatter(state, exception), (IReadOnlyList<KeyValuePair<string, object?>>)state!));
    }
}
