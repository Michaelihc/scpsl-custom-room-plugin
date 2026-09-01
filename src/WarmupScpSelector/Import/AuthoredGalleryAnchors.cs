using System;
using System.Collections.Generic;
using System.Text;
using PlayerRoles;
using UnityEngine;
using WarmupScpSelector.Export;
using WarmupScpSelector.Warmup;

namespace WarmupScpSelector.Import;

/// <summary>
/// Recovers each SCP display's selection-coin anchor from an authored station schematic.
///
/// The coins are the one part of the gallery code must always spawn itself: a coin's pickup serial is
/// what binds it to an SCP role, so a static copy in the asset would look right and select nothing.
/// Everything the coin sits in front of - stand, model, label - comes from the asset. Those two halves
/// only line up while the live config still produces the same slot grid the asset was exported from,
/// and it does not have to. Raising <c>PedestalSpacing</c> from 3.7 to 4.0 pushes the outermost back-rank
/// stand past the gallery's usable width, so the rank drops from six stands to four, the remainder fall
/// through to the side ranks, and three coins end up six metres from the exhibit they select. The room
/// still looks correct: the labels are authored, so a player reads "SCP-096", grabs the nearest coin,
/// and drafts SCP-173.
///
/// So the anchor is taken from the exhibit's own LABEL. The exporter writes it as ordinary schematic
/// text and it names the SCP, which makes the coin follow the exhibit by identity instead of by
/// arithmetic - and keeps following it after an author drags a stand somewhere else in the map editor.
/// Anything that cannot be matched confidently falls back to the computed slot, so a schematic with no
/// labels behaves exactly as before rather than losing its coins.
/// </summary>
internal static class AuthoredGalleryAnchors
{
    /// <summary>
    /// One anchor per option, in option order; <c>null</c> where the asset offers no confident match.
    /// </summary>
    public static IReadOnlyList<Vector3?> Resolve(
        StationAsset? asset,
        WarmupHallLayout hall,
        IReadOnlyList<ScpOption> options)
    {
        Vector3?[] anchors = new Vector3?[options?.Count ?? 0];
        if (asset == null || hall == null || options == null || options.Count == 0)
        {
            return anchors;
        }

        List<(string Text, Vector3 Local)> labels = GalleryLabels(asset);
        if (labels.Count == 0)
        {
            return anchors;
        }

        for (int i = 0; i < options.Count; i++)
        {
            ScpOption option = options[i];
            if (option == null)
            {
                continue;
            }

            if (TryMatch(labels, Normalise(option.Label), out Vector3 local) ||
                TryMatch(labels, RoleCode(option.Role), out local))
            {
                anchors[i] = hall.CoinAnchor(local.x, local.z);
            }
        }

        return anchors;
    }

    /// <summary>Every text block standing inside the gallery, reduced to its readable content.</summary>
    private static List<(string Text, Vector3 Local)> GalleryLabels(StationAsset asset)
    {
        List<(string, Vector3)> labels = new();
        foreach (StationAssetBlock block in asset.Blocks)
        {
            if (block.BlockType != SchematicBlockType.Text)
            {
                continue;
            }

            Vector3 local = block.ApproximateRootPosition;
            if (local.z < WarmupHallLayout.GalleryFarZ || local.z > WarmupHallLayout.ConnectorFarZ)
            {
                continue; // signage in another compartment cannot name a gallery exhibit
            }

            string text = Normalise(block.Text("Text", string.Empty));
            if (text.Length == 0 || text.Length > 32)
            {
                continue; // the welcome banner is prose, not an exhibit label
            }

            labels.Add((text, local));
        }

        return labels;
    }

    /// <summary>A single unambiguous label match; two candidates are treated as none.</summary>
    private static bool TryMatch(List<(string Text, Vector3 Local)> labels, string needle, out Vector3 local)
    {
        local = Vector3.zero;
        if (needle.Length < 3)
        {
            return false;
        }

        int found = -1;
        for (int i = 0; i < labels.Count; i++)
        {
            if (labels[i].Text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (found >= 0)
            {
                return false;
            }

            found = i;
        }

        if (found < 0)
        {
            return false;
        }

        local = labels[found].Local;
        return true;
    }

    /// <summary>"Scp3114" -> "3114": the digits an authored label carries whatever language it is in.</summary>
    private static string RoleCode(RoleTypeId role)
    {
        string name = role.ToString();
        StringBuilder digits = new(name.Length);
        foreach (char c in name)
        {
            if (c >= '0' && c <= '9')
            {
                digits.Append(c);
            }
        }

        return digits.ToString();
    }

    /// <summary>Strips TMP markup and collapses whitespace so authored text compares like plain text.</summary>
    private static string Normalise(string? markup)
    {
        if (string.IsNullOrWhiteSpace(markup))
        {
            return string.Empty;
        }

        StringBuilder text = new(markup!.Length);
        bool inTag = false;
        bool pendingSpace = false;
        foreach (char c in markup)
        {
            if (c == '<')
            {
                inTag = true;
                continue;
            }

            if (inTag)
            {
                inTag = c != '>';
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = text.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                text.Append(' ');
                pendingSpace = false;
            }

            text.Append(c);
        }

        return text.ToString();
    }
}
