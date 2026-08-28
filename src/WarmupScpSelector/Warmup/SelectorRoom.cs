using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using MapGeneration;
using PlayerRoles;
using UnityEngine;
using WarmupScpSelector.Models;
using Logger = LabApi.Features.Console.Logger;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace WarmupScpSelector.Warmup;

/// <summary>
/// Builds and tears down the warmup station: the pressurised shell from <see cref="WarmupHallLayout"/>,
/// the SCP draft gallery (one stand, model, label, and coin per offered SCP), the branded back wall, and
/// the observation deck's viewport.
///
/// The station floats just above the static surface zone (resolved at build time). SCP:SL collision and
/// physics get unreliable at extreme coordinates, so anchoring to the real, already-loaded surface region
/// is what keeps a primitive deck actually walkable instead of something players fall straight through.
/// </summary>
public sealed class SelectorRoom
{
    private const int MaxOptions = 16;
    private const float StandWidthCap = 1.9f;
    private const float LogoScale = 0.39f;
    private const float LogoHdrBoost = 4.5f;
    private const float LogoHdrAlpha = 0.65f;

    /// <summary>
    /// The logo's resolved bounds are 6.419467 x 7.950564; centring around this authored-space point makes
    /// its visual bounds land exactly on the requested wall anchor.
    /// </summary>
    private static readonly Vector3 LogoAssetCenter = new(-0.003987f, 0.291070f, -0.08f);

    /// <summary>Back-wall world text below the logo. Language-independent brand copy; GB2312-safe, no emoji.</summary>
    private const string BannerMarkup =
        "<align=center><size=135%><b>" +
        "<color=#6BFF6B>莺</color><color=#2DFFBE>歌</color><color=#3CE2E7>傲</color><color=#4FCBFF>然</color>" +
        "</b></size>\n<size=46%><color=#FFE08A>祝你玩得愉快　·　欢迎加入 QQ 群 860705092</color></size></align>";

    private static readonly Dictionary<RoleTypeId, string> BuiltInModels = new()
    {
        [RoleTypeId.Scp049] = "scp-049",
        [RoleTypeId.Scp079] = "scp-079",
        [RoleTypeId.Scp096] = "scp-096",
        [RoleTypeId.Scp106] = "scp-106",
        [RoleTypeId.Scp173] = "scp-173",
        [RoleTypeId.Scp939] = "scp-939",
        [RoleTypeId.Scp3114] = "scp-3114",
    };

    private readonly WarmupScpSelectorPlugin _plugin;
    private readonly List<AdminToy> _toys = new();
    private readonly List<Pickup> _pickups = new();
    private readonly List<ScpOption> _offered = new();
    private PrimitiveObjectToy? _aimDoorGate;
    private PrimitiveObjectToy? _parkourDoorGate;

    public SelectorRoom(WarmupScpSelectorPlugin plugin)
    {
        _plugin = plugin;
    }

    private Config Config => _plugin.Config;

    private bool UseChinese => string.Equals(Config.Language, "cn", StringComparison.OrdinalIgnoreCase);

    /// <summary>Coin pickup serial -> the SCP role it selects.</summary>
    public Dictionary<ushort, RoleTypeId> CoinRoles { get; } = new();

    /// <summary>The SCP options actually built into the gallery, in stand order.</summary>
    public IReadOnlyList<ScpOption> OfferedOptions => _offered;

    /// <summary>Resolved station layout for the current build; activity lanes anchor off this.</summary>
    public WarmupHallLayout? Hall { get; private set; }

    /// <summary>Where warmup players are teleported to (and spawned).</summary>
    public Vector3 SpawnPosition { get; private set; }

    /// <summary>Horizontal spawn rotation in degrees, so arrivals face the SCP stands.</summary>
    public float SpawnYaw { get; private set; }

    /// <summary>Station deck centre for the current build.</summary>
    public Vector3 Origin { get; private set; }

    /// <summary>Whether the Aim Bay hatch was sealed for this warmup and is waiting to be opened.</summary>
    public bool AimRangeDoorPrepared { get; private set; }

    /// <summary>Whether the parkour shaft hatch was sealed for this warmup and is waiting to be opened.</summary>
    public bool ParkourDoorPrepared { get; private set; }

    public bool IsSpawned { get; private set; }

