using System.Text;

namespace AdoHookReceiver.Tests;

/// <summary>
/// Microsoft's documented service hook samples, under Fixtures/Ado. All three share one account
/// and project id; their organisation base URLs differ.
/// </summary>
public static class Fixtures
{
    public const string AccountId = "bbbb1b1b-cc2c-dd3d-ee4e-ffffff5f5f5f";
    public const string ProjectId = "00aa00aa-bb11-cc22-dd33-44ee44ee44ee";
    public const string BuildSubscriptionId = "aaaa0a0a-bb1b-cc2c-dd3d-eeeeee4e4e4e";

    public static TheoryData<string> EventTypes => ["git.pullrequest.created", "git.push", "build.complete"];

    public static byte[] Load(string eventType) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ado", $"{eventType}.json"));

    /// <summary>The fixture's JSON text without surrounding whitespace, as a parser sees it.</summary>
    public static string Text(string eventType) => Encoding.UTF8.GetString(Load(eventType)).Trim();
}
