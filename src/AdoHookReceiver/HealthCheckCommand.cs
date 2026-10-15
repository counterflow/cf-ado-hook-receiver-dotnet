namespace AdoHookReceiver;

/// <summary>
/// <c>--healthcheck</c>: calls the running app's <c>/health</c> and exits 0 or 1. Used by the
/// Docker HEALTHCHECK, because the aspnet image ships no curl. Needs no app configuration.
/// </summary>
public static class HealthCheckCommand
{
    public const string Argument = "--healthcheck";

    public static async Task<int> RunAsync()
    {
        var ports = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");
        var port = ports?.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "8080";

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync($"http://localhost:{port}/health");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
