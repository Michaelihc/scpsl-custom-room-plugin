using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace WarmupScpSelector.Models;

/// <summary>One local transform block parsed from a ProjectMER <c>.mer.json</c> model.</summary>
internal class MerTransform
{
    public string Name { get; set; } = string.Empty;

    public int ObjectId { get; set; } = -1;

    public int ParentId { get; set; } = -1;

    public Vector3 Position { get; set; }

    public Vector3 Rotation { get; set; }

    public Vector3 Scale { get; set; } = Vector3.one;
}

/// <summary>One primitive block parsed from a ProjectMER <c>.mer.json</c> model.</summary>
internal sealed class MerPrimitive : MerTransform
{
    /// <summary>
    /// Optional one-level empty transform above this primitive. ProjectMER emblem assets use a rotated
    /// child beneath a non-uniformly scaled empty to create a sheared quad. Root-parented primitives and
    /// the plugin's existing flat SCP models leave this null.
    /// </summary>
    public MerTransform? ParentTransform { get; set; }

    public PrimitiveType Type { get; set; } = PrimitiveType.Cube;

    public PrimitiveFlags Flags { get; set; } = PrimitiveFlags.Visible;

    public Color Color { get; set; } = Color.white;

    public bool IsMarker => Flags == PrimitiveFlags.None ||
        Name.StartsWith("marker_", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Minimal reader for the ProjectMER block <c>.mer.json</c> format the tools/build_scp_*_asset.py
/// scripts emit. Models are embedded in the plugin assembly so they ship inside the DLL (no external
/// content to deploy). Vendored on purpose - this standalone plugin carries its own copy.
/// </summary>
internal static class MerModelLoader
{
    /// <summary>Loads the first embedded resource whose name ends with <paramref name="endsWith"/>.</summary>
    public static List<MerPrimitive> LoadEmbedded(string endsWith)
    {
        try
        {
            Assembly assembly = typeof(MerModelLoader).Assembly;
            string? resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
            if (resource == null)
            {
                Logger.Warn($"[WarmupScpSelector] Embedded model '{endsWith}' not found; available: {string.Join(", ", assembly.GetManifestResourceNames())}");
                return new List<MerPrimitive>();
            }

            using Stream? stream = assembly.GetManifestResourceStream(resource);
            if (stream == null)
            {
                return new List<MerPrimitive>();
            }

            using StreamReader reader = new(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            Logger.Error($"[WarmupScpSelector] Failed to load embedded model '{endsWith}': {ex.GetBaseException().Message}");
            return new List<MerPrimitive>();
        }
    }

    public static List<MerPrimitive> Parse(string json)
    {
        List<MerPrimitive> primitives = new();
        if (MiniJson.Parse(json) is not Dictionary<string, object?> root ||
            !root.TryGetValue("Blocks", out object? blocksValue) ||
            blocksValue is not List<object?> blocks)
        {
            Logger.Warn("[WarmupScpSelector] Model is not a MER block document; no Blocks array found.");
            return primitives;
        }

        int rootObjectId = (int)GetNumber(root, "RootObjectId");
        Dictionary<int, MerTransform> emptyTransforms = new();
        foreach (object? blockValue in blocks)
        {
            if (blockValue is not Dictionary<string, object?> block || GetNumber(block, "BlockType") != 0d)
            {
                continue;
            }

            MerTransform transform = ParseTransform(block);
            if (transform.ObjectId >= 0 && !emptyTransforms.ContainsKey(transform.ObjectId))
            {
                emptyTransforms.Add(transform.ObjectId, transform);
            }
        }

        foreach (object? blockValue in blocks)
        {
            if (blockValue is not Dictionary<string, object?> block || GetNumber(block, "BlockType") != 1d)
            {
                continue;
            }

            MerPrimitive primitive = new()
            {
                Name = GetName(block),
                ObjectId = (int)GetNumber(block, "ObjectId", -1d),
                ParentId = (int)GetNumber(block, "ParentId", -1d),
                Position = GetVector(block, "Position"),
                Rotation = GetVector(block, "Rotation"),
                Scale = GetVector(block, "Scale", Vector3.one),
            };

            // A document root is only a container and was historically ignored by this loader. Preserve
            // that behavior for the flat SCP assets, but retain a non-root empty parent so the runtime can
            // reproduce the one-level TRS shear used by converted logo schematics.
            if (primitive.ParentId != rootObjectId &&
                emptyTransforms.TryGetValue(primitive.ParentId, out MerTransform? parent) &&
                parent.ParentId == rootObjectId)
            {
                primitive.ParentTransform = parent;
            }

            if (block.TryGetValue("Properties", out object? propertiesValue) &&
                propertiesValue is Dictionary<string, object?> properties)
            {
                double primitiveType = GetNumber(properties, "PrimitiveType", (double)(int)PrimitiveType.Cube);
                if (primitiveType >= 0d && primitiveType <= (double)(int)PrimitiveType.Quad)
                {
                    primitive.Type = (PrimitiveType)(int)primitiveType;
                }

                primitive.Flags = (PrimitiveFlags)(int)GetNumber(properties, "PrimitiveFlags", (double)(int)PrimitiveFlags.Visible);
                if (properties.TryGetValue("Color", out object? colorValue) &&
                    colorValue is string colorText &&
                    ColorUtility.TryParseHtmlString(colorText.Trim(), out Color color))
                {
                    primitive.Color = color;
                }
            }

            primitives.Add(primitive);
        }

        return primitives;
    }

    private static MerTransform ParseTransform(Dictionary<string, object?> block)
    {
        return new MerTransform
        {
            Name = GetName(block),
            ObjectId = (int)GetNumber(block, "ObjectId", -1d),
            ParentId = (int)GetNumber(block, "ParentId", -1d),
            Position = GetVector(block, "Position"),
            Rotation = GetVector(block, "Rotation"),
            Scale = GetVector(block, "Scale", Vector3.one),
        };
    }

    private static string GetName(Dictionary<string, object?> block)
    {
        return block.TryGetValue("Name", out object? nameValue) && nameValue is string name
            ? name
            : string.Empty;
    }

    private static Vector3 GetVector(Dictionary<string, object?> block, string key, Vector3 fallback = default)
    {
        if (!block.TryGetValue(key, out object? value) || value is not Dictionary<string, object?> vector)
        {
            return fallback;
        }

        return new Vector3(
            (float)GetNumber(vector, "x"),
            (float)GetNumber(vector, "y"),
            (float)GetNumber(vector, "z"));
    }

    private static double GetNumber(Dictionary<string, object?> map, string key, double fallback = 0d)
    {
        return map.TryGetValue(key, out object? value) && value is double number ? number : fallback;
    }

    /// <summary>
    /// Tiny JSON reader (objects, arrays, strings, numbers, bools, null) so the plugin does not
    /// need a JSON assembly the server's Managed folder does not ship.
    /// </summary>
    private static class MiniJson
    {
        public static object? Parse(string text)
        {
            int index = 0;
            return ParseValue(text, ref index);
        }

        private static object? ParseValue(string text, ref int index)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                throw new FormatException("Unexpected end of JSON.");
            }

            char c = text[index];
            return c switch
            {
                '{' => ParseObject(text, ref index),
                '[' => ParseArray(text, ref index),
                '"' => ParseString(text, ref index),
                't' => ParseLiteral(text, ref index, "true", true),
                'f' => ParseLiteral(text, ref index, "false", false),
                'n' => ParseLiteral(text, ref index, "null", null),
                _ => ParseNumber(text, ref index),
            };
        }

        private static Dictionary<string, object?> ParseObject(string text, ref int index)
        {
            Dictionary<string, object?> result = new(StringComparer.Ordinal);
            index++;
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == '}')
            {
                index++;
                return result;
            }

            while (index < text.Length)
            {
                SkipWhitespace(text, ref index);
                string key = ParseString(text, ref index);
                SkipWhitespace(text, ref index);
                Expect(text, ref index, ':');
                result[key] = ParseValue(text, ref index);
                SkipWhitespace(text, ref index);
                if (index < text.Length && text[index] == ',')
                {
                    index++;
                    continue;
                }

                Expect(text, ref index, '}');
                return result;
            }

            throw new FormatException("Unterminated JSON object.");
        }

        private static List<object?> ParseArray(string text, ref int index)
        {
            List<object?> result = new();
            index++;
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == ']')
            {
                index++;
                return result;
            }

            while (index < text.Length)
            {
                result.Add(ParseValue(text, ref index));
                SkipWhitespace(text, ref index);
                if (index < text.Length && text[index] == ',')
                {
                    index++;
                    continue;
                }

                Expect(text, ref index, ']');
                return result;
            }

            throw new FormatException("Unterminated JSON array.");
        }

