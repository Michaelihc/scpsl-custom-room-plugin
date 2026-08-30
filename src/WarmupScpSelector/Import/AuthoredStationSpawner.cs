using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using UnityEngine;
using WarmupScpSelector.Export;
using WarmupScpSelector.Warmup;
using Logger = LabApi.Features.Console.Logger;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace WarmupScpSelector.Import;

/// <summary>What an authored station spawn actually produced.</summary>
internal readonly struct AuthoredStationResult
{
    public AuthoredStationResult(int spawned, int skippedShaft, int unsupported)
    {
        Spawned = spawned;
        SkippedShaft = skippedShaft;
        Unsupported = unsupported;
    }

    public int Spawned { get; }

    /// <summary>Blocks inside a code-owned compartment (parkour shaft, aim bay), deliberately not spawned.</summary>
    public int SkippedShaft { get; }

    public int Unsupported { get; }

    public override string ToString() =>
        $"{Spawned} blocks spawned, {SkippedShaft} code-owned blocks skipped, {Unsupported} unsupported";
}

/// <summary>
/// Spawns an authored station schematic in place of the generated static geometry.
///
/// Two rules make this safe, and both exist because of what a real edited file turned out to contain:
///
/// 1. THE AIM BAY IS NEVER SPAWNED FROM THE ASSET - see <see cref="IsCodeOwned"/>. Its dividers and
///    cover are what the bots path around, and the lane spawns that furniture itself, so taking it from
///    the asset too would spawn every piece twice. The parkour shaft IS spawned from the asset, and the
///    Pulse Line reads its gates back off those landings so geometry and gates stay one thing.
/// 2. PICKUPS ARE NEVER SPAWNED FROM THE ASSET. Selection coins and counter guns carry runtime identity
///    (serial to SCP role, owned-weapon bookkeeping); a static copy would look right and do nothing.
///    Code spawns them at the anchors as usual.
///
/// Everything else - shell, decor, models, lights, signage - comes from the file, so an artist's work
/// lands as authored.
/// </summary>
internal sealed class AuthoredStationSpawner
{
    private readonly List<AdminToy> _toys = new();

    public bool IsSpawned { get; private set; }

    /// <summary>Spawns the asset at the station origin. Returns false and cleans up on any failure.</summary>
    public bool TrySpawn(StationAsset asset, WarmupHallLayout hall, out AuthoredStationResult result, out string error)
    {
        result = default;
        error = string.Empty;
        Despawn();
        try
        {
            Dictionary<int, Transform> parents = new();
            int spawned = 0, skipped = 0, unsupported = 0;

            // Parents before children: a child's transform is local to its parent, and the brand logo's
            // shear only exists while that hierarchy does.
            foreach (StationAssetBlock block in Ordered(asset))
            {
                if (IsCodeOwned(hall, block.ApproximateRootPosition))
                {
                    skipped++;
                    continue;
                }

                if (block.Name.StartsWith("marker_", StringComparison.OrdinalIgnoreCase) ||
                    block.BlockType == SchematicBlockType.Pickup)
                {
                    continue; // anchors are data, pickups are runtime-owned
                }

                Transform? parent = block.ParentId != asset.RootObjectId && parents.TryGetValue(block.ParentId, out Transform? found)
                    ? found
                    : null;

                AdminToy? toy = Spawn(block, hall, parent, ref unsupported);
                if (toy == null)
                {
                    continue;
                }

                _toys.Add(toy);
                spawned++;
                if (block.HasObjectId)
                {
                    parents[block.ObjectId] = toy.Transform;
                }
            }

            IsSpawned = true;
            result = new AuthoredStationResult(spawned, skipped, unsupported);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            Despawn();
            return false;
        }
    }

