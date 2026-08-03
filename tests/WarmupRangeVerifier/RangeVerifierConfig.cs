using System.ComponentModel;

namespace WarmupRangeVerifier;

/// <summary>Configuration for the disposable local-only Aim Range verifier.</summary>
public sealed class RangeVerifierConfig
{
    [Description("Run the verifier automatically after WaitingForPlayers. DEV TOOL ONLY.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Maximum seconds to poll for the product Aim Range to finish startup.")]
    public float StartupTimeoutSeconds { get; set; } = 15f;

    [Description("Maximum seconds to poll for each dummy role/settling state.")]
    public float DummySettleTimeoutSeconds { get; set; } = 4f;

    [Description("Restart the product Aim lane after cleanup assertions so the port remains ready for manual QA.")]
    public bool RestartRangeAfterVerification { get; set; } = true;
}
