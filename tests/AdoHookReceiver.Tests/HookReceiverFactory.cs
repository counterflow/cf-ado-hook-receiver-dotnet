using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using AdoHookReceiver.Hooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdoHookReceiver.Tests;

/// <summary>
/// The receiver with test configuration and a recording sink. Runs in the "Testing" environment,
/// so neither appsettings.Development.json nor a developer's user secrets are loaded.
/// </summary>
public sealed class HookReceiverFactory(
    IDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? services = null) : WebApplicationFactory<Program>
{
    public const string Username = "ado";
    public const string Password = "pass:word";

    public RecordingSink Sink { get; } = new();

    public static AuthenticationHeaderValue BasicAuth(string username = Username, string password = Password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));

    public static Dictionary<string, string?> DefaultSettings() => new()
    {
        ["AdoHook:Username"] = Username,
        ["AdoHook:Password"] = Password,
        ["AdoHook:Allow:EventTypes:0"] = "git.pullrequest.created",
        ["AdoHook:Allow:EventTypes:1"] = "git.push",
        ["AdoHook:Allow:EventTypes:2"] = "build.complete",
        ["AdoHook:Allow:Organizations:0"] = Fixtures.AccountId,
        ["AdoHook:Allow:ProjectIds:0"] = Fixtures.ProjectId,
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings ?? DefaultSettings()));
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IHookSink>(Sink);
            services?.Invoke(s);
        });
    }
}

public sealed class RecordingSink : IHookSink
{
    public ConcurrentQueue<ReceivedHook> Hooks { get; } = new();

    public Task HandleAsync(ReceivedHook hook, CancellationToken cancellationToken)
    {
        Hooks.Enqueue(hook);
        return Task.CompletedTask;
    }
}

public sealed class ThrowingSink : IHookSink
{
    public Task HandleAsync(ReceivedHook hook, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Sink is down.");
}

/// <summary>Fails outside the sink, to reach the unhandled-exception path.</summary>
public sealed class ThrowingTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Clock is broken.");
}