    public void Despawn()
    {
        IsSpawned = false;
        for (int i = _toys.Count - 1; i >= 0; i--)
        {
            try
            {
                if (!_toys[i].IsDestroyed)
                {
                    _toys[i].Destroy();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Authored station cleanup failed: {ex.Message}");
            }
        }

        _toys.Clear();
    }

    /// <summary>
    /// Compartments whose geometry is bound to gameplay and is therefore always built by code, never
    /// taken from an asset:
    ///
    /// - the AIM BAY, because its lane dividers and cover are what the bots path around and shoot from,
    ///   and because the lane spawns that furniture itself - taking it from the asset as well would
    ///   simply spawn every piece twice.
    ///
    /// The parkour shaft is deliberately NOT here. Its authored landings ARE spawned, and the Pulse Line
    /// recovers its gates from them (AuthoredParkourRoute), so an author's route drives gameplay instead
    /// of being overridden by the generated one - which a player reported crossing without sprinting.
    ///
    /// A point is judged by its position relative to the compartment, with a little slack so wall-thick
    /// blocks sitting exactly on a boundary go the same way as the compartment they belong to.
    /// </summary>
    public static bool IsCodeOwned(WarmupHallLayout hall, Vector3 local)
    {
        return Inside(hall.AimBay, local);

        static bool Inside(StationZone zone, Vector3 p) =>
            p.x >= zone.MinX - 1f && p.x <= zone.MaxX + 1f &&
            p.z >= zone.MinZ - 1f && p.z <= zone.MaxZ + 1f;
    }

    /// <summary>Root-most first, so a parent transform always exists before its children ask for it.</summary>
    private static IEnumerable<StationAssetBlock> Ordered(StationAsset asset)
    {
        Dictionary<int, StationAssetBlock> byId = asset.Blocks
            .Where(b => b.HasObjectId)
            .GroupBy(b => b.ObjectId)
            .ToDictionary(g => g.Key, g => g.First());

        int Depth(StationAssetBlock block)
        {
            int depth = 0;
            StationAssetBlock cursor = block;
            while (cursor.ParentId != asset.RootObjectId && byId.TryGetValue(cursor.ParentId, out StationAssetBlock? parent) && depth < 16)
            {
                cursor = parent;
                depth++;
            }

            return depth;
        }

        return asset.Blocks.OrderBy(Depth);
    }

    private AdminToy? Spawn(StationAssetBlock block, WarmupHallLayout hall, Transform? parent, ref int unsupported)
    {
        // A child is authored relative to its parent, so it is spawned in local space untouched. Anything
        // else is authored relative to the schematic root, which is the station origin.
        //
        // ApproximateRootPosition (rather than Position) is what makes the fallback safe: a block whose
        // parent exists in the file but was NOT spawned - a marker, a pickup, a code-owned compartment -
        // still lands roughly where it was authored instead of dropping its local offset on the station
        // origin. For a genuinely root-level block the two are identical.
        Vector3 position = parent != null ? block.Position : hall.Origin + block.ApproximateRootPosition;
        Quaternion rotation = Quaternion.Euler(block.Rotation);

        switch (block.BlockType)
        {
            case SchematicBlockType.Primitive:
            case SchematicBlockType.Empty:
            {
                PrimitiveObjectToy toy = parent == null
                    ? PrimitiveObjectToy.Create(position, rotation, block.Scale, networkSpawn: false)
                    : PrimitiveObjectToy.Create(position, rotation, block.Scale, parent, networkSpawn: false);
                toy.Type = (PrimitiveType)block.Integer("PrimitiveType", (int)PrimitiveType.Cube);
                toy.Color = block.TryColor(out Color color) ? color : Color.white;
                toy.Flags = block.BlockType == SchematicBlockType.Empty
                    ? PrimitiveFlags.None
                    : (PrimitiveFlags)block.Integer("PrimitiveFlags", (int)PrimitiveFlags.Visible);
                toy.IsStatic = true;
                toy.Spawn();
                return toy;
            }

            case SchematicBlockType.Light:
            {
                LightSourceToy light = LightSourceToy.Create(position, rotation, Vector3.one, networkSpawn: false);
                light.Color = block.TryColor(out Color color) ? color : Color.white;
                light.Intensity = block.Number("Intensity", 1f);
                light.Range = block.Number("Range", 10f);
                light.Type = (LightType)block.Integer("LightType", (int)LightType.Point);
                light.ShadowType = (LightShadows)block.Integer("ShadowType", (int)LightShadows.None);
                light.IsStatic = true;
                light.Spawn();
                return light;
            }

            case SchematicBlockType.Text:
            {
                TextToy text = TextToy.Create(position, rotation, block.Scale, networkSpawn: false);
                text.TextFormat = block.Text("Text", string.Empty);
                // ProjectMER stores DisplaySize pre-divided by 20; undo that on the way back in.
                text.DisplaySize = block.Vector2Property("DisplaySize", new Vector2(10f, 2f)) * StationSchematic.TextDisplaySizeScale;
                text.IsStatic = true;
                text.Spawn();
                return text;
            }

            default:
                unsupported++;
                return null;
        }
    }
}
