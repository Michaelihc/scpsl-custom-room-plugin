using System;
using System.Linq;
using System.Reflection;
using LabApi.Features.Wrappers;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Replacement;

/// <summary>
/// Optional bridge to ScpTiers' public tutorial-state hooks, so an SCP swap carries tier, progress and feat
/// counters to the new holder. <c>TransferTutorialState</c> applies the captured state to the other player now
/// holding the SCP role, reapplying tier buffs without promotion healing or tier cards. Absent ScpTiers (or a
/// build without the transfer hook) makes both calls no-ops. Source: <c>SCP Enhacements/scp-tiers/Plugin.cs</c>.
/// </summary>
internal static class ScpTiersBridge
{
    private static MethodInfo? _capture;
    private static MethodInfo? _transfer;
    private static bool _resolved;

    public static object? Capture(Player player)
    {
        Resolve();
        try
        {
            // Without the transfer hook there is nothing to carry the state to; skip the capture entirely.
            return _transfer == null ? null : _capture?.Invoke(null, new object[] { player });
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector:Replacement] ScpTiers capture failed: {ex.GetBaseException().Message}");
            return null;
        }
    }

    public static bool Transfer(Player player, object state)
    {
        Resolve();
        try
        {
            return _transfer?.Invoke(null, new[] { player, state }) is true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector:Replacement] ScpTiers transfer failed: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private static void Resolve()
    {
        if (_resolved)
        {
            return;
        }

        Type? type = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == "ScpTiers")
            ?.GetType("ScpTiers.ScpTiersPlugin", throwOnError: false);
        if (type == null)
        {
            return;
        }

        _capture = type.GetMethod("CaptureTutorialState", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Player) }, null);
        _transfer = type.GetMethod("TransferTutorialState", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Player), typeof(object) }, null);
        _resolved = _capture != null && _transfer != null;
    }
}
