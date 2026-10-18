using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AdoHookReceiver.Hooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AdoHookReceiver.Tests;

/// <summary>One test per row of the spec's responses table, plus health and startup.</summary>
public sealed class HooksEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpResponseMessage> PostAsync(
        HookReceiverFactory factory, byte[] body, AuthenticationHeaderValue? auth = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/ado/hooks")
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new("application/json") } },
        };
        request.Headers.Authorization = auth;
        return await factory.CreateClient().SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> PostAuthorizedAsync(HookReceiverFactory factory, byte[] body) =>
        PostAsync(factory, body, HookReceiverFactory.BasicAuth());

    private static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);

    [Theory]
    [MemberData(nameof(Fixtures.EventTypes), MemberType = typeof(Fixtures))]
    public async Task Accepts_an_allowed_hook_and_hands_it_to_the_sink(string eventType)
    {
        await using var factory = new HookReceiverFactory();

        using var response = await PostAuthorizedAsync(factory, Fixtures.Load(eventType));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<Receipt>(Ct);
        var hook = Assert.Single(factory.Sink.Hooks);
        Assert.Equal(hook.ReceiptId, receipt!.ReceiptId);
        Assert.Equal(7, Guid.Parse(hook.ReceiptId).Version);
        Assert.Equal("ado", hook.Source);
        Assert.Equal(eventType, hook.EventType);
        Assert.Equal("a0a0a0a0-bbbb-cccc-dddd-e1e1e1e1e1e1", hook.EventId);
        Assert.NotNull(hook.TraceParent);
        Assert.Equal(Fixtures.Text(eventType), hook.Raw.GetRawText());
    }

    [Fact]
    public async Task Raw_stays_usable_after_the_request_ends()
    {
        await using var factory = new HookReceiverFactory();

        using var response = await PostAuthorizedAsync(factory, Fixtures.Load("git.push"));

        var hook = Assert.Single(factory.Sink.Hooks);
        Assert.Equal("git.push", hook.Raw.GetProperty("eventType").GetString());
    }

    public static TheoryData<string, string?> BadCredentials => new()
    {
        { "no header", null },
        { "wrong password", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("ado:wrong")) },
        { "wrong username", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("someone:pass:word")) },
        { "password prefix", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("ado:pass")) },
        { "no colon", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("ado")) },
        { "not base64", "Basic !!!" },
        { "bearer scheme", "Bearer abc" },
    };

    [Theory]
    [MemberData(nameof(BadCredentials))]
    public async Task Refuses_missing_or_wrong_credentials_with_401(string credentialCase, string? header)
    {
        await using var factory = new HookReceiverFactory();

        using var response = await PostAsync(
            factory, Fixtures.Load("git.push"), header is null ? null : AuthenticationHeaderValue.Parse(header));

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, credentialCase);
        Assert.Equal("Basic realm=\"ado-hooks\"", response.Headers.WwwAuthenticate.ToString());
        Assert.Empty(factory.Sink.Hooks);
    }

    [Fact]
    public async Task Accepts_a_password_containing_a_colon()
    {
        await using var factory = new HookReceiverFactory();

        using var response = await PostAsync(factory, Fixtures.Load("git.push"), HookReceiverFactory.BasicAuth("ado", "pass:word"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"id":"e1","eventType":"git.push"}""")]
    [InlineData("""{"eventType":"git.push","resource":{}}""")]
    public async Task Rejects_a_body_that_is_not_a_service_hook_with_400(string body)
    {
        await using var factory = new HookReceiverFactory();

        using var response = await PostAuthorizedAsync(factory, Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(factory.Sink.Hooks);
    }

    [Fact]
    public async Task Refuses_an_event_type_not_on_the_allow_list_with_403()
    {
        var settings = HookReceiverFactory.DefaultSettings();
        settings.Remove("AdoHook:Allow:EventTypes:2");
        await using var factory = new HookReceiverFactory(settings);

        using var response = await PostAuthorizedAsync(factory, Fixtures.Load("build.complete"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Sink.Hooks);
    }

    [Fact]
    public async Task Refuses_a_project_not_on_the_allow_list_with_403()
    {
        var settings = HookReceiverFactory.DefaultSettings();
        settings["AdoHook:Allow:ProjectIds:0"] = Guid.NewGuid().ToString();
        await using var factory = new HookReceiverFactory(settings);

        using var response = await PostAuthorizedAsync(factory, Fixtures.Load("git.push"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Sink.Hooks);
    }

    [Fact]
    public async Task Refuses_a_body_over_the_size_limit_with_413()
    {
        var settings = HookReceiverFactory.DefaultSettings();
        settings["AdoHook:BodyLimitBytes"] = "1024";
        await using var factory = new HookReceiverFactory(settings);

        using var response = await PostAuthorizedAsync(factory, Fixtures.Load("git.push"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(factory.Sink.Hooks);
    }

    [Fact]
    public async Task Returns_503_when_the_sink_throws_so_ADO_retries()
    {
        await using var factory = new HookReceiverFactory(services: s => s.AddSingleton<IHookSink, ThrowingSink>());

        using var response = await PostAuthorizedAsync(factory, Fixtures.Load("git.push"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Returns_500_problem_details_on_an_unhandled_exception()
    {
        await using var factory = new HookReceiverFactory(services: s => s.AddSingleton<TimeProvider, ThrowingTimeProvider>());

        using var response = await PostAuthorizedAsync(factory, Fixtures.Load("git.push"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("Clock is broken", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Health_returns_200_without_credentials()
    {
        await using var factory = new HookReceiverFactory();

        using var response = await factory.CreateClient().GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("AdoHook:Allow:EventTypes:0", "EventTypes")]
    [InlineData("AdoHook:Allow:Organizations:0", "Organizations")]
    [InlineData("AdoHook:Allow:ProjectIds:0", "ProjectIds")]
    public void Refuses_to_start_when_a_required_allow_list_is_empty(string settingToRemove, string list)
    {
        var settings = HookReceiverFactory.DefaultSettings();
        foreach (var key in settings.Keys.Where(k => k.StartsWith(settingToRemove[..^1], StringComparison.Ordinal)).ToList())
        {
            settings.Remove(key);
        }

        using var factory = new HookReceiverFactory(settings);

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains($"AdoHook:Allow:{list} needs at least one entry", exception.Message, StringComparison.Ordinal);
    }

    private sealed record Receipt(string ReceiptId);
}
