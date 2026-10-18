using System.Text;
using AdoHookReceiver.Hooks;
using AdoHookReceiver.Options;
using Microsoft.Extensions.Options;

namespace AdoHookReceiver.Tests;

public sealed class AllowListTests
{
    private static AllowList Create(
        string[]? eventTypes = null, string[]? organizations = null, string[]? projectIds = null, string[]? subscriptionIds = null) =>
        new(Microsoft.Extensions.Options.Options.Create(new AdoHookOptions
        {
            Allow = new AllowOptions
            {
                EventTypes = [.. eventTypes ?? ["git.pullrequest.created", "git.push", "build.complete"]],
                Organizations = [.. organizations ?? [Fixtures.AccountId]],
                ProjectIds = [.. projectIds ?? [Fixtures.ProjectId]],
                SubscriptionIds = [.. subscriptionIds ?? []],
            },
        }));

    private static Envelope Parse(string eventType)
    {
        Assert.True(Envelope.TryParse(Fixtures.Load(eventType), out var envelope, out _));
        return envelope!;
    }

    private static Envelope ParseJson(string json)
    {
        Assert.True(Envelope.TryParse(Encoding.UTF8.GetBytes(json), out var envelope, out _));
        return envelope!;
    }

    [Theory]
    [MemberData(nameof(Fixtures.EventTypes), MemberType = typeof(Fixtures))]
    public void Allows_every_fixture_when_listed(string eventType) =>
        Assert.Null(Create().Refuse(Parse(eventType)));

    [Fact]
    public void Refuses_an_event_type_not_listed() =>
        Assert.Equal("EventTypes", Create(eventTypes: ["git.push"]).Refuse(Parse("build.complete")));

    [Fact]
    public void Matches_event_types_ignoring_case() =>
        Assert.Null(Create(eventTypes: ["GIT.PUSH"]).Refuse(Parse("git.push")));

    [Theory]
    [InlineData("https://dev.azure.com/fabrikam/")]
    [InlineData("https://dev.azure.com/fabrikam")]
    [InlineData("HTTPS://DEV.AZURE.COM/FABRIKAM/")]
    public void Matches_an_organisation_by_base_url_ignoring_case_and_trailing_slash(string organization) =>
        Assert.Null(Create(organizations: [organization]).Refuse(Parse("git.pullrequest.created")));

    [Fact]
    public void Refuses_an_organisation_not_listed() =>
        Assert.Equal("Organizations", Create(organizations: ["https://dev.azure.com/someone-else/"]).Refuse(Parse("git.pullrequest.created")));

    [Fact]
    public void Refuses_a_hook_without_organisation_details() =>
        Assert.Equal("Organizations", Create().Refuse(ParseJson("""{"id":"e1","eventType":"git.push","resource":{}}""")));

    [Fact]
    public void Refuses_a_project_not_listed() =>
        Assert.Equal("ProjectIds", Create(projectIds: [Guid.NewGuid().ToString()]).Refuse(Parse("git.push")));

    [Fact]
    public void Refuses_a_hook_without_a_project() =>
        Assert.Equal("ProjectIds", Create().Refuse(ParseJson(
            "{\"id\":\"e1\",\"eventType\":\"git.push\",\"resource\":{},"
            + "\"resourceContainers\":{\"account\":{\"id\":\"" + Fixtures.AccountId + "\"}}}")));

    [Fact]
    public void Ignores_subscription_ids_when_none_are_listed() =>
        Assert.Null(Create().Refuse(Parse("git.push")));

    [Fact]
    public void Allows_a_listed_subscription() =>
        Assert.Null(Create(subscriptionIds: [Fixtures.BuildSubscriptionId]).Refuse(Parse("build.complete")));

    [Fact]
    public void Refuses_a_subscription_not_listed() =>
        Assert.Equal("SubscriptionIds", Create(subscriptionIds: [Guid.NewGuid().ToString()]).Refuse(Parse("build.complete")));

    [Fact]
    public void Refuses_a_hook_without_a_subscription_id_when_subscriptions_are_listed() =>
        Assert.Equal("SubscriptionIds", Create(subscriptionIds: [Fixtures.BuildSubscriptionId]).Refuse(Parse("git.push")));
}