    public void Build()
    {
        Despawn();

        Vector3 origin = ResolveOrigin();
        WarmupHallLayout hall = new(origin);
        Hall = hall;
        Origin = origin;
        SpawnPosition = hall.SpawnPosition;
        SpawnYaw = hall.SpawnYaw;

        // Valid, distinct SCP options only: skip nulls/non-SCP, collapse duplicate roles so each SCP gets
        // exactly one stand + coin, and cap the count so a pathological config cannot flood the gallery.
        List<ScpOption> options = (Config.ScpOptions ?? new List<ScpOption>())
            .Where(o => o != null && ScpOption.IsScpRole(o.Role))
            .GroupBy(o => o.Role)
            .Select(g => g.First())
            .Take(MaxOptions)
            .ToList();

        float spacing = Sanitize(Config.PedestalSpacing, 2.4f, 6f, 3.7f);
        float standWidth = Mathf.Min(spacing * 0.7f, StandWidthCap);
        IReadOnlyList<GalleryDisplaySlot> slots = hall.BuildDisplaySlots(options.Count, spacing, standWidth);
        if (slots.Count < options.Count)
        {
            Logger.Warn(
                $"[WarmupScpSelector] Gallery fits {slots.Count} stands but {options.Count} SCP options are configured; " +
                "the extras were dropped. Reduce PedestalSpacing or ScpOptions.");
            options = options.Take(slots.Count).ToList();
        }

        _offered.AddRange(options); // single source of truth for the live status-panel chip row

        StationShellBuilder shell = new(hall, _toys, UseChinese);
        shell.Build();

        BuildGallery(hall, shell, options, slots);
        BuildObservationViewport(hall, shell);
        PrepareHatchGates(hall);

        IsSpawned = true;
        _plugin.LogDebug($"Built warmup station at {origin}: {_toys.Count} toys, {_pickups.Count} coins, {slots.Count} stands.");
    }

    // ---- Gallery -------------------------------------------------------------------------------

