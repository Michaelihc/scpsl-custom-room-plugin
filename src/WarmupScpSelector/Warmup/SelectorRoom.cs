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
/// Builds and tears down the selector room: a plain floor with low walls, one pedestal per offered SCP
/// carrying that SCP's model and label, and one big coin in front of each model. The room floats just
/// above the static surface zone (resolved at build time) — SCP:SL collision/physics get unreliable at
/// extreme coordinates, so anchoring to the real, already-loaded surface region keeps the floor walkable
/// without colliding with any map geometry.
/// </summary>
public sealed class SelectorRoom
{
    // Local layout (relative to the room origin, which sits at the floor surface center).
    private const float FloorTopY = 0f;
    private const float SpawnZ = -3f;
    private const float RowZ = 2.5f;
    private const float FloorCenterZ = -0.25f;
    private const float FloorDepth = 9f;
    private const float FloorThickness = 0.4f;
    private const float WallHeight = 5f;
    private const float WallThickness = 0.3f;
    private const float PedestalHeight = 1f;
    private const float PedestalDepth = 1.4f;
    private const float CenterDisplayClearance = 3f;
    private const float LogoScale = 0.39f;
    private const float LogoHdrBoost = 4.5f;
    private const float LogoHdrAlpha = 0.65f;
    private const int MaxOptions = 16;

    // 莺歌傲然 brand theme: a near-black navy gallery where the SCP models, teal floor seam, and
    // cream/gold server logo carry the saturation. Gold owns the supporting text. The lights stay near-white
    // on purpose so the muted SCP model primitives render close to true
    // color (a tinted light would wash them out). The surface is dark at night and players get no flashlight,
    // so the room reads on its own ambient + point lights. Tuned with tools/build_room_preview.py.
    private static readonly Color FloorColor = Hex("#161B26");
    private static readonly Color FloorSeamColor = Hex("#33EEDA");   // glowing teal seam so the ground plane reads with no flashlight
    private static readonly Color WallColor = Hex("#10141D");
    private static readonly Color CeilingColor = Hex("#0A0D13");
    private static readonly Color PedestalColor = Hex("#1A2130");
    private static readonly Color MainLightColor = Hex("#F4F3EE");   // overhead + spawn fill
    private static readonly Color PlaceholderColor = Hex("#5A6472");

    // The logo's resolved bounds are 6.419467 x 7.950564; centering around this authored-space point makes
    // its visual bounds land exactly on the requested wall anchor. It is embedded alongside the SCP models.
    private static readonly Vector3 LogoAssetCenter = new(-0.003987f, 0.291070f, -0.08f);

    // Back-wall world text below the primitive logo. Language-independent brand copy; GB2312-safe, no emoji.
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
    private Vector3 _aimDoorCenter;
    private Vector3 _aimDoorSize;

    public SelectorRoom(WarmupScpSelectorPlugin plugin)
    {
        _plugin = plugin;
    }

    private Config Config => _plugin.Config;

    /// <summary>Coin pickup serial -> the SCP role it selects.</summary>
    public Dictionary<ushort, RoleTypeId> CoinRoles { get; } = new();

    /// <summary>The SCP options actually built into the room, left to right; the status panel reads this so its
    /// chip row always matches the pedestals/coins on offer.</summary>
    public IReadOnlyList<ScpOption> OfferedOptions => _offered;

    /// <summary>Where warmup players are teleported to (and spawned), facing the pedestals.</summary>
    public Vector3 SpawnPosition { get; private set; }

    /// <summary>Resolved room origin (floor-surface center) for the current build; used to place activity stations.</summary>
    public Vector3 Origin { get; private set; }

    /// <summary>Resolved width shared by the selector gallery and its continuous Aim hall.</summary>
    public float Width { get; private set; }

    /// <summary>Depth of the selector-gallery portion of the continuous hall.</summary>
    public float Depth => FloorDepth;

    /// <summary>World Z where the selector floor meets the optional Aim hall.</summary>
    public float AimRangeDoorPlaneZ { get; private set; }

    /// <summary>Whether the room authored a fail-closed full-width seam for this warmup.</summary>
    public bool AimRangeDoorPrepared { get; private set; }

    public bool IsSpawned { get; private set; }

