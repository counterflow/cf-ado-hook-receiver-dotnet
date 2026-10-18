using System.Text;
using System.Text.Json;
using AdoHookReceiver.Hooks;

namespace AdoHookReceiver.Tests;

public sealed class EnvelopeTests
{
    [Theory]
    [MemberData(nameof(Fixtures.EventTypes), MemberType = typeof(Fixtures))]
    public void Accepts_every_fixture_with_raw_unchanged(string eventType)
    {
        var body = Fixtures.Load(eventType);

        Assert.True(Envelope.TryParse(body, out var envelope, out var error), error);

        using var input = JsonDocument.Parse(body);
        Assert.True(JsonElement.DeepEquals(input.RootElement, envelope!.Raw));
        Assert.Equal(Fixtures.Text(eventType), envelope.Raw.GetRawText());
    }

    [Theory]
    [InlineData("git.pullrequest.created", "tfs", null, null, "2024-09-19T13:03:27.2879096Z", "https://dev.azure.com/fabrikam/")]
    [InlineData("git.push", "tfs", null, null, "2024-09-19T13:03:27.0379153Z", "https://dev.azure.com/fabrikam-fiber-inc/")]
    [InlineData("build.complete", "azure-devops", Fixtures.BuildSubscriptionId, 1L, "2024-12-02T12:21:13.8866607Z", "https://dev.azure.com/FabrikamFiber/")]
    public void Lifts_envelope_fields(
        string eventType, string publisherId, string? subscriptionId, long? notificationId, string createdDate, string orgUrl)
    {
        Assert.True(Envelope.TryParse(Fixtures.Load(eventType), out var envelope, out _));

        Assert.Equal("a0a0a0a0-bbbb-cccc-dddd-e1e1e1e1e1e1", envelope!.EventId);
        Assert.Equal(eventType, envelope.EventType);
        Assert.Equal("2.0", envelope.ResourceVersion);
        Assert.Equal(publisherId, envelope.PublisherId);
        Assert.Equal(subscriptionId, envelope.SubscriptionId);
        Assert.Equal(notificationId, envelope.NotificationId);
        Assert.Equal(DateTimeOffset.Parse(createdDate), envelope.CreatedDate);
        Assert.Equal(Fixtures.AccountId, envelope.AccountId);
        Assert.Equal(orgUrl, envelope.OrgUrl);
        Assert.Equal(Fixtures.ProjectId, envelope.ProjectId);
    }

    [Theory]
    [InlineData("", "Body is not valid JSON.")]
    [InlineData("hello", "Body is not valid JSON.")]
    [InlineData("[1,2]", "Body is not a JSON object.")]
    [InlineData("""{"eventType":"git.push","resource":{}}""", "Body has no 'id'.")]
    [InlineData("""{"id":7,"eventType":"git.push","resource":{}}""", "Body has no 'id'.")]
    [InlineData("""{"id":"e1","resource":{}}""", "Body has no 'eventType'.")]
    [InlineData("""{"id":"e1","eventType":"git.push"}""", "Body has no 'resource' object.")]
    [InlineData("""{"id":"e1","eventType":"git.push","resource":"text"}""", "Body has no 'resource' object.")]
    public void Rejects_bodies_that_are_not_service_hooks(string body, string expectedError)
    {
        Assert.False(Envelope.TryParse(Encoding.UTF8.GetBytes(body), out var envelope, out var error));
        Assert.Null(envelope);
        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void Optional_fields_of_the_wrong_type_read_as_null()
    {
        var body = """{"id":"e1","eventType":"git.push","resource":{},"notificationId":"seven","createdDate":5,"resourceContainers":{"account":"x"}}""";

        Assert.True(Envelope.TryParse(Encoding.UTF8.GetBytes(body), out var envelope, out _));
        Assert.Null(envelope!.NotificationId);
        Assert.Null(envelope.CreatedDate);
        Assert.Null(envelope.AccountId);
        Assert.Null(envelope.OrgUrl);
        Assert.Null(envelope.ProjectId);
    }
}
