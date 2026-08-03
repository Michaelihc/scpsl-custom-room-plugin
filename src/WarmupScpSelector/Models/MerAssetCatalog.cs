using System;
using System.Collections.Generic;
using System.Linq;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Models;

/// <summary>
/// Registry of the authored Aim Range MER prop assets and their required marker contracts.
/// Pure metadata only: gameplay code keeps its own anchor/layout decisions (AimRangeLayout) and
/// collision stays in explicit AddBox geometry — this catalog exists so runtime visual spawning and
/// fail-closed validation can reference one authoritative asset/marker list instead of hardcoding
/// strings per call site.
/// </summary>
internal static class MerAssetCatalog
{
    /// <summary>Embedded resource suffix for the armory weapon rack prop.</summary>
    public const string WeaponRackAsset = "aim-weapon-rack.mer.json";

    /// <summary>Embedded resource suffix for the center-bay moving-target carrier prop.</summary>
    public const string MovingTargetCarrierAsset = "aim-moving-target-carrier.mer.json";

    /// <summary>Markers every weapon rack instance must expose (validated before visual spawn).</summary>
    public static readonly IReadOnlyList<string> WeaponRackMarkers = new[]
    {
        "marker_pivot",
        "marker_bounds_min",
        "marker_bounds_max",
        "marker_shelf_0",
        "marker_shelf_1",
        "marker_shelf_2",
        "marker_shelf_3",
        "marker_shelf_4",
        "marker_shelf_5",
    };

    /// <summary>Markers every moving-target carrier instance must expose.</summary>
    public static readonly IReadOnlyList<string> MovingTargetCarrierMarkers = new[]
    {
        "marker_pivot",
        "marker_bounds_min",
        "marker_bounds_max",
        "marker_target_mount",
    };

    /// <summary>
    /// Loads an embedded asset and verifies its exact marker set. Returns the parsed primitives on
    /// success; returns null (and logs) when the asset is missing or its markers do not match the
    /// contract, so callers can fail closed and disable only the affected visual prop.
    /// </summary>
    public static List<MerPrimitive>? LoadValidated(string assetSuffix, IReadOnlyList<string> requiredMarkers)
    {
        List<MerPrimitive> primitives = MerModelLoader.LoadEmbedded(assetSuffix);
        if (primitives.Count == 0)
        {
            Logger.Warn($"[WarmupScpSelector] MER asset '{assetSuffix}' missing or empty; visual prop disabled.");
            return null;
        }

        HashSet<string> actual = new(StringComparer.Ordinal);
        foreach (MerPrimitive primitive in primitives)
        {
            if (primitive.IsMarker)
            {
                actual.Add(primitive.Name);
            }
        }

        string[] missing = requiredMarkers.Where(marker => !actual.Contains(marker)).ToArray();
        if (missing.Length > 0)
        {
            Logger.Warn($"[WarmupScpSelector] MER asset '{assetSuffix}' is missing markers: {string.Join(", ", missing)}; visual prop disabled.");
            return null;
        }

        return primitives;
    }
}
