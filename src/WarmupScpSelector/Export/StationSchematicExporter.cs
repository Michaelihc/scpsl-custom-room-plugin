using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabApi.Features.Wrappers;
using UnityEngine;
using WarmupScpSelector.Activities.Parkour;
using WarmupScpSelector.Warmup;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Export;

/// <summary>Where an export landed and what it contains.</summary>
internal readonly struct StationExportResult
{
    public StationExportResult(string path, int primitives, int lights, int texts, int pickups, int markers)
    {
        Path = path;
        Primitives = primitives;
        Lights = lights;
        Texts = texts;
        Pickups = pickups;
        Markers = markers;
    }

    public string Path { get; }
    public int Primitives { get; }
    public int Lights { get; }
    public int Texts { get; }
    public int Pickups { get; }
    public int Markers { get; }

    public int Total => Primitives + Lights + Texts + Pickups + Markers;

    public override string ToString() =>
        $"{Total} blocks ({Primitives} primitives, {Lights} lights, {Texts} text, {Pickups} pickups, {Markers} markers)";
}

/// <summary>
/// Exports the warmup station as a ProjectMER schematic so someone else can open it in the in-game map
/// editor, rearrange it, and hand the file back.
///
/// It exports what is ACTUALLY SPAWNED rather than re-describing the geometry from the layout code. That
/// is the whole point: a second description of the room would drift from the builders the first time
/// either changed, and the drift would be invisible until someone spawned the export and found it did
/// not match the game. Reading the world cannot drift.
///
/// Two things survive the round trip that a naive dump would destroy:
///
/// - PARENTING. The brand logo is ~200 quads whose shear comes from a non-uniformly scaled invisible
///   parent times a rotated child. Flattening those to world transforms silently removes the shear and
///   the emblem comes back subtly wrong, so a toy parented to another exported toy is emitted with its
///   parent's id and its LOCAL transform.
/// - ANCHORS. Coins, native workstations, bots, and targets are not schematic geometry, and the gameplay
///   anchors (spawn point, hatch planes, parkour landings, firing line) are not geometry at all. Both are
///   emitted as named <c>marker_*</c> empties so an editor can see and move them, and so a future import
///   can bind gameplay to them instead of to hardcoded numbers.
/// </summary>
internal static class StationSchematicExporter
{
    /// <summary>Slack around the station when deciding which world toys belong to it.</summary>
    private const float CaptureSlack = 6f;