    public void Build()
    {
        Despawn();

        Vector3 origin = ResolveOrigin();
        // Valid, distinct SCP options only: skip nulls/non-SCP, collapse duplicate roles so each SCP gets
        // exactly one pedestal + coin, and cap the count so a pathological config can't spawn a huge room.
        List<ScpOption> options = (Config.ScpOptions ?? new List<ScpOption>())
            .Where(o => o != null && ScpOption.IsScpRole(o.Role))
            .GroupBy(o => o.Role)
            .Select(g => g.First())
            .Take(MaxOptions)
            .ToList();
        _offered.AddRange(options); // single source of truth for the live status-panel chip row
        int count = options.Count;
        float spacing = Sanitize(Config.PedestalSpacing, 2f, 20f, 4f);
        List<float> displayXs = Enumerable.Range(0, count)
            .Select(i => DisplayX(i, count, spacing))
            .ToList();
        float rowWidth = displayXs.Count > 0 ? displayXs.Max(x => Mathf.Abs(x)) * 2f : 0f;
        float floorWidth = rowWidth + 7f;
        float pedestalWidth = Mathf.Min(spacing * 0.7f, 1.6f);

        Origin = origin;
        Width = floorWidth;
        SpawnPosition = origin + new Vector3(0f, 0.5f, SpawnZ);

        // Floor (top surface at the room origin Y) and four perimeter walls so Tutorial players can't fall off.
        AddBox(origin + new Vector3(0f, FloorTopY - FloorThickness / 2f, FloorCenterZ), new Vector3(floorWidth, FloorThickness, FloorDepth), FloorColor, collidable: true);
        // Glowing brand seam: a bright teal border ring inset on the floor (a bright plate with a slightly
        // smaller floor-colored plate on top, leaving a lit border). Purely visual, sitting a few cm proud.
        AddBox(origin + new Vector3(0f, FloorTopY + 0.02f, FloorCenterZ), new Vector3(floorWidth - 0.5f, 0.04f, FloorDepth - 0.5f), FloorSeamColor, collidable: false);
        AddBox(origin + new Vector3(0f, FloorTopY + 0.03f, FloorCenterZ), new Vector3(floorWidth - 0.9f, 0.04f, FloorDepth - 0.9f), FloorColor, collidable: false);
        float wallMidY = FloorTopY + WallHeight / 2f;
        float backZ = FloorCenterZ + FloorDepth / 2f;
        float frontZ = FloorCenterZ - FloorDepth / 2f;
        AddBox(origin + new Vector3(0f, wallMidY, backZ), new Vector3(floorWidth, WallHeight, WallThickness), WallColor, collidable: true);
        if (Config.ActivitiesEnabled && Config.Activities?.Aim?.Enabled == true)
        {
            BuildAimRangeSeam(origin, floorWidth, frontZ);
        }
        else
        {
            AddBox(origin + new Vector3(0f, wallMidY, frontZ), new Vector3(floorWidth, WallHeight, WallThickness), WallColor, collidable: true);
        }
        AddBox(origin + new Vector3(-floorWidth / 2f, wallMidY, FloorCenterZ), new Vector3(WallThickness, WallHeight, FloorDepth), WallColor, collidable: true);
        AddBox(origin + new Vector3(floorWidth / 2f, wallMidY, FloorCenterZ), new Vector3(WallThickness, WallHeight, FloorDepth), WallColor, collidable: true);
        // Ceiling: encloses the room so the night skybox can't show behind the labels (which made them invisible).
        AddBox(origin + new Vector3(0f, FloorTopY + WallHeight, FloorCenterZ), new Vector3(floorWidth, WallThickness, FloorDepth), CeilingColor, collidable: true);

        // The centered primitive logo sits high on the back wall; the server name and welcome/QQ line sit
        // beneath it. Displays are split into left/right banks below so neither is hidden by an SCP model.
        Vector3 wallContentZ = new(0f, 0f, backZ - WallThickness / 2f - 0.06f);
        SpawnLogo(origin + wallContentZ + new Vector3(0f, FloorTopY + 3.15f, 0f), LogoScale);
        AddBanner(origin + wallContentZ + new Vector3(0f, FloorTopY + 0.95f, -0.01f));

        // Three strong, broad point lights illuminate the entire selector half. Do not regress to one
        // light per model or HDR/emissive colors: the combined hall deliberately uses a few bright lights.
        float lightY = FloorTopY + WallHeight - 0.6f;
        foreach (float x in new[] { -floorWidth / 3f, 0f, floorWidth / 3f })
        {
            AddLight(origin + new Vector3(x, lightY, FloorCenterZ), 24f, 18f);
        }

        // Models face the players (who stand on -Z): rotate the +Z-authored models 180 degrees about Y.
        Quaternion facing = Quaternion.Euler(0f, 180f, 0f);

        for (int i = 0; i < count; i++)
        {
            ScpOption option = options[i];
            float x = displayXs[i];

            AddBox(origin + new Vector3(x, FloorTopY + PedestalHeight / 2f, RowZ), new Vector3(pedestalWidth, PedestalHeight, PedestalDepth), PedestalColor, collidable: true);

            Vector3 modelBase = origin + new Vector3(x, FloorTopY + PedestalHeight, RowZ + 0.05f);
            float modelTop = SpawnModel(ResolveModelName(option), modelBase, facing, Sanitize(Config.ModelScale, 0.05f, 10f, 1f));

            // Label faces the player (-Z) with identity rotation; the 180-degree model facing mirrored the text.
            AddLabel(origin + new Vector3(x, FloorTopY + PedestalHeight + modelTop + 0.45f, RowZ), option.Label, Quaternion.identity);

            // Big coin floating clearly IN FRONT of the model (toward the players) and frozen so it can't
            // fall, roll, or clip into the model the way a resting pickup did.
            Vector3 coinPos = origin + new Vector3(x, FloorTopY + 1.1f, RowZ - PedestalDepth / 2f - 0.8f);
            Pickup? coin = Pickup.Create(Config.SelectorItem, coinPos, Quaternion.Euler(90f, 0f, 0f), Vector3.one * Sanitize(Config.SelectorCoinScale, 1f, 20f, 6f), networkSpawn: false);
            if (coin != null)
            {
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

        IsSpawned = true;
        _plugin.LogDebug($"Built selector room at {origin}: {_toys.Count} toys, {_pickups.Count} coins.");
    }

    // The hall is fail-closed during synchronous setup. Its temporary full-width wall disappears only after the
    // aligned continuation, counter guns, targets, and scheduler are ready; no doorway geometry remains afterward.
    private void BuildAimRangeSeam(Vector3 origin, float floorWidth, float frontZ)
    {
        AimRangeDoorPlaneZ = origin.z + frontZ;
        AimRangeDoorPrepared = true;
        _aimDoorCenter = origin + new Vector3(0f, WallHeight / 2f, frontZ);
        _aimDoorSize = new Vector3(floorWidth, WallHeight, WallThickness);
        _aimDoorGate = AddBox(_aimDoorCenter, _aimDoorSize, WallColor, collidable: true);
    }

    public bool OpenAimRangeDoor()
    {
        if (!AimRangeDoorPrepared || _aimDoorGate == null)
        {
            return false;
        }

        try
        {
            if (!_aimDoorGate.IsDestroyed)
            {
                _aimDoorGate.Destroy();
            }

            _aimDoorGate = null;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector] Could not open Aim Range door: {ex.Message}");
            return false;
        }
    }

    public void CloseAimRangeDoor()
    {
        if (!AimRangeDoorPrepared || _aimDoorGate != null || !IsSpawned)
        {
            return;
        }

        _aimDoorGate = AddBox(_aimDoorCenter, _aimDoorSize, WallColor, collidable: true);
    }

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
        _aimDoorCenter = default;
        _aimDoorSize = default;
        AimRangeDoorPlaneZ = 0f;
        AimRangeDoorPrepared = false;
        Width = 0f;
        IsSpawned = false;
    }

