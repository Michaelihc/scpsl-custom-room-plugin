using System.ComponentModel;

namespace WarmupScpSelector.Services;

public sealed class HintDisplayConfig
{
    [Description("Prefix added to hint IDs before they are sent to HSM. Keep it unique per plugin.")]
    public string TagPrefix { get; set; } = "warmupscp.";

    [Description("HSM X coordinate of the warmup status panel. HSM uses 0 as screen center.")]
    public float DefaultX { get; set; } = 0f;

    [Description("Font size used for the warmup status panel.")]
    public int PromptTextSize { get; set; } = 20;

    [Description("Extra HSM line height added between rendered lines.")]
    public float LineHeight { get; set; } = 0f;

    [Description("If true, asks HSM to refresh immediately after add/update/remove operations. Default false: HSM coalesces text updates within the hint's SyncSpeed window (~0.1s for Fast); adds/removes are still immediate. Only set true for genuinely per-frame-animated hints.")]
    public bool ForceFastUpdates { get; set; } = false;
}