    public static bool TryExport(
        WarmupHallLayout hall,
        ParkourLayout? parkour,
        AimRangeLayout? aim,
        string schematicName,
        string schematicsDirectory,
        out StationExportResult result,
        out string error)
    {
        result = default;
        error = string.Empty;
        try
        {
            StationSchematic schematic = new();
            Vector3 origin = hall.Origin;

            // Map every captured toy to its block id first, so a child can resolve its parent's id
            // regardless of the order the world hands them to us.
            List<AdminToy> captured = CaptureToys(hall);
            Dictionary<Transform, AdminToy> byTransform = new();
            foreach (AdminToy toy in captured)
            {
                byTransform[toy.Transform] = toy;
            }

            // Parents MUST be emitted before their children, because a child resolves its parent's block
            // id from what has already been written. AdminToy.List order is arbitrary, so a logo quad
            // could otherwise be emitted first, fail to find its parent, and be written as a flattened
            // world transform - silently dropping the shear that makes the emblem correct.
            Dictionary<AdminToy, SchematicBlock> emitted = new();
            int primitives = 0, lights = 0, texts = 0;
            foreach (AdminToy toy in captured.OrderBy(t => CapturedDepth(t, byTransform)))
            {
                SchematicBlock? block = EmitToy(schematic, toy, byTransform, emitted, origin, ref primitives, ref lights, ref texts);
                if (block != null)
                {
                    emitted[toy] = block;
                }
            }

            int pickups = EmitPickups(schematic, hall, origin);
            int markers = EmitAnchors(schematic, hall, parkour, aim, origin);

            string directory = Path.Combine(schematicsDirectory, schematicName);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, schematicName + ".json");
            File.WriteAllText(path, schematic.ToJson(), new System.Text.UTF8Encoding(false));

            result = new StationExportResult(path, primitives, lights, texts, pickups, markers);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            return false;
        }
    }

    /// <summary>Every admin toy standing inside the station, plus a little slack for wall thickness.</summary>
    private static List<AdminToy> CaptureToys(WarmupHallLayout hall)
    {
        List<AdminToy> captured = new();
        foreach (AdminToy toy in AdminToy.List)
        {
            try
            {
                if (toy == null || toy.IsDestroyed || toy.Transform == null)
                {
                    continue;
                }

                if (hall.IsInsideStation(toy.Transform.position, CaptureSlack, CaptureSlack))
                {
                    captured.Add(toy);
                }
            }
            catch
            {
                // A toy can be destroyed between enumeration and inspection; skip it rather than
                // aborting an otherwise complete export.
            }
        }

        return captured;
    }

    /// <summary>
    /// How many captured ancestors a toy has. Sorting by this guarantees a parent is written before any
    /// of its children. Bounded so a pathological or cyclic hierarchy cannot spin.
    /// </summary>
    private static int CapturedDepth(AdminToy toy, Dictionary<Transform, AdminToy> byTransform)
    {
        int depth = 0;
        Transform? cursor = toy.Transform != null ? toy.Transform.parent : null;
        while (cursor != null && depth < 16)
        {
            if (byTransform.ContainsKey(cursor))
            {
                depth++;
            }

            cursor = cursor.parent;
        }

        return depth;
    }

    private static SchematicBlock? EmitToy(
        StationSchematic schematic,
        AdminToy toy,
        Dictionary<Transform, AdminToy> byTransform,
        Dictionary<AdminToy, SchematicBlock> emitted,
        Vector3 origin,
        ref int primitives,
        ref int lights,
        ref int texts)
    {
        Transform transform = toy.Transform;
        Transform? parent = transform.parent;
        bool parented = parent != null && byTransform.TryGetValue(parent, out AdminToy parentToy) &&
            emitted.TryGetValue(parentToy, out SchematicBlock parentBlock);

        int parentId = schematic.RootObjectId;
        Vector3 position;
        Vector3 rotation;
        Vector3 scale;
        if (parented)
        {
            parentId = emitted[byTransform[parent!]].ObjectId;
            position = transform.localPosition;
            rotation = transform.localEulerAngles;
            scale = transform.localScale;
        }
        else
        {
            // Station-local, so the file is portable: the station floats at whatever height the surface
            // zone resolved to, and baking that in would make the schematic useless anywhere else.
            position = transform.position - origin;
            rotation = transform.eulerAngles;
            scale = transform.localScale;
        }

        switch (toy)
        {
            case PrimitiveObjectToy primitive:
            {
                SchematicBlock block = schematic.Add(Name(primitive, "primitive"), SchematicBlockType.Primitive, parentId);
                block.Position = position;
                block.Rotation = rotation;
                block.Scale = scale;
                block.Properties["PrimitiveType"] = (int)primitive.Type;
                block.Properties["Color"] = StationSchematic.ColorHex(primitive.Color);
                block.Properties["PrimitiveFlags"] = (int)primitive.Flags;
                block.Properties["Static"] = false;
                primitives++;
                return block;
            }

            case LightSourceToy light:
            {
                SchematicBlock block = schematic.Add(Name(light, "light"), SchematicBlockType.Light, parentId);
                block.Position = position;
                block.Rotation = rotation;
                block.Scale = Vector3.one;
                block.Properties["LightType"] = (int)light.Type;
                block.Properties["Color"] = StationSchematic.ColorHex(light.Color);
                block.Properties["Intensity"] = light.Intensity;
                block.Properties["Range"] = light.Range;
                block.Properties["Shape"] = (int)light.Shape;
                block.Properties["SpotAngle"] = light.SpotAngle;
                block.Properties["InnerSpotAngle"] = light.InnerSpotAngle;
                block.Properties["ShadowStrength"] = light.ShadowStrength;
                block.Properties["ShadowType"] = (int)light.ShadowType;
                block.Properties["Static"] = false;
                lights++;
                return block;
            }

            case TextToy text:
            {
                SchematicBlock block = schematic.Add(Name(text, "text"), SchematicBlockType.Text, parentId);
                block.Position = position;
                block.Rotation = rotation;
                block.Scale = scale;
                block.Properties["Text"] = text.TextFormat ?? string.Empty;
                // ProjectMER multiplies this by 20 on load, so store the pre-multiplied value.
                block.Properties["DisplaySize"] = text.DisplaySize / StationSchematic.TextDisplaySizeScale;
                block.Properties["Static"] = false;
                texts++;
                return block;
            }

            default:
                return null;
        }
    }

    /// <summary>Selection coins and any other station pickup, as real ProjectMER pickup blocks.</summary>
    private static int EmitPickups(StationSchematic schematic, WarmupHallLayout hall, Vector3 origin)
    {
        int count = 0;
        foreach (Pickup pickup in Pickup.List)
        {
            try
            {
                if (pickup == null || pickup.GameObject == null ||
                    !hall.IsInsideStation(pickup.Position, CaptureSlack, CaptureSlack))
                {
                    continue;
                }

                SchematicBlock block = schematic.Add($"pickup_{pickup.Type}", SchematicBlockType.Pickup, schematic.RootObjectId);
                block.Position = pickup.Position - origin;
                block.Rotation = pickup.Rotation.eulerAngles;
                block.Scale = pickup.Transform != null ? pickup.Transform.localScale : Vector3.one;
                block.Properties["ItemType"] = (int)pickup.Type;
                block.Properties["Chance"] = 100f;
                count++;
            }
            catch
            {
                // Same rationale as toys: a pickup can vanish mid-export.
            }
        }

        return count;
    }

    /// <summary>
    /// Gameplay anchors as named empties. These are the contract for merging an edited file back: they
    /// say where the spawn point, hatches, exhibits, firing line, and parkour landings are, so an editor
    /// can move them deliberately instead of an importer having to guess from geometry.
    /// </summary>
    private static int EmitAnchors(
        StationSchematic schematic,
        WarmupHallLayout hall,
        ParkourLayout? parkour,
        AimRangeLayout? aim,
        Vector3 origin)
    {
        int count = 0;

        count += Marker(schematic, "marker_spawn", hall.SpawnPosition - origin);
        count += Marker(schematic, "marker_logo", hall.LogoAnchor - origin);
        count += Marker(schematic, "marker_banner", hall.BannerAnchor - origin);

        foreach (StationZone zone in hall.Zones)
        {
            count += Marker(schematic, $"marker_zone_{zone.Id}", new Vector3(zone.CenterX, 0f, zone.CenterZ));
        }

        for (int i = 0; i < hall.Openings.Count; i++)
        {
            StationOpening hatch = hall.Openings[i];
            float center = (hatch.From + hatch.To) / 2f;
            Vector3 local = hatch.InConstantXWall
                ? new Vector3(hatch.Plane, 0f, center)
                : new Vector3(center, 0f, hatch.Plane);
            count += Marker(schematic, $"marker_hatch_{i}", local);
        }

        if (aim != null)
        {
            count += Marker(schematic, "marker_aim_entrance", aim.EntranceSpawn - origin);
            count += Marker(schematic, "marker_aim_shooting_line", new Vector3(aim.ShootingLineX, 0f, 0f));
            count += Marker(schematic, "marker_aim_backstop", new Vector3(aim.BackstopX, 0f, 0f));
            for (int i = 0; i < aim.ShelfAnchors.Count; i++)
            {
                count += Marker(schematic, $"marker_aim_gun_{i}", aim.ShelfAnchors[i].LocalPosition - origin);
            }
        }

        if (parkour != null)
        {
            count += Marker(schematic, "marker_parkour_start", parkour.StartPlate.RecoveryPosition - origin);
            count += Marker(schematic, "marker_parkour_finish", parkour.FinishPlate.RecoveryPosition - origin);
            count += Marker(schematic, "marker_parkour_reset_coin", parkour.ResetCoinPosition - origin);
            for (int i = 0; i < parkour.Platforms.Count; i++)
            {
                count += Marker(schematic, $"marker_parkour_landing_{i:00}", parkour.Platforms[i].RecoveryPosition - origin);
            }
        }

        return count;
    }

    private static int Marker(StationSchematic schematic, string name, Vector3 localPosition)
    {
        SchematicBlock block = schematic.Add(name, SchematicBlockType.Empty, schematic.RootObjectId);
        block.Position = localPosition;
        block.Rotation = Vector3.zero;
        block.Scale = Vector3.one;
        return 1;
    }

    /// <summary>
    /// A stable, human-meaningful block name. Unity appends "(Clone)" to every spawned prefab, which is
    /// noise in an editor; the toy's own name is kept when it carries one.
    /// </summary>
    private static string Name(AdminToy toy, string fallback)
    {
        try
        {
            string name = toy.GameObject != null ? toy.GameObject.name : string.Empty;
            name = name.Replace("(Clone)", string.Empty).Trim();
            return name.Length > 0 ? name : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