    private void BuildGallery(
        WarmupHallLayout hall,
        StationShellBuilder shell,
        IReadOnlyList<ScpOption> options,
        IReadOnlyList<GalleryDisplaySlot> slots)
    {
        SpawnLogo(hall.LogoAnchor, LogoScale);
        AddBanner(hall.BannerAnchor);

        float modelScale = Sanitize(Config.ModelScale, 0.05f, 10f, 1f);
        float coinScale = Sanitize(Config.SelectorCoinScale, 1f, 20f, 6f);

        for (int i = 0; i < options.Count; i++)
        {
            ScpOption option = options[i];
            GalleryDisplaySlot slot = slots[i];

            // A low collidable plinth. The models themselves are visible-only primitives, so without it a
            // player just walks through the exhibit.
            shell.AddBox(
                new Vector3(slot.StandTopCenter.x, slot.StandTopCenter.y - WarmupHallLayout.StandHeight / 2f, slot.StandTopCenter.z),
                new Vector3(slot.StandWidth, WarmupHallLayout.StandHeight, slot.StandDepth),
                StationPalette.DeckPanel,
                collidable: true);
            shell.AddBox(
                new Vector3(slot.StandTopCenter.x, slot.StandTopCenter.y + 0.02f, slot.StandTopCenter.z),
                new Vector3(slot.StandWidth - 0.1f, 0.03f, slot.StandDepth - 0.1f),
                StationPalette.Cyan,
                collidable: false);

            float modelTop = SpawnModel(ResolveModelName(option), slot.StandTopCenter, slot.Facing, modelScale);
            AddLabel(new Vector3(slot.StandTopCenter.x, slot.StandTopCenter.y + modelTop + 0.45f, slot.StandTopCenter.z), option.Label);

            // Big coin floating clearly IN FRONT of the model and frozen, so it cannot fall, roll, or clip
            // into the exhibit the way a resting pickup did.
            Pickup? coin = Pickup.Create(
                Config.SelectorItem,
                slot.CoinPosition,
                Quaternion.Euler(90f, 0f, 0f),
                Vector3.one * coinScale,
                networkSpawn: false);
            if (coin == null)
            {
                continue;
            }

            _pickups.Add(coin); // track before spawn so a throw mid-setup is still cleaned up by Despawn
            CoinRoles[coin.Serial] = option.Role;
            coin.IsLocked = false;
            coin.Spawn();
            try
            {
                if (coin.Rigidbody != null)
                {
                    coin.Rigidbody.isKinematic = true; // freeze in place: no gravity, no roll, no clip
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Could not freeze coin: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The observation deck's reason to exist: a dark viewport with a deterministic star field behind it.
    /// Purely decorative, and cheap - one dark plate plus a fixed handful of specks.
    /// </summary>
    private void BuildObservationViewport(WarmupHallLayout hall, StationShellBuilder shell)
    {
        StationZone deck = hall.ObservationDeck;
        float wallX = deck.MinX + 0.32f;

        shell.AddBox(hall.World(wallX, 2.6f, deck.CenterZ), new Vector3(0.12f, 3.4f, 14f), StationPalette.Void, collidable: false);

        // Deterministic scatter: a fixed generator so the view is identical every warmup and in tests.
        System.Random random = new System.Random(20260828);
        for (int i = 0; i < 46; i++)
        {
            float z = deck.CenterZ + (float)(random.NextDouble() - 0.5d) * 13f;
            float y = 1.1f + (float)random.NextDouble() * 3f;
            float size = 0.04f + (float)random.NextDouble() * 0.05f;
            shell.AddBox(hall.World(wallX + 0.08f, y, z), new Vector3(0.02f, size, size), StationPalette.Star, collidable: false);
        }

        // Frame the viewport so it reads as a window rather than a hole in the wall.
        foreach (float edge in new[] { -1.75f, 1.75f })
        {
            shell.AddBox(hall.World(wallX + 0.1f, 2.6f + edge * 1.0f, deck.CenterZ), new Vector3(0.1f, 0.16f, 14.3f), StationPalette.Frame, collidable: false);
        }

        shell.AddLabel(
            hall.World(deck.MinX + 0.6f, 4.35f, deck.CenterZ),
            UseChinese ? "<color=#4FCBFF>观景舱</color>" : "<color=#4FCBFF>OBSERVATION</color>",
            Quaternion.Euler(0f, -90f, 0f),
            380f);
    }

    // ---- Hatch gates ---------------------------------------------------------------------------

    /// <summary>
    /// Seals the activity compartments during setup. Each lane is fail-closed: its hatch panel is removed
    /// only once that lane's world, props, and scheduler have all started successfully, so a half-built
    /// range or an unvalidated parkour route is never reachable.
    /// </summary>
    private void PrepareHatchGates(WarmupHallLayout hall)
    {
        if (Config.ActivitiesEnabled && Config.Activities?.Aim?.Enabled == true)
        {
            _aimDoorGate = BuildHatchGate(hall, hall.AimBay);
            AimRangeDoorPrepared = true;
        }

        if (Config.ActivitiesEnabled && Config.Activities?.Parkour?.Enabled == true)
        {
            _parkourDoorGate = BuildHatchGate(hall, hall.ParkourShaft);
            ParkourDoorPrepared = true;
        }
    }

    private PrimitiveObjectToy BuildHatchGate(WarmupHallLayout hall, StationZone zone)
    {
        (Vector3 center, Vector3 size) = HatchGateGeometry(hall, zone);
        PrimitiveObjectToy gate = PrimitiveObjectToy.Create(center, Quaternion.identity, size, networkSpawn: false);
        _toys.Add(gate);
        gate.Type = PrimitiveType.Cube;
        gate.Color = StationPalette.Bulkhead;
        gate.Flags = PrimitiveFlags.Visible | PrimitiveFlags.Collidable;
        gate.IsStatic = true;
        gate.Spawn();
        return gate;
    }

    private static (Vector3 Center, Vector3 Size) HatchGateGeometry(WarmupHallLayout hall, StationZone zone)
    {
        if (zone.Id == "aim")
        {
            return (hall.World(zone.MinX, WarmupHallLayout.HatchHeight / 2f, zone.CenterZ),
                new Vector3(0.35f, WarmupHallLayout.HatchHeight, zone.Depth));
        }

        return (hall.World(zone.CenterX, WarmupHallLayout.HatchHeight / 2f, zone.MinZ),
            new Vector3(zone.Width, WarmupHallLayout.HatchHeight, 0.35f));
    }

    public bool OpenAimRangeDoor() => OpenGate(ref _aimDoorGate, AimRangeDoorPrepared, "Aim Bay");

    public void CloseAimRangeDoor()
    {
        if (AimRangeDoorPrepared && _aimDoorGate == null && IsSpawned && Hall != null)
        {
            _aimDoorGate = BuildHatchGate(Hall, Hall.AimBay);
        }
    }

    public bool OpenParkourDoor() => OpenGate(ref _parkourDoorGate, ParkourDoorPrepared, "parkour shaft");

    public void CloseParkourDoor()
    {
        if (ParkourDoorPrepared && _parkourDoorGate == null && IsSpawned && Hall != null)
        {
            _parkourDoorGate = BuildHatchGate(Hall, Hall.ParkourShaft);
        }
    }

    private static bool OpenGate(ref PrimitiveObjectToy? gate, bool prepared, string label)
    {
        if (!prepared || gate == null)
        {
            return false;
        }

        try
        {
            if (!gate.IsDestroyed)
            {
                gate.Destroy();
            }

            gate = null;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector] Could not open {label} hatch: {ex.Message}");
            return false;
        }
    }

    // ---- Teardown ------------------------------------------------------------------------------

    public void Despawn()
    {
        foreach (Pickup pickup in _pickups)
        {
            try
            {
                pickup.Destroy();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Failed to destroy coin: {ex.Message}");
            }
        }

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
                Logger.Warn($"[WarmupScpSelector] Failed to destroy toy: {ex.Message}");
            }
        }

        _toys.Clear();
        _pickups.Clear();
        CoinRoles.Clear();
        _offered.Clear();
        _aimDoorGate = null;
        _parkourDoorGate = null;
        AimRangeDoorPrepared = false;
        ParkourDoorPrepared = false;
        Hall = null;
        IsSpawned = false;
    }

    // ---- Models and signage --------------------------------------------------------------------

    /// <summary>Spawns one model's primitives at <paramref name="baseWorld"/>; returns its height above that base.</summary>
    private float SpawnModel(string modelName, Vector3 baseWorld, Quaternion facing, float scale)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return SpawnPlaceholderCube(baseWorld, facing, scale);
        }

