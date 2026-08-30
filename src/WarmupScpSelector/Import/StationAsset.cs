using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using WarmupScpSelector.Export;
using WarmupScpSelector.Models;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Import;

/// <summary>One block read back from an authored ProjectMER schematic.</summary>
internal sealed class StationAssetBlock
{
    public string Name { get; set; } = string.Empty;

    public int ObjectId { get; set; } = -1;

    public int ParentId { get; set; } = -1;

    public SchematicBlockType BlockType { get; set; } = SchematicBlockType.Primitive;

    /// <summary>Transform relative to the parent block, as ProjectMER stores it.</summary>
    public Vector3 Position { get; set; }

    public Vector3 Rotation { get; set; }

    public Vector3 Scale { get; set; } = Vector3.one;

    public Dictionary<string, object?> Properties { get; } = new(StringComparer.Ordinal);

    /// <summary>Resolved position relative to the schematic root, ignoring parent rotation/scale.</summary>
    public Vector3 ApproximateRootPosition { get; set; }

    public bool TryColor(out Color color)
    {
        color = Color.white;
        if (!Properties.TryGetValue("Color", out object? raw) || raw is not string text || text.Length == 0)
        {
            return false;
        }

        string candidate = text.Trim();
        if (candidate[0] != '#' && candidate.Length == 8)
        {
            candidate = "#" + candidate;
        }

        return ColorUtility.TryParseHtmlString(candidate, out color);
    }

    public float Number(string key, float fallback) =>
        Properties.TryGetValue(key, out object? raw) && raw is double number ? (float)number : fallback;

    public int Integer(string key, int fallback) =>
        Properties.TryGetValue(key, out object? raw) && raw is double number ? (int)number : fallback;

    public string Text(string key, string fallback) =>
        Properties.TryGetValue(key, out object? raw) && raw is string text ? text : fallback;

    public Vector2 Vector2Property(string key, Vector2 fallback)
    {
        if (!Properties.TryGetValue(key, out object? raw) || raw is not Dictionary<string, object?> map)
        {
            return fallback;
        }

        return new Vector2(
            map.TryGetValue("x", out object? x) && x is double dx ? (float)dx : fallback.x,
            map.TryGetValue("y", out object? y) && y is double dy ? (float)dy : fallback.y);
    }
}

/// <summary>
/// An authored station schematic, read back from the same ProjectMER format the exporter writes.
///
/// This is the merge-back half of the hand-off loop: someone rearranges the exported room in the
/// in-game map editor and the result is spawned in place of the generated geometry. Reading the format
/// this plugin also writes keeps one definition of it rather than two that can disagree.
/// </summary>
internal sealed class StationAsset
{
    private readonly List<StationAssetBlock> _blocks = new();

    private StationAsset(int rootObjectId, string source)
    {
        RootObjectId = rootObjectId;
        Source = source;
    }

    public int RootObjectId { get; }

    /// <summary>Where this was loaded from, for logs.</summary>
    public string Source { get; }

    public IReadOnlyList<StationAssetBlock> Blocks => _blocks;

    /// <summary>Named <c>marker_*</c> empties, which are the anchor contract when the author keeps them.</summary>
    public Dictionary<string, Vector3> Markers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static StationAsset? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            if (MerModelLoader.ParseJson(File.ReadAllText(path)) is not Dictionary<string, object?> root ||
                !root.TryGetValue("Blocks", out object? rawBlocks) || rawBlocks is not List<object?> blocks)
            {
                Logger.Warn($"[WarmupScpSelector] Authored station '{path}' is not a schematic document.");
                return null;
            }

            int rootId = root.TryGetValue("RootObjectId", out object? rid) && rid is double d ? (int)d : 0;
            StationAsset asset = new(rootId, path);
            foreach (object? entry in blocks)
            {
                if (entry is Dictionary<string, object?> block)
                {
                    asset._blocks.Add(ReadBlock(block));
                }
            }

            asset.ResolveRootPositions();
            foreach (StationAssetBlock block in asset._blocks)
            {
                if (block.Name.StartsWith("marker_", StringComparison.OrdinalIgnoreCase))
                {
                    asset.Markers[block.Name] = block.ApproximateRootPosition;
                }
            }

            return asset;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector] Could not read authored station '{path}': {ex.GetBaseException().Message}");
            return null;
        }
    }

    private static StationAssetBlock ReadBlock(Dictionary<string, object?> raw)
    {
        StationAssetBlock block = new()
        {
            Name = raw.TryGetValue("Name", out object? name) && name is string text ? text : string.Empty,
            ObjectId = Int(raw, "ObjectId", -1),
            ParentId = Int(raw, "ParentId", -1),
            BlockType = (SchematicBlockType)Int(raw, "BlockType", (int)SchematicBlockType.Primitive),
            Position = Vector(raw, "Position", Vector3.zero),
            Rotation = Vector(raw, "Rotation", Vector3.zero),
            Scale = Vector(raw, "Scale", Vector3.one),
        };

        if (raw.TryGetValue("Properties", out object? properties) && properties is Dictionary<string, object?> map)
        {
            foreach (KeyValuePair<string, object?> property in map)
            {
                block.Properties[property.Key] = property.Value;
            }
        }

        return block;
    }

    /// <summary>
    /// Accumulates parent offsets so callers can ask which compartment a block sits in. Parent rotation
    /// and scale are deliberately NOT applied: this is a locator for filtering, not a transform solver,
    /// and the spawner reproduces the real hierarchy instead of baking it.
    /// </summary>
    private void ResolveRootPositions()
    {
        Dictionary<int, StationAssetBlock> byId = new();
        foreach (StationAssetBlock block in _blocks)
        {
            if (block.ObjectId >= 0)
            {
                byId[block.ObjectId] = block;
            }
        }

        foreach (StationAssetBlock block in _blocks)
        {
            Vector3 sum = block.Position;
            StationAssetBlock cursor = block;
            int guard = 0;
            while (cursor.ParentId != RootObjectId && byId.TryGetValue(cursor.ParentId, out StationAssetBlock? parent) && guard++ < 16)
            {
                sum += parent.Position;
                cursor = parent;
            }

            block.ApproximateRootPosition = sum;
        }
    }

    private static int Int(Dictionary<string, object?> map, string key, int fallback) =>
        map.TryGetValue(key, out object? raw) && raw is double number ? (int)number : fallback;

    private static Vector3 Vector(Dictionary<string, object?> map, string key, Vector3 fallback)
    {
        if (!map.TryGetValue(key, out object? raw) || raw is not Dictionary<string, object?> vector)
        {
            return fallback;
        }

        return new Vector3(
            vector.TryGetValue("x", out object? x) && x is double dx ? (float)dx : 0f,
            vector.TryGetValue("y", out object? y) && y is double dy ? (float)dy : 0f,
            vector.TryGetValue("z", out object? z) && z is double dz ? (float)dz : 0f);
    }

    public override string ToString() =>
        $"{_blocks.Count} blocks, {Markers.Count} markers, from {Path.GetFileName(Source)}";

    /// <summary>Culture-invariant parse helper kept here so the reader owns all of its own conversions.</summary>
    public static bool TryParseFloat(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
