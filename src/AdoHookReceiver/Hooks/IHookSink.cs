namespace AdoHookReceiver.Hooks;

/// <summary>
/// The one seam later layers replace. Registered once in DI; a throw becomes a 503 so ADO retries.
/// </summary>
public interface IHookSink
{
    Task HandleAsync(ReceivedHook hook, CancellationToken cancellationToken);
}
