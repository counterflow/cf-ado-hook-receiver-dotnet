using AdoHookReceiver.Options;

namespace AdoHookReceiver.Tests;

public sealed class AdoHookOptionsValidatorTests
{
    private static AdoHookOptions Valid() => new()
    {
        Username = "ado",
        Password = "secret",
        Allow = new AllowOptions
        {
            EventTypes = ["git.push"],
            Organizations = [Fixtures.AccountId, "https://dev.azure.com/fabrikam/"],
            ProjectIds = [Fixtures.ProjectId],
            SubscriptionIds = [Fixtures.BuildSubscriptionId],
        },
    };

    private static IEnumerable<string> Failures(AdoHookOptions options) =>
        new AdoHookOptionsValidator().Validate(null, options).Failures ?? [];

    [Fact]
    public void Accepts_valid_options() =>
        Assert.True(new AdoHookOptionsValidator().Validate(null, Valid()).Succeeded);

    public static TheoryData<string, string> InvalidCases => new()
    {
        { "no username", "Username is required" },
        { "username with colon", "Username must not contain ':'" },
        { "no password", "Password is required" },
        { "zero body limit", "BodyLimitBytes must be greater than 0" },
        { "no event types", "EventTypes needs at least one entry" },
        { "blank event type", "EventTypes must not contain blank entries" },
        { "no organisations", "Organizations needs at least one entry" },
        { "http organisation", "is neither a GUID nor an absolute https URL" },
        { "no projects", "ProjectIds needs at least one entry" },
        { "project not a GUID", "ProjectIds entry 'Fabrikam' is not a GUID" },
        { "subscription not a GUID", "SubscriptionIds entry 'sub-1' is not a GUID" },
    };

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Rejects_invalid_options(string invalidCase, string expectedFailure)
    {
        var options = Valid();
        switch (invalidCase)
        {
            case "no username": options.Username = ""; break;
            case "username with colon": options.Username = "a:b"; break;
            case "no password": options.Password = ""; break;
            case "zero body limit": options.BodyLimitBytes = 0; break;
            case "no event types": options.Allow.EventTypes = []; break;
            case "blank event type": options.Allow.EventTypes = ["git.push", " "]; break;
            case "no organisations": options.Allow.Organizations = []; break;
            case "http organisation": options.Allow.Organizations = ["http://dev.azure.com/fabrikam/"]; break;
            case "no projects": options.Allow.ProjectIds = []; break;
            case "project not a GUID": options.Allow.ProjectIds = ["Fabrikam"]; break;
            case "subscription not a GUID": options.Allow.SubscriptionIds = ["sub-1"]; break;
        }

        Assert.Contains(Failures(options), failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
    }

    [Fact]
    public void Allows_an_empty_subscription_list()
    {
        var options = Valid();
        options.Allow.SubscriptionIds = [];

        Assert.Empty(Failures(options));
    }
}
