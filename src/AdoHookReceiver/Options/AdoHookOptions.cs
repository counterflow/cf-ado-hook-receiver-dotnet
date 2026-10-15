namespace AdoHookReceiver.Options;

/// <summary>Settings bound from the <c>AdoHook</c> configuration section.</summary>
public sealed class AdoHookOptions
{
    public const string SectionName = "AdoHook";

    /// <summary>Basic auth username ADO sends.</summary>
    public string Username { get; set; } = "";

    /// <summary>Basic auth password ADO sends.</summary>
    public string Password { get; set; } = "";

    /// <summary>Largest request body accepted, in bytes.</summary>
    public long BodyLimitBytes { get; set; } = 5 * 1024 * 1024;

    public AllowOptions Allow { get; set; } = new();
}

/// <summary>
/// The allow-list. Event types, organisations and projects are required;
/// subscription ids are an optional extra restriction.
/// </summary>
public sealed class AllowOptions
{
    /// <summary>ADO event ids, e.g. <c>git.pullrequest.created</c>.</summary>
    public List<string> EventTypes { get; set; } = [];

    /// <summary>Account GUIDs or organisation base URLs.</summary>
    public List<string> Organizations { get; set; } = [];

    public List<string> ProjectIds { get; set; } = [];

    /// <summary>Empty means subscription ids are not checked.</summary>
    public List<string> SubscriptionIds { get; set; } = [];
}
