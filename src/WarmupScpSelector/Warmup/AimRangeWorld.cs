using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using UnityEngine;
using WarmupScpSelector.Activities.AimRange;
using WarmupScpSelector.Models;
using Logger = LabApi.Features.Console.Logger;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace WarmupScpSelector.Warmup
{
    /// <summary>Explicit collidable widened range shell plus validated visual-only MER weapon racks.</summary>
    public sealed class AimRangeWorld
    {
        private static readonly Color FloorColor = Hex("#293140");
        private static readonly Color FloorSeamColor = Hex("#33EEDA");
        private static readonly Color WallColor = Hex("#242C3B");
        private static readonly Color CeilingColor = Hex("#1B2230");
        private static readonly Color DividerColor = Hex("#35445C");
        private static readonly Color BackstopColor = Hex("#202735");
        private static readonly Color LightColor = Hex("#FFFDF3");
        private static readonly Color BotCoverColor = Hex("#2D394D");

        private readonly List<AdminToy> _toys = new List<AdminToy>();
        private readonly List<AimShelfAnchor> _shelfAnchors = new List<AimShelfAnchor>();
        private readonly MerWorldSpawner _merSpawner = new MerWorldSpawner();

        public AimRangeLayout? Layout { get; private set; }
        public IReadOnlyList<AimShelfAnchor> ShelfAnchors => _shelfAnchors;
        public bool IsSpawned { get; private set; }

        public bool Build(Vector3 galleryOrigin, float galleryFrontZ)
        {
            Despawn();
            try
            {
                Layout = new AimRangeLayout(galleryOrigin, galleryFrontZ);
                ValidateLayout(Layout);
                _shelfAnchors.AddRange(Layout.ShelfAnchors);
                float y = galleryOrigin.y;
                float doorZ = Layout.DoorPlaneZ;
                float halfWidth = AimRangeLayout.Width / 2f;
                float wall = 0.3f;

                float stubWidth = (AimRangeLayout.Width - AimRangeLayout.DoorWidth) / 2f;
                AddBox(new Vector3(galleryOrigin.x - (AimRangeLayout.DoorWidth + stubWidth) / 2f, y + 2.5f, doorZ),
                    new Vector3(stubWidth, 5f, wall), WallColor, true);
                AddBox(new Vector3(galleryOrigin.x + (AimRangeLayout.DoorWidth + stubWidth) / 2f, y + 2.5f, doorZ),
                    new Vector3(stubWidth, 5f, wall), WallColor, true);
                AddBox(new Vector3(galleryOrigin.x, y + AimRangeLayout.DoorHeight + (5f - AimRangeLayout.DoorHeight) / 2f, doorZ),
                    new Vector3(AimRangeLayout.DoorWidth, 5f - AimRangeLayout.DoorHeight, wall), WallColor, true);

                AddBox(new Vector3(galleryOrigin.x, y - 0.2f, doorZ - AimRangeLayout.Depth / 2f),
                    new Vector3(AimRangeLayout.Width, 0.4f, AimRangeLayout.Depth), FloorColor, true);
                AddBox(new Vector3(galleryOrigin.x, y + 0.02f, doorZ - AimRangeLayout.Depth / 2f),
                    new Vector3(AimRangeLayout.Width - 0.5f, 0.04f, AimRangeLayout.Depth - 0.5f), FloorSeamColor, false);
                AddBox(new Vector3(galleryOrigin.x, y + 0.03f, doorZ - AimRangeLayout.Depth / 2f),
                    new Vector3(AimRangeLayout.Width - 0.9f, 0.04f, AimRangeLayout.Depth - 0.9f), FloorColor, false);
                AddBox(new Vector3(galleryOrigin.x - halfWidth, y + 2.5f, doorZ - AimRangeLayout.Depth / 2f),
                    new Vector3(wall, 5f, AimRangeLayout.Depth), WallColor, true);
                AddBox(new Vector3(galleryOrigin.x + halfWidth, y + 2.5f, doorZ - AimRangeLayout.Depth / 2f),
                    new Vector3(wall, 5f, AimRangeLayout.Depth), WallColor, true);
                AddBox(new Vector3(galleryOrigin.x, y + 2.5f, doorZ - AimRangeLayout.Depth),
                    new Vector3(AimRangeLayout.Width, 5f, 0.6f), BackstopColor, true);
                AddBox(new Vector3(galleryOrigin.x, y + 5f, doorZ - AimRangeLayout.Depth / 2f),
                    new Vector3(AimRangeLayout.Width, 0.3f, AimRangeLayout.Depth), CeilingColor, true);

                float shootingLineDepth = 6.8f;
                float dividerStartDepth = shootingLineDepth + 0.2f;
                float dividerLength = AimRangeLayout.Depth - dividerStartDepth;
                float dividerCenterDepth = dividerStartDepth + dividerLength / 2f;
                AddBox(new Vector3(galleryOrigin.x, y + 0.55f, doorZ - shootingLineDepth),
                    new Vector3(AimRangeLayout.Width - 1.2f, 1.1f, 0.25f), WallColor, true);
                AddBox(new Vector3(galleryOrigin.x - AimRangeLayout.LaneWidth / 2f, y + 1.4f, doorZ - dividerCenterDepth),
                    new Vector3(0.25f, 2.8f, dividerLength), DividerColor, true);
                AddBox(new Vector3(galleryOrigin.x + AimRangeLayout.LaneWidth / 2f, y + 1.4f, doorZ - dividerCenterDepth),
                    new Vector3(0.25f, 2.8f, dividerLength), DividerColor, true);

                BuildBotCover(Layout.BotCovers);
                BuildSlidingRails(Layout.SlidingTargetTracks, y);

                BuildShelfColliders(Layout.LeftRackOrigin, Vector3.right);
                BuildShelfColliders(Layout.RightRackOrigin, Vector3.left);
                BuildCradleColliders(Layout.ShelfAnchors);
                SpawnRackVisuals();

                foreach (float depth in new[] { 3.0f, 10.5f, 18.2f })
                {
                    AddLight(new Vector3(galleryOrigin.x - AimRangeLayout.LaneWidth, y + 3.9f, doorZ - depth), 5.2f, 10.8f);
                    AddLight(new Vector3(galleryOrigin.x, y + 3.9f, doorZ - depth), 5.2f, 10.8f);
                    AddLight(new Vector3(galleryOrigin.x + AimRangeLayout.LaneWidth, y + 3.9f, doorZ - depth), 5.2f, 10.8f);
                }

                AddLabel(new Vector3(galleryOrigin.x, y + 3.9f, doorZ - 0.18f), "AIM RANGE · 瞄准训练场", 420f);
                AddLabel(new Vector3(galleryOrigin.x - AimRangeLayout.LaneWidth, y + 3.55f, doorZ - 6.55f), "1  LIVE BOTS · 实战机器人", 260f);
                AddLabel(new Vector3(galleryOrigin.x, y + 3.55f, doorZ - 6.55f), "2  SLIDING · 平移靶", 230f);
                AddLabel(new Vector3(galleryOrigin.x + AimRangeLayout.LaneWidth, y + 3.55f, doorZ - 6.55f), "3  SPHERES · 球形反应", 250f);
                IsSpawned = true;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim range world startup failed: {ex.Message}");
                Despawn();
                return false;
            }
        }

        public void Despawn()
        {
            IsSpawned = false;
            _merSpawner.DespawnAll();
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
                    Logger.Warn($"[WarmupScpSelector] Aim range toy cleanup failed: {ex.Message}");
                }
            }

            _toys.Clear();
            _shelfAnchors.Clear();
            Layout = null;
        }

        private void BuildBotCover(IReadOnlyList<AimBotCover> covers)
        {
            foreach (AimBotCover cover in covers)
            {
                AddBox(cover.Center, cover.Size, BotCoverColor, true);
            }
        }

        private void BuildSlidingRails(IReadOnlyList<SlidingTargetTrackDefinition> tracks, float floorY)
        {
            foreach (SlidingTargetTrackDefinition track in tracks)
            {
                Vector3 center = Vector3.Lerp(track.EndpointA, track.EndpointB, 0.5f);
                float length = Vector3.Distance(track.EndpointA, track.EndpointB) + 0.6f;
                AddBox(new Vector3(center.x, floorY + 0.025f, center.z), new Vector3(length, 0.05f, 0.18f), DividerColor, false);
            }
        }

        private void SpawnRackVisuals()
        {
            if (Layout == null)
            {
                return;
            }

            IReadOnlyList<MerPrimitive>? rackAsset = MerAssetCatalog.LoadValidated(
                MerAssetCatalog.WeaponRackAsset,
                MerAssetCatalog.WeaponRackMarkers);
            if (rackAsset == null)
            {
                return;
            }

            TrySpawnRackVisual(rackAsset, true, Layout.LeftRackOrigin, Layout.LeftRackRotation);
            TrySpawnRackVisual(rackAsset, false, Layout.RightRackOrigin, Layout.RightRackRotation);
        }

        private void TrySpawnRackVisual(IReadOnlyList<MerPrimitive> asset, bool left, Vector3 origin, Quaternion rotation)
        {
            if (Layout == null)
            {
                return;
            }

            string side = left ? "left" : "right";
            MerWorldInstance? instance = null;
            try
            {
                MerWorldTransform root = new MerWorldTransform(origin, rotation, Vector3.one);
                if (!_merSpawner.TrySpawn(asset, MerAssetCatalog.WeaponRackMarkers, root, true, out instance, out string error) ||
                    instance == null)
                {
                    Logger.Warn($"[WarmupScpSelector] {side} weapon-rack visual disabled: {error}");
                    return;
                }

                if (!AimRangeMarkerAlignment.TryResolveRack(left, instance.Markers, Layout.ShelfAnchors,
                        out IReadOnlyList<AimShelfAnchor> resolved, out error))
                {
                    _merSpawner.Despawn(instance);
                    Logger.Warn($"[WarmupScpSelector] {side} weapon-rack visual disabled: {error}");
                    return;
                }

                foreach (AimShelfAnchor anchor in resolved)
                {
                    int index = _shelfAnchors.FindIndex(candidate => candidate.SlotId == anchor.SlotId);
                    if (index >= 0)
                    {
                        _shelfAnchors[index] = anchor;
                    }
                }
            }
            catch (Exception ex)
            {
                _merSpawner.Despawn(instance);
                Logger.Warn($"[WarmupScpSelector] {side} weapon-rack visual disabled: {ex.GetBaseException().Message}");
            }
        }

        private void BuildShelfColliders(Vector3 rackOrigin, Vector3 inward)
        {
            AddColliderBox(rackOrigin + new Vector3(0f, 1.35f, 0f), new Vector3(0.35f, 2.7f, 4.3f));
            AddColliderBox(rackOrigin + inward * 0.22f + new Vector3(0f, 0.85f, 0f), new Vector3(0.45f, 0.12f, 4f));
            AddColliderBox(rackOrigin + inward * 0.22f + new Vector3(0f, 1.65f, 0f), new Vector3(0.45f, 0.12f, 4f));
            AddColliderBox(rackOrigin + inward * 0.22f + new Vector3(0f, 2.45f, 0f), new Vector3(0.45f, 0.12f, 4f));
        }

        private void BuildCradleColliders(IReadOnlyList<AimShelfAnchor> anchors)
        {
            foreach (AimShelfAnchor anchor in anchors)
            {
                bool left = anchor.SlotId < 3;
                Vector3 inward = left ? Vector3.right : Vector3.left;
                float markerHeightOffset = anchor.SlotId == 1 || anchor.SlotId == 4 ? -0.05f : 0.03f;
                Vector3 cradle = anchor.LocalPosition - inward * 0.25f + Vector3.up * markerHeightOffset;
                AddColliderBox(cradle + Vector3.down * 0.05f, new Vector3(0.12f, 0.10f, 0.44f));
                AddColliderBox(cradle + new Vector3(0f, 0.05f, -0.19f), new Vector3(0.10f, 0.18f, 0.08f));
                AddColliderBox(cradle + new Vector3(0f, 0.05f, 0.19f), new Vector3(0.10f, 0.18f, 0.08f));
            }
        }

        private void AddColliderBox(Vector3 center, Vector3 size)
        {
            PrimitiveObjectToy toy = PrimitiveObjectToy.Create(center, Quaternion.identity, size, networkSpawn: false);
            _toys.Add(toy);
            toy.Type = PrimitiveType.Cube;
            toy.Color = Color.clear;
            toy.Flags = PrimitiveFlags.Collidable;
            toy.IsStatic = true;
            toy.Spawn();
        }

        private void AddBox(Vector3 center, Vector3 size, Color color, bool collidable)
        {
            PrimitiveObjectToy toy = PrimitiveObjectToy.Create(center, Quaternion.identity, size, networkSpawn: false);
            _toys.Add(toy);
            toy.Type = PrimitiveType.Cube;
            toy.Color = color;
            toy.Flags = collidable ? PrimitiveFlags.Visible | PrimitiveFlags.Collidable : PrimitiveFlags.Visible;
            toy.IsStatic = true;
            toy.Spawn();
        }

        private void AddLight(Vector3 center, float intensity, float range)
        {
            LightSourceToy light = LightSourceToy.Create(center, Quaternion.identity, Vector3.one, networkSpawn: false);
            _toys.Add(light);
            light.Color = LightColor;
            light.Intensity = intensity;
            light.Range = range;
            light.Type = LightType.Point;
            light.ShadowType = LightShadows.None;
            light.IsStatic = true;
            light.Spawn();
        }

        private void AddLabel(Vector3 center, string text, float width)
        {
            TextToy label = TextToy.Create(center, Quaternion.Euler(0f, 180f, 0f), Vector3.one * 0.18f, networkSpawn: false);
            _toys.Add(label);
            label.TextFormat = "<align=center><b>" + text + "</b></align>";
            label.DisplaySize = new Vector2(width, 50f);
            label.IsStatic = true;
            label.Spawn();
        }

        private static void ValidateLayout(AimRangeLayout layout)
        {
            if (layout == null || layout.ShelfAnchors.Count != 6 ||
                layout.ShelfAnchors.Select(anchor => anchor.SlotId).Distinct().Count() != 6 ||
                layout.SlidingTargetTracks.Count != 3 || layout.BotPaths.Count != 2 ||
                SphereTargetLayout.RequiredClearWidth > AimRangeLayout.LaneWidth - 0.5f ||
                AimRangeLayout.Width > 19.21f || AimRangeLayout.Depth > 22.01f)
            {
                throw new InvalidOperationException("authored widened range gameplay anchors are incomplete");
            }
        }

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.magenta;
    }
}
