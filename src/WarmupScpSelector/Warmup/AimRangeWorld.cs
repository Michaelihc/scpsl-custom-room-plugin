using System;
using System.Collections.Generic;
using InventorySystem.Items.Firearms.Attachments;
using LabApi.Features.Wrappers;
using MapGeneration.Distributors;
using Mirror;
using UnityEngine;
using WarmupScpSelector.Activities.AimRange;
using Logger = LabApi.Features.Console.Logger;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace WarmupScpSelector.Warmup
{
    /// <summary>Explicit collidable continuation of the selector hall, with three centered training lanes.</summary>
    public sealed class AimRangeWorld
    {
        private static readonly Color FloorColor = Hex("#293140");
        private static readonly Color FloorSeamColor = Hex("#33EEDA");
        private static readonly Color WallColor = Hex("#242C3B");
        private static readonly Color CeilingColor = Hex("#1B2230");
        private static readonly Color DividerColor = Hex("#35445C");
        private static readonly Color BackstopColor = Hex("#202735");
        private static readonly Color CounterColor = Hex("#31445E");
        private static readonly Color LightColor = Hex("#FFFDF3");
        private static readonly Color BotCoverColor = Hex("#2D394D");

        private readonly List<AdminToy> _toys = new List<AdminToy>();
        private readonly List<GameObject> _structures = new List<GameObject>();
        public AimRangeLayout? Layout { get; private set; }
        public IReadOnlyList<AimShelfAnchor> ShelfAnchors => Layout?.ShelfAnchors ?? Array.Empty<AimShelfAnchor>();
        public bool IsSpawned { get; private set; }

        public bool Build(Vector3 galleryOrigin, float galleryFrontZ, float galleryWidth, float galleryDepth)
        {
            Despawn();
            try
            {
                Layout = new AimRangeLayout(galleryOrigin, galleryFrontZ, galleryWidth, galleryDepth);
                ValidateLayout(Layout);
                float y = galleryOrigin.y;
                float doorZ = Layout.DoorPlaneZ;
                float shellWidth = Layout.ShellWidth;
                float halfWidth = shellWidth / 2f;
                float wall = 0.3f;

                AddBox(new Vector3(galleryOrigin.x, y - 0.2f, doorZ - AimRangeLayout.Depth / 2f),
                    new Vector3(shellWidth, 0.4f, AimRangeLayout.Depth), FloorColor, true);
                AddBox(new Vector3(galleryOrigin.x, y + 0.02f, doorZ - AimRangeLayout.Depth / 2f),
                    new Vector3(shellWidth - 0.5f, 0.04f, AimRangeLayout.Depth - 0.5f), FloorSeamColor, false);
                AddBox(new Vector3(galleryOrigin.x, y + 0.03f, doorZ - AimRangeLayout.Depth / 2f),
                    new Vector3(shellWidth - 0.9f, 0.04f, AimRangeLayout.Depth - 0.9f), FloorColor, false);
                AddBox(new Vector3(galleryOrigin.x - halfWidth, y + 2.5f, doorZ - AimRangeLayout.Depth / 2f),
                    new Vector3(wall, 5f, AimRangeLayout.Depth), WallColor, true);
                AddBox(new Vector3(galleryOrigin.x + halfWidth, y + 2.5f, doorZ - AimRangeLayout.Depth / 2f),
                    new Vector3(wall, 5f, AimRangeLayout.Depth), WallColor, true);
                AddBox(new Vector3(galleryOrigin.x, y + 2.5f, doorZ - AimRangeLayout.Depth),
                    new Vector3(shellWidth, 5f, 0.6f), BackstopColor, true);
                AddBox(new Vector3(galleryOrigin.x, y + 5f, doorZ - AimRangeLayout.Depth / 2f),
                    new Vector3(shellWidth, 0.3f, AimRangeLayout.Depth), CeilingColor, true);

                float shootingLineDepth = AimRangeLayout.ShootingCounterDepth;
                float dividerStartDepth = shootingLineDepth + 0.2f;
                float dividerLength = AimRangeLayout.Depth - dividerStartDepth;
                float dividerCenterDepth = dividerStartDepth + dividerLength / 2f;
                AddBox(new Vector3(galleryOrigin.x, y + AimRangeLayout.ShootingCounterHeight / 2f, doorZ - shootingLineDepth),
                    new Vector3(Layout.ShootingCounterWidth, AimRangeLayout.ShootingCounterHeight, 0.4f), CounterColor, true);
                AddBox(new Vector3(galleryOrigin.x - AimRangeLayout.LaneWidth / 2f, y + AimRangeLayout.Height / 2f, doorZ - dividerCenterDepth),
                    new Vector3(0.25f, AimRangeLayout.Height, dividerLength), DividerColor, true);
                AddBox(new Vector3(galleryOrigin.x + AimRangeLayout.LaneWidth / 2f, y + AimRangeLayout.Height / 2f, doorZ - dividerCenterDepth),
                    new Vector3(0.25f, AimRangeLayout.Height, dividerLength), DividerColor, true);

                BuildAttachmentWorkstations(Layout.AttachmentWorkstationAnchors);

                BuildBotCover(Layout.BotCovers);
                BuildSlidingRails(Layout.SlidingTargetTracks, y);

                // Three deliberately strong point lights replace the old nine-light grid. They make the six
                // physical counter guns obvious without HDR materials or a dense field of light toys.
                foreach (float x in new[] { -AimRangeLayout.LaneWidth, 0f, AimRangeLayout.LaneWidth })
                {
                    AddLight(new Vector3(galleryOrigin.x + x, y + 3.8f, doorZ - shootingLineDepth + 0.7f), 24f, 16f);
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
            for (int i = _structures.Count - 1; i >= 0; i--)
            {
                try
                {
                    GameObject structure = _structures[i];
                    if (structure == null)
                    {
                        continue;
                    }

                    NetworkIdentity identity = structure.GetComponent<NetworkIdentity>();
                    if (NetworkServer.active && identity != null && identity.netId != 0)
                    {
                        NetworkServer.Destroy(structure);
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(structure);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[WarmupScpSelector] Aim attachment-workstation cleanup failed: {ex.Message}");
                }
            }

            _structures.Clear();
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
            Layout = null;
        }

        private void BuildBotCover(IReadOnlyList<AimBotCover> covers)
        {
            foreach (AimBotCover cover in covers)
            {
                AddBox(cover.Center, cover.Size, BotCoverColor, true);
            }
        }

        private void BuildAttachmentWorkstations(IReadOnlyList<AimWorkstationAnchor> anchors)
        {
            SpawnableStructure? prefab = ResolveAttachmentWorkstationPrefab();
            if (prefab == null || anchors == null || anchors.Count != 2)
            {
                throw new InvalidOperationException("native attachment workstation prefab or symmetric anchors unavailable");
            }

            foreach (AimWorkstationAnchor anchor in anchors)
            {
                SpawnableStructure? instance = null;
                try
                {
                    instance = UnityEngine.Object.Instantiate(prefab, anchor.Position, anchor.Rotation);
                    _structures.Add(instance.gameObject); // own before spawn so partial setup still tears down
                    WorkstationController controller = instance.GetComponentInChildren<WorkstationController>(true);
                    if (controller == null)
                    {
                        throw new InvalidOperationException("workstation prefab has no controller");
                    }

                    controller.Status = (byte)WorkstationController.WorkstationStatus.Offline;
                    controller.KnownUser = null;
                    controller.ServerStopwatch.Reset();
                    NetworkServer.Spawn(instance.gameObject);
                }
                catch
                {
                    if (instance != null && !_structures.Contains(instance.gameObject))
                    {
                        _structures.Add(instance.gameObject);
                    }

                    throw;
                }
            }
        }

        private static SpawnableStructure? ResolveAttachmentWorkstationPrefab()
        {
            foreach (GameObject candidate in NetworkClient.prefabs.Values)
            {
                if (candidate != null && candidate.TryGetComponent(out SpawnableStructure structure) &&
                    structure.StructureType == StructureType.Workstation &&
                    candidate.GetComponentInChildren<WorkstationController>(true) != null)
                {
                    return structure;
                }
            }

            if (NetworkManager.singleton != null)
            {
                foreach (GameObject candidate in NetworkManager.singleton.spawnPrefabs)
                {
                    if (candidate != null && candidate.TryGetComponent(out SpawnableStructure structure) &&
                        structure.StructureType == StructureType.Workstation &&
                        candidate.GetComponentInChildren<WorkstationController>(true) != null)
                    {
                        return structure;
                    }
                }
            }

            return null;
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
            if (layout == null || layout.ShelfAnchors.Count != 6 || layout.AttachmentWorkstationAnchors.Count != 2 ||
                layout.SlidingTargetTracks.Count != 3 || layout.BotPaths.Count != 2 ||
                SphereTargetLayout.RequiredClearWidth > layout.SphereBayWidth - 0.5f ||
                layout.ShellWidth < AimRangeLayout.Width || AimRangeLayout.Depth > 22.01f)
            {
                throw new InvalidOperationException("authored widened range gameplay anchors are incomplete");
            }
        }

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.magenta;
    }
}