        private static string ParseString(string text, ref int index)
        {
            Expect(text, ref index, '"');
            StringBuilder builder = new();
            while (index < text.Length)
            {
                char c = text[index++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= text.Length)
                {
                    break;
                }

                char escape = text[index++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        builder.Append((char)ushort.Parse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        index += 4;
                        break;
                    default:
                        throw new FormatException($"Unsupported JSON escape '\\{escape}'.");
                }
            }

            throw new FormatException("Unterminated JSON string.");
        }

        private static object ParseNumber(string text, ref int index)
        {
            int start = index;
            while (index < text.Length && (char.IsDigit(text[index]) || text[index] is '-' or '+' or '.' or 'e' or 'E'))
            {
                index++;
            }

            string token = text.Substring(start, index - start);
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : throw new FormatException($"Invalid JSON number '{token}'.");
        }

        private static object? ParseLiteral(string text, ref int index, string literal, object? value)
        {
            if (string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
            {
                throw new FormatException($"Invalid JSON literal at index {index}.");
            }

            index += literal.Length;
            return value;
        }

        private static void Expect(string text, ref int index, char expected)
        {
            if (index >= text.Length || text[index] != expected)
            {
                throw new FormatException($"Expected '{expected}' at JSON index {index}.");
            }

            index++;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }
    }
}