        List<MerPrimitive> primitives = MerModelLoader.LoadEmbedded(modelName + ".mer.json");
        if (primitives.Count == 0)
        {
            return SpawnPlaceholderCube(baseWorld, facing, scale);
        }

        float topY = 0f;
        foreach (MerPrimitive primitive in primitives)
        {
            if (primitive.IsMarker)
            {
                continue;
            }

            PrimitiveObjectToy toy = PrimitiveObjectToy.Create(
                baseWorld + facing * (primitive.Position * scale),
                facing * Quaternion.Euler(primitive.Rotation),
                primitive.Scale * scale,
                networkSpawn: false);
            _toys.Add(toy); // track before configure/spawn so a throw mid-setup is still cleaned up
            toy.Type = primitive.Type;
            toy.Color = primitive.Color;
            toy.Flags = PrimitiveFlags.Visible;
            toy.IsStatic = true;
            toy.Spawn();

            topY = Mathf.Max(topY, (primitive.Position.y + primitive.Scale.y / 2f) * scale);
        }

        return topY > 0f ? topY : 1.8f;
    }

    /// <summary>
    /// Spawns the embedded wall logo. Most of its quads use ProjectMER's stock TRS-shear trick: an
    /// invisible, non-uniformly scaled primitive parent plus a rotated visible child. The parent is
    /// world-baked, leaving only one replicated parent link (child -> shear parent), which avoids a
    /// root -> parent -> child resolution race for players joining during warmup.
    /// </summary>
    private void SpawnLogo(Vector3 centerWorld, float scale)
    {
        List<MerPrimitive> primitives = MerModelLoader.LoadEmbedded("yingge-aoran-logo-opt.mer.json");
        Dictionary<int, PrimitiveObjectToy> parents = new();

        foreach (MerPrimitive primitive in primitives)
        {
            if (primitive.IsMarker)
            {
                continue;
            }

            if (primitive.ParentTransform is MerTransform parentTransform)
            {
                if (!parents.TryGetValue(parentTransform.ObjectId, out PrimitiveObjectToy parent))
                {
                    parent = PrimitiveObjectToy.Create(
                        centerWorld + (parentTransform.Position - LogoAssetCenter) * scale,
                        Quaternion.Euler(parentTransform.Rotation),
                        parentTransform.Scale * scale,
                        networkSpawn: false);
                    _toys.Add(parent);
                    parent.Type = PrimitiveType.Quad;
                    parent.Color = Color.clear;
                    parent.Flags = PrimitiveFlags.None;
                    parent.IsStatic = true;
                    parent.Spawn();
                    parents.Add(parentTransform.ObjectId, parent);
                }

                PrimitiveObjectToy child = PrimitiveObjectToy.Create(
                    primitive.Position,
                    Quaternion.Euler(primitive.Rotation),
                    primitive.Scale,
                    parent.Transform,
                    networkSpawn: false);
                _toys.Add(child);
                child.Type = primitive.Type;
                child.Color = LogoHdrColor(primitive.Color);
                child.Flags = primitive.Flags;
                child.IsStatic = true;
                child.Spawn();
                continue;
            }

            PrimitiveObjectToy toy = PrimitiveObjectToy.Create(
                centerWorld + (primitive.Position - LogoAssetCenter) * scale,
                Quaternion.Euler(primitive.Rotation),
                primitive.Scale * scale,
                networkSpawn: false);
            _toys.Add(toy);
            toy.Type = primitive.Type;
            toy.Color = LogoHdrColor(primitive.Color);
            toy.Flags = primitive.Flags;
            toy.IsStatic = true;
            toy.Spawn();
        }

        // The gallery's own deck lights carry the logo; unclamped translucent base colors restore its
        // bloom without spending a dedicated light toy on it.
    }

    /// <summary>Neutral fallback block for custom/future SCPs whose model cannot be resolved.</summary>
    private float SpawnPlaceholderCube(Vector3 baseWorld, Quaternion facing, float scale)
    {
        float height = 1.7f * scale;
        PrimitiveObjectToy cube = PrimitiveObjectToy.Create(
            baseWorld + new Vector3(0f, height / 2f, 0f),
            facing,
            new Vector3(1.1f, 1.7f, 1.1f) * scale,
            networkSpawn: false);
        _toys.Add(cube);
        cube.Type = PrimitiveType.Cube;
        cube.Color = StationPalette.Placeholder;
        cube.Flags = PrimitiveFlags.Visible;
        cube.IsStatic = true;
        cube.Spawn();
        return height;
    }

    /// <summary>Exhibit label. Identity rotation faces the arrival aisle at -Z.</summary>
    private void AddLabel(Vector3 center, string label)
    {
        TextToy text = TextToy.Create(center, Quaternion.identity, Vector3.one * 0.2f, networkSpawn: false);
        _toys.Add(text);
        text.TextFormat = $"<align=center><b>{label}</b></align>";
        text.DisplaySize = new Vector2(220f, 40f);
        text.IsStatic = true;
        text.Spawn();
    }

    private void AddBanner(Vector3 center)
    {
        TextToy text = TextToy.Create(center, Quaternion.identity, Vector3.one * 0.45f, networkSpawn: false);
        _toys.Add(text);
        text.TextFormat = BannerMarkup;
        text.DisplaySize = new Vector2(1000f, 200f);
        text.IsStatic = true;
        text.Spawn();
    }

    private static string ResolveModelName(ScpOption option)
    {
        if (!string.IsNullOrWhiteSpace(option.Model))
        {
            return option.Model.Trim();
        }

        return BuiltInModels.TryGetValue(option.Role, out string modelName) ? modelName : string.Empty;
    }

    private static Color LogoHdrColor(Color authored) => new(
        authored.r * LogoHdrBoost,
        authored.g * LogoHdrBoost,
        authored.b * LogoHdrBoost,
        LogoHdrAlpha);

    /// <summary>
    /// Anchor the station just above the static surface zone - sane, already-loaded coordinates where a
    /// primitive deck is reliably walkable. SCP:SL collision and physics misbehave at extreme or empty
    /// coordinates (a hand-picked (0, 1000, 0) is exactly where a freshly placed player fell through).
    /// </summary>
    private Vector3 ResolveOrigin()
    {
        try
        {
            Room? surface = Room.Get(FacilityZone.Surface).FirstOrDefault();
            if (surface != null)
            {
                return surface.Position + new Vector3(0f, Sanitize(Config.SurfaceClearance, 5f, 80f, 20f), 0f);
            }

            Logger.Warn("[WarmupScpSelector] Surface zone not found; using fallback RoomOrigin.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector] Could not resolve surface origin: {ex.Message}");
        }

        return SanitizeOrigin(Config.RoomOrigin, new Vector3(0f, 1015f, 0f));
    }

    private static float Sanitize(float value, float min, float max, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

    private static Vector3 SanitizeOrigin(Vector3 value, Vector3 fallback)
    {
        bool finite =
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        return finite ? value : fallback;
    }
}
