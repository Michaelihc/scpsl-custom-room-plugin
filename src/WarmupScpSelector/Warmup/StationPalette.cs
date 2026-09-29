using UnityEngine;

namespace WarmupScpSelector.Warmup;

/// <summary>
/// One 莺歌傲然 orbital-station theme shared by every warmup builder (hall shell, SCP gallery, Aim
/// range furniture, parkour shaft).
///
/// The station is WHITE: clean near-white panelling with grey structural recesses, the way a real
/// station interior reads. Three rules keep it coherent - break them and the room stops working:
///
/// 1. Structure stays light. SCP:SL has no global illumination, so a surface's colour IS its
///    brightness; a dark palette makes the room read as a black void no matter how many lights are
///    added. Definition comes from grey ribs and seams against white panels, never from darkening
///    the panels themselves.
/// 2. Point lights stay NEAR-WHITE. The SCP models are muted primitives and a tinted lamp washes
///    them out. Colour comes from the surfaces, never from the lamps.
/// 3. Accents are darker than the ground they sit on. On a white station the signal colours have to
///    be deepened or they vanish; the dark-on-light versions here are the usable ones.
///
/// The one deliberate dark surface is <see cref="DisplayPanel"/>: the brand logo and its gradient
/// wordmark were designed against a near-black ground, so the gallery's feature wall stays dark to
/// keep them legible instead of washing the brand out across a white wall.
///
/// Brand hues: scpsl-plugins-metarepo\.server\brand\server-identity-preview.html.
/// </summary>
internal static class StationPalette
{
    // ---- Structure -------------------------------------------------------------------------
    /// <summary>Deck plating players walk on. Light, but a step below the walls so the floor reads.</summary>
    public static readonly Color Deck = Hex("#D6DBE0");

    /// <summary>Raised deck panel, brighter than <see cref="Deck"/>, for plate joints.</summary>
    public static readonly Color DeckPanel = Hex("#E8ECEF");

    /// <summary>Wall bulkhead panelling: the station's dominant near-white surface.</summary>
    public static readonly Color Bulkhead = Hex("#EDF1F4");

    /// <summary>Recessed rib between bulkhead panels. Grey, not black - this is the only panel definition.</summary>
    public static readonly Color BulkheadRib = Hex("#A9B4BF");

    /// <summary>Overhead plating. Brightest surface so the ceiling reads as a lit deckhead.</summary>
    public static readonly Color Overhead = Hex("#F3F6F8");

    /// <summary>Structural frame around openings and along load-bearing edges.</summary>
    public static readonly Color Frame = Hex("#8D9AA7");

    /// <summary>
    /// Dark feature-wall panel. Used only behind the brand logo and wordmark, which were designed
    /// against a near-black ground and wash out on white.
    /// </summary>
    public static readonly Color DisplayPanel = Hex("#12161F");

    // ---- Glass / viewports -----------------------------------------------------------------
    /// <summary>Half-transparent partition glass.</summary>
    public static readonly Color Glass = new(0.72f, 0.85f, 0.94f, 0.30f);

    /// <summary>Viewport void behind a window: near-black so star specks read against it.</summary>
    public static readonly Color Void = Hex("#05070C");

    /// <summary>Distant star / running-light speck.</summary>
    public static readonly Color Star = Hex("#FFFFFF");

    // ---- Signals (deepened for a light ground) ----------------------------------------------
    /// <summary>Brand teal. Wayfinding strips, route markers, active seams.</summary>
    public static readonly Color Guide = Hex("#00A896");

    /// <summary>Brand cyan. Secondary wayfinding and zone identity.</summary>
    public static readonly Color Cyan = Hex("#1B87C9");

    /// <summary>Amber caution striping at thresholds and drops.</summary>
    public static readonly Color Caution = Hex("#E09612");

    /// <summary>Go / finish / success green.</summary>
    public static readonly Color Signal = Hex("#22A44E");

    /// <summary>Stop / hazard red.</summary>
    public static readonly Color Alert = Hex("#CC3B33");

    /// <summary>Brand gold, for supporting text on the dark feature wall only.</summary>
    public static readonly Color Gold = Hex("#C9A227");

    // ---- Lighting --------------------------------------------------------------------------
    /// <summary>Near-white deck lighting. Never tint this: the SCP models must render true.</summary>
    public static readonly Color DeckLight = Hex("#F5F8FC");

    /// <summary>Neutral placeholder block for an SCP whose model asset cannot be resolved.</summary>
    public static readonly Color Placeholder = Hex("#6B7684");

    private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.magenta;
}
