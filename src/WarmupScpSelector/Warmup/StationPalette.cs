using UnityEngine;

namespace WarmupScpSelector.Warmup;

/// <summary>
/// One 莺歌傲然 orbital-station theme shared by every warmup builder (hall shell, SCP gallery, Aim
/// range furniture, parkour shaft). The station reads as cold graphite structure lit by near-white
/// deck lighting, with brand cyan/teal used only for wayfinding and gold reserved for brand text.
///
/// Two rules keep the look coherent — break them and the room stops reading as one place:
/// 1. Point lights stay NEAR-WHITE. The SCP models are muted primitives; a tinted light washes them
///    out. Colour comes from the surfaces, never from the lamps.
/// 2. Structure is desaturated (deck / bulkhead / overhead). Saturation is a signal: teal = route,
///    amber = caution, green = go, red = stop, gold = brand.
///
/// Source of truth for the brand hues: scpsl-plugins-metarepo\.server\brand\server-identity-preview.html.
/// </summary>
internal static class StationPalette
{
    // ---- Structure -------------------------------------------------------------------------
    /// <summary>Deck plating players walk on.</summary>
    public static readonly Color Deck = Hex("#20262E");

    /// <summary>Raised deck panel, one step lighter than <see cref="Deck"/> for plate joints.</summary>
    public static readonly Color DeckPanel = Hex("#2A313B");

    /// <summary>Wall bulkhead panelling.</summary>
    public static readonly Color Bulkhead = Hex("#2E3641");

    /// <summary>Recessed rib between bulkhead panels.</summary>
    public static readonly Color BulkheadRib = Hex("#171C23");

    /// <summary>Overhead (ceiling) plating; darkest structure so the room never feels capped by fog.</summary>
    public static readonly Color Overhead = Hex("#12161C");

    /// <summary>Structural frame around openings and along load-bearing edges.</summary>
    public static readonly Color Frame = Hex("#3A434F");

    // ---- Glass / viewports -----------------------------------------------------------------
    /// <summary>Half-transparent partition glass, matching the authored room's translucent inner walls.</summary>
    public static readonly Color Glass = new(0.62f, 0.80f, 0.92f, 0.28f);

    /// <summary>Viewport void behind a window: near-black so star quads read against it.</summary>
    public static readonly Color Void = Hex("#05070C");

    /// <summary>Distant star / running-light speck.</summary>
    public static readonly Color Star = Hex("#DCEBFF");

    // ---- Signals ---------------------------------------------------------------------------
    /// <summary>Brand teal. Wayfinding strips, route markers, active seams.</summary>
    public static readonly Color Guide = Hex("#33EEDA");

    /// <summary>Brand cyan. Secondary wayfinding and zone identity.</summary>
    public static readonly Color Cyan = Hex("#4FCBFF");

    /// <summary>Amber caution striping at thresholds and drops.</summary>
    public static readonly Color Caution = Hex("#FFB020");

    /// <summary>Go / finish / success green.</summary>
    public static readonly Color Signal = Hex("#5BFF80");

    /// <summary>Stop / hazard red.</summary>
    public static readonly Color Alert = Hex("#FF5555");

    /// <summary>Brand gold, used for supporting text only.</summary>
    public static readonly Color Gold = Hex("#FFD24D");

    // ---- Lighting --------------------------------------------------------------------------
    /// <summary>Near-white deck lighting. Never tint this: the SCP models must render true.</summary>
    public static readonly Color DeckLight = Hex("#F2F5FA");

    /// <summary>Neutral placeholder block for an SCP whose model asset cannot be resolved.</summary>
    public static readonly Color Placeholder = Hex("#5A6472");

    private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.magenta;
}