    /// <summary>Spawns one model's primitives at <paramref name="baseWorld"/>; returns the model's height above its base.</summary>
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

            Vector3 worldPos = baseWorld + facing * (primitive.Position * scale);
            Quaternion worldRot = facing * Quaternion.Euler(primitive.Rotation);
            Vector3 worldScale = primitive.Scale * scale;

            PrimitiveObjectToy toy = PrimitiveObjectToy.Create(worldPos, worldRot, worldScale, networkSpawn: false);
            _toys.Add(toy); // track before configure/spawn so a throw mid-setup is still cleaned up by Despawn
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
                    Vector3 parentPosition = centerWorld + (parentTransform.Position - LogoAssetCenter) * scale;
                    parent = PrimitiveObjectToy.Create(
                        parentPosition,
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

            Vector3 worldPosition = centerWorld + (primitive.Position - LogoAssetCenter) * scale;
            PrimitiveObjectToy toy = PrimitiveObjectToy.Create(
                worldPosition,
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

        // The center selector light is shared with the room instead of adding a fourth point light.
        // Unclamped translucent base colors restore the branded logo bloom within that light budget.
    }

    private PrimitiveObjectToy AddBox(Vector3 center, Vector3 size, Color color, bool collidable)
    {
        PrimitiveObjectToy toy = PrimitiveObjectToy.Create(center, Quaternion.identity, size, networkSpawn: false);
        _toys.Add(toy); // track before configure/spawn so a throw mid-setup is still cleaned up by Despawn
        toy.Type = PrimitiveType.Cube;
        toy.Color = color;
        toy.Flags = collidable ? PrimitiveFlags.Visible | PrimitiveFlags.Collidable : PrimitiveFlags.Visible;
        toy.IsStatic = true;
        toy.Spawn();
        return toy;
    }

    private static string ResolveModelName(ScpOption option)
    {
        if (!string.IsNullOrWhiteSpace(option.Model))
        {
            return option.Model.Trim();
        }

        return BuiltInModels.TryGetValue(option.Role, out string modelName) ? modelName : string.Empty;
    }

    // Neutral fallback block for custom/future SCPs whose model cannot be resolved.
    private float SpawnPlaceholderCube(Vector3 baseWorld, Quaternion facing, float scale)
    {
        float height = 1.7f * scale;
        Vector3 size = new Vector3(1.1f, 1.7f, 1.1f) * scale;
        PrimitiveObjectToy cube = PrimitiveObjectToy.Create(baseWorld + new Vector3(0f, height / 2f, 0f), facing, size, networkSpawn: false);
        _toys.Add(cube);
        cube.Type = PrimitiveType.Cube;
        cube.Color = PlaceholderColor;
        cube.Flags = PrimitiveFlags.Visible;
        cube.IsStatic = true;
        cube.Spawn();
        return height;
    }

    private void AddLight(Vector3 center, float intensity, float range, Color? color = null)
    {
        LightSourceToy light = LightSourceToy.Create(center, Quaternion.identity, Vector3.one, networkSpawn: false);
        _toys.Add(light);
        light.Color = color ?? MainLightColor;
        light.Intensity = intensity;
        light.Range = range;
        light.Type = LightType.Point;
        light.ShadowType = LightShadows.None;
        light.IsStatic = true;
        light.Spawn();
    }

    private void AddLabel(Vector3 center, string label, Quaternion facing)
    {
        TextToy text = TextToy.Create(center, facing, Vector3.one * 0.2f, networkSpawn: false);
        _toys.Add(text); // track before configure/spawn so a throw mid-setup is still cleaned up by Despawn
        text.TextFormat = $"<align=center><b>{label}</b></align>";
        text.DisplaySize = new Vector2(220f, 40f);
        text.IsStatic = true;
        text.Spawn();
    }

    // Server name + welcome/QQ line beneath the logo. Faces the player line (−Z) with identity rotation.
    private void AddBanner(Vector3 center)
    {
        TextToy text = TextToy.Create(center, Quaternion.identity, Vector3.one * 0.45f, networkSpawn: false);
        _toys.Add(text); // track before configure/spawn so a throw mid-setup is still cleaned up by Despawn
        text.TextFormat = BannerMarkup;
        text.DisplaySize = new Vector2(1000f, 200f);
        text.IsStatic = true;
        text.Spawn();
    }

    // Split the ordered displays into left/right banks around a genuinely centered logo opening. An odd
    // count cannot form complete pairs, so its unmatched display goes on the far-right outer edge rather
    // than widening one side of the central opening (the old seven-option layout was -7m vs +3m inside).
    private static float DisplayX(int index, int count, float spacing)
    {
        int leftCount = count / 2;
        return index < leftCount
            ? -CenterDisplayClearance - (leftCount - 1 - index) * spacing
            : CenterDisplayClearance + (index - leftCount) * spacing;
    }

    private static Color LogoHdrColor(Color authored)
    {
        return new Color(
            authored.r * LogoHdrBoost,
            authored.g * LogoHdrBoost,
            authored.b * LogoHdrBoost,
            LogoHdrAlpha);
    }

    private static Color Hex(string hex)
    {
        return ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.magenta;
    }

    // Anchor the room just above the static surface zone — sane, already-loaded coordinates where the
    // primitive floor is reliably walkable. SCP:SL collision/physics misbehave at extreme/empty coords
    // (e.g. a hand-picked (0,1000,0)), which is why a freshly placed player fell straight through.
    private Vector3 ResolveOrigin()
    {
        try
        {
            Room? surface = Room.Get(FacilityZone.Surface).FirstOrDefault();
            if (surface != null)
            {
                float clearance = Sanitize(Config.SurfaceClearance, 5f, 80f, 20f);
                return surface.Position + new Vector3(0f, clearance, 0f);
            }

            Logger.Warn("[WarmupScpSelector] Surface zone not found; using fallback RoomOrigin.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector] Could not resolve surface origin: {ex.Message}");
        }

        return SanitizeOrigin(Config.RoomOrigin, new Vector3(0f, 1015f, 0f));
    }

    private static float Sanitize(float value, float min, float max, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return fallback;
        }

        return Mathf.Clamp(value, min, max);
    }

    private static Vector3 SanitizeOrigin(Vector3 value, Vector3 fallback)
    {
        bool finite =
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        return finite ? value : fallback;
    }
}
