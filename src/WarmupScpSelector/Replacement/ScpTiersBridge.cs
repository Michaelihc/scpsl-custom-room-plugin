using System;
using System.Linq;
using System.Reflection;
using LabApi.Features.Wrappers;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Replacement;

/// <summary>
/// Optional bridge to ScpTiers' public tutorial-state hooks, so an SCP swap carries tier, progress and feat
/// counters to the new holder. <c>RestoreTutorialState</c> reapplies tier buffs without promotion healing or
/// tier cards. Absent ScpTiers makes both calls no-ops. Source: <c>SCP Enhacements/scp-tiers/Plugin.cs</c>.
/// </summary>
internal static class ScpTiersBridge
{
    private static MethodInfo? _capture;
    private static MethodInfo? _restore;
    private static bool _resolved;

    public static object? Capture(Player player)
    {
        Resolve();
        try
        {
            return _capture?.Invoke(null, new object[] { player });
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector:Replacement] ScpTiers capture failed: {ex.GetBaseException().Message}");
            return null;
        }
    }

    public static bool Restore(Player player, object state)
    {
        Resolve();
        try
        {
            return _restore?.Invoke(null, new[] { player, state }) is true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector:Replacement] ScpTiers restore failed: {ex.GetBaseException().Message}");
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
        _restore = type.GetMethod("RestoreTutorialState", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Player), typeof(object) }, null);
        _resolved = _capture != null && _restore != null;
    }
}
