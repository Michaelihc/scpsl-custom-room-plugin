using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace WarmupScpSelector.Export;

/// <summary>
/// ProjectMER block types, mirrored from <c>ProjectMER.Features.Enums.BlockType</c>. Only the ones this
/// exporter emits are named; the numbering is ProjectMER's and must not be renumbered.
/// </summary>
internal enum SchematicBlockType
{
    Empty = 0,
    Primitive = 1,
    Light = 2,
    Pickup = 3,
    Workstation = 4,
    Text = 8,
}

/// <summary>One serialisable ProjectMER schematic block.</summary>
internal sealed class SchematicBlock
{
    public SchematicBlock(string name, int objectId, int parentId, SchematicBlockType blockType)
    {
        Name = name;
        ObjectId = objectId;
        ParentId = parentId;
        BlockType = blockType;
    }

    public string Name { get; }

    public int ObjectId { get; }

    public int ParentId { get; }

    public SchematicBlockType BlockType { get; }

    /// <summary>Transform RELATIVE TO THE PARENT block, exactly as ProjectMER applies it on load.</summary>
    public Vector3 Position { get; set; }

    public Vector3 Rotation { get; set; }

    public Vector3 Scale { get; set; } = Vector3.one;

    public Dictionary<string, object> Properties { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// A complete schematic document plus its JSON writer.
///
/// The format is ProjectMER's, verified against the vendored source rather than inferred from a sample:
/// blocks are a flat list linked by <c>ParentId</c>, every transform is LOCAL to its parent, colours are
/// <c>RRGGBBAA</c> hex, and <c>DisplaySize</c> on a text block is multiplied by 20 when it loads - so it
/// is divided by 20 here. Getting any of those wrong produces a file that opens but is subtly wrong,
/// which is worse than one that fails outright.
/// </summary>
internal sealed class StationSchematic
{
    /// <summary>ProjectMER multiplies a text block's DisplaySize by this on load.</summary>
    public const float TextDisplaySizeScale = 20f;

    private readonly List<SchematicBlock> _blocks = new();
    private int _nextId;

    public StationSchematic(int rootObjectId = 0)
    {
        RootObjectId = rootObjectId;
        _nextId = rootObjectId + 1;
    }

    /// <summary>
    /// The root every top-level block hangs from. ProjectMER looks the root up by id and tolerates it
    /// having no block of its own, which is what the authored DT.json did too.
    /// </summary>
    public int RootObjectId { get; }

    public IReadOnlyList<SchematicBlock> Blocks => _blocks;

    public SchematicBlock Add(string name, SchematicBlockType blockType, int parentId)
    {
        SchematicBlock block = new(name, _nextId++, parentId, blockType);
        _blocks.Add(block);
        return block;
    }

    public string ToJson()
    {
        StringBuilder sb = new();
        sb.Append("{\n  \"RootObjectId\": ").Append(RootObjectId.ToString(CultureInfo.InvariantCulture)).Append(",\n  \"Blocks\": [\n");
        for (int i = 0; i < _blocks.Count; i++)
        {
            WriteBlock(sb, _blocks[i]);
            sb.Append(i == _blocks.Count - 1 ? "\n" : ",\n");
        }

        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    private static void WriteBlock(StringBuilder sb, SchematicBlock block)
    {
        sb.Append("    {\n");
        sb.Append("      \"Name\": ").Append(Quote(block.Name)).Append(",\n");
        sb.Append("      \"ObjectId\": ").Append(Int(block.ObjectId)).Append(",\n");
        sb.Append("      \"ParentId\": ").Append(Int(block.ParentId)).Append(",\n");
        sb.Append("      \"AnimatorName\": \"\",\n");
        WriteVector(sb, "Position", block.Position);
        WriteVector(sb, "Rotation", block.Rotation);
        WriteVector(sb, "Scale", block.Scale);
        sb.Append("      \"BlockType\": ").Append(Int((int)block.BlockType)).Append(",\n");
        sb.Append("      \"Properties\": {");
        int written = 0;
        foreach (KeyValuePair<string, object> property in block.Properties)
        {
            sb.Append(written++ == 0 ? "\n" : ",\n");
            sb.Append("        ").Append(Quote(property.Key)).Append(": ").Append(Value(property.Value));
        }

        sb.Append(written == 0 ? "}\n" : "\n      }\n");
        sb.Append("    }");
    }

    private static void WriteVector(StringBuilder sb, string name, Vector3 value)
    {
        sb.Append("      ").Append(Quote(name)).Append(": { \"x\": ").Append(Float(value.x))
            .Append(", \"y\": ").Append(Float(value.y))
            .Append(", \"z\": ").Append(Float(value.z)).Append(" },\n");
    }

    private static string Value(object value) => value switch
    {
        null => "null",
        string text => Quote(text),
        bool flag => flag ? "true" : "false",
        int number => Int(number),
        byte number => Int(number),
        float number => Float(number),
        double number => Float((float)number),
        Vector2 vector => $"{{ \"x\": {Float(vector.x)}, \"y\": {Float(vector.y)} }}",
        Vector3 vector => $"{{ \"x\": {Float(vector.x)}, \"y\": {Float(vector.y)}, \"z\": {Float(vector.z)} }}",
        _ => Quote(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
    };

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Round-trip-safe, culture-invariant, and never emits NaN/Infinity into the document.</summary>
    private static string Float(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return "0.0";
        }

        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.IndexOf('.') < 0 && text.IndexOf('E') < 0 && text.IndexOf('e') < 0 ? text + ".0" : text;
    }

    /// <summary>
    /// ProjectMER parses an 8-character string as RRGGBBAA. Emitting that form (rather than <c>#RRGGBB</c>)
    /// keeps the alpha channel, which the translucent partitions and the HDR-boosted logo depend on.
    /// </summary>
    public static string ColorHex(Color color) =>
        ColorUtility.ToHtmlStringRGBA(new Color(
            Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), Mathf.Clamp01(color.a)));

    private static string Quote(string value)
    {
        StringBuilder sb = new(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
