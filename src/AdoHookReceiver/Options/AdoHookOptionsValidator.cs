using Microsoft.Extensions.Options;

namespace AdoHookReceiver.Options;

/// <summary>Refuses to let the app start on missing or invalid settings.</summary>
public sealed class AdoHookOptionsValidator : IValidateOptions<AdoHookOptions>
{
    private const string Prefix = AdoHookOptions.SectionName;

    public ValidateOptionsResult Validate(string? name, AdoHookOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Username))
        {
            failures.Add($"{Prefix}:Username is required.");
        }
        else if (options.Username.Contains(':'))
        {
            failures.Add($"{Prefix}:Username must not contain ':' (basic auth splits on it).");
        }

        if (string.IsNullOrEmpty(options.Password))
        {
            failures.Add($"{Prefix}:Password is required.");
        }

        if (options.BodyLimitBytes <= 0)
        {
            failures.Add($"{Prefix}:BodyLimitBytes must be greater than 0.");
        }

        var allow = options.Allow;

        RequireEntries(failures, "EventTypes", allow.EventTypes);
        if (allow.EventTypes.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add($"{Prefix}:Allow:EventTypes must not contain blank entries.");
        }

        RequireEntries(failures, "Organizations", allow.Organizations);
        foreach (var organization in allow.Organizations.Where(o => !IsGuid(o) && !IsHttpsUrl(o)))
        {
            failures.Add($"{Prefix}:Allow:Organizations entry '{organization}' is neither a GUID nor an absolute https URL.");
        }

        RequireEntries(failures, "ProjectIds", allow.ProjectIds);
        RequireGuids(failures, "ProjectIds", allow.ProjectIds);
        RequireGuids(failures, "SubscriptionIds", allow.SubscriptionIds);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireEntries(List<string> failures, string list, List<string> values)
    {
        if (values.Count == 0)
        {
            failures.Add($"{Prefix}:Allow:{list} needs at least one entry; nothing is accepted until the allow-list is configured.");
        }
    }

    private static void RequireGuids(List<string> failures, string list, List<string> values)
    {
        foreach (var value in values.Where(v => !IsGuid(v)))
        {
            failures.Add($"{Prefix}:Allow:{list} entry '{value}' is not a GUID.");
        }
    }

    private static bool IsGuid(string value) => Guid.TryParse(value, out _);

    private static bool IsHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
