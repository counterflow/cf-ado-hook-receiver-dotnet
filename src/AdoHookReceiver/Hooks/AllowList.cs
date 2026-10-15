using AdoHookReceiver.Options;
using Microsoft.Extensions.Options;

namespace AdoHookReceiver.Hooks;

/// <summary>
/// Accepts only configured event types, organisations and projects, and, when configured,
/// subscriptions. A hook must match every list that is set; a missing field never matches.
/// </summary>
public sealed class AllowList
{
    private readonly HashSet<string> _eventTypes;
    private readonly HashSet<Guid> _organizationIds;
    private readonly HashSet<string> _organizationUrls;
    private readonly HashSet<Guid> _projectIds;
    private readonly HashSet<Guid> _subscriptionIds;

    public AllowList(IOptions<AdoHookOptions> options)
    {
        var allow = options.Value.Allow;

        _eventTypes = new HashSet<string>(allow.EventTypes.Select(e => e.Trim()), StringComparer.OrdinalIgnoreCase);
        _organizationIds = [.. allow.Organizations.Where(o => Guid.TryParse(o, out _)).Select(Guid.Parse)];
        _organizationUrls = new HashSet<string>(
            allow.Organizations.Where(o => !Guid.TryParse(o, out _)).Select(NormalizeUrl),
            StringComparer.OrdinalIgnoreCase);
        _projectIds = [.. allow.ProjectIds.Select(Guid.Parse)];
        _subscriptionIds = [.. allow.SubscriptionIds.Select(Guid.Parse)];
    }

    /// <summary>Returns the name of the list that refuses the hook, or null when it is allowed.</summary>
    public string? Refuse(Envelope envelope)
    {
        if (!_eventTypes.Contains(envelope.EventType))
        {
            return "EventTypes";
        }

        if (!IsAllowedOrganization(envelope))
        {
            return "Organizations";
        }

        if (!Contains(_projectIds, envelope.ProjectId))
        {
            return "ProjectIds";
        }

        if (_subscriptionIds.Count > 0 && !Contains(_subscriptionIds, envelope.SubscriptionId))
        {
            return "SubscriptionIds";
        }

        return null;
    }

    private bool IsAllowedOrganization(Envelope envelope) =>
        Contains(_organizationIds, envelope.AccountId)
        || (envelope.OrgUrl is { } url && _organizationUrls.Contains(NormalizeUrl(url)));

    private static bool Contains(HashSet<Guid> set, string? value) =>
        Guid.TryParse(value, out var id) && set.Contains(id);

    private static string NormalizeUrl(string url) => url.Trim().TrimEnd('/');
}
