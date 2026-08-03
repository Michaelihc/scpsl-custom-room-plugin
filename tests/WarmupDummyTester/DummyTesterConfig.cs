using System.ComponentModel;

namespace WarmupDummyTester;

/// <summary>Config for the dev-only dummy tester. See README; never ship this plugin.</summary>
public sealed class DummyTesterConfig
{
    [Description("Whether the dummy tester is active. DEV TOOL ONLY — do not enable on a production server.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Seconds between dummy scans and on-screen log refreshes.")]
    public float RefreshSeconds { get; set; } = 1f;

    [Description("Horizontal radius used to spread dummies around the selector spawn so they don't stack.")]
    public float SpreadRadius { get; set; } = 3f;
}
