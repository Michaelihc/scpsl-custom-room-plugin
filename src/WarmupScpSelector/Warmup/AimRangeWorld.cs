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
    /// <summary>
    /// Fits out the station's Aim Bay: shooting counter, lane dividers, bot cover, target rails, native
    /// attachment workstations, counter lighting, and lane signage.
    ///
    /// The bay's deck, bulkheads, overhead, and general lighting belong to <see cref="StationShellBuilder"/>
    /// - this only adds range furniture, so the range can be rebuilt without touching the room around it.
    /// </summary>
    public sealed class AimRangeWorld
    {
        private const float CounterThickness = 0.4f;
        private const float DividerThickness = 0.25f;
        private const float CounterLightIntensity = 24f;
        private const float CounterLightRange = 16f;

        private readonly List<AdminToy> _toys = new List<AdminToy>();
        private readonly List<GameObject> _structures = new List<GameObject>();

        public AimRangeLayout? Layout { get; private set; }

        public IReadOnlyList<AimShelfAnchor> ShelfAnchors => Layout?.ShelfAnchors ?? Array.Empty<AimShelfAnchor>();

        public bool IsSpawned { get; private set; }

        public bool Build(WarmupHallLayout hall, bool chinese)
        {
            Despawn();
            try
            {
                AimRangeLayout layout = new AimRangeLayout(hall);
                ValidateLayout(layout);
                Layout = layout;

                float downrangeLength = layout.BackstopX - layout.ShootingLineX;

                // Low counter across the whole bay at the shooting line.
                AddBox(
                    layout.Range(0f, AimRangeLayout.ShootingCounterHeight / 2f, 0f),
                    new Vector3(CounterThickness, AimRangeLayout.ShootingCounterHeight, layout.ShootingCounterWidth),
                    StationPalette.Frame,
                    collidable: true);
                AddBox(
                    layout.Range(0f, AimRangeLayout.ShootingCounterHeight + 0.02f, 0f),
                    new Vector3(CounterThickness + 0.06f, 0.04f, layout.ShootingCounterWidth - 0.2f),
                    StationPalette.Guide,
                    collidable: false);

                // Full-height lane dividers, starting just downrange of the counter so the firing line stays open.
                float dividerStart = 0.6f;
                float dividerLength = downrangeLength - dividerStart;
                float dividerCenter = dividerStart + dividerLength / 2f;
                foreach (float lateral in new[] { layout.DividerOneZ, layout.DividerTwoZ })
                {
                    AddBox(
                        layout.Range(lateral, AimRangeLayout.Height / 2f, dividerCenter),
                        new Vector3(dividerLength, AimRangeLayout.Height, DividerThickness),
                        StationPalette.BulkheadRib,
                        collidable: true);
                }

                // Backstop facing: a visibly darker plate on the far bulkhead so rounds have somewhere to
                // land. It stands PROUD of that bulkhead rather than sunk into it: overlapping the wall
                // put its top and bottom faces on the wall's own planes, which flickers along the top
                // edge of the range.
                AddBox(
                    layout.Range(0f, AimRangeLayout.Height / 2f, downrangeLength - 0.35f),
                    new Vector3(0.35f, AimRangeLayout.Height, layout.ShellWidth - 0.4f),
                    StationPalette.Overhead,
                    collidable: true);

                BuildAttachmentWorkstations(layout.AttachmentWorkstationAnchors);
                BuildBotCover(layout.BotCovers);
                BuildSlidingRails(layout);
                BuildCounterLighting(layout);
                BuildSignage(layout, chinese);

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
                AddBox(cover.Center, cover.Size, StationPalette.Bulkhead, collidable: true);
                AddBox(
                    new Vector3(cover.Center.x, cover.Center.y + cover.Size.y / 2f + 0.03f, cover.Center.z),
                    new Vector3(cover.Size.x + 0.04f, 0.05f, cover.Size.z - 0.1f),
                    StationPalette.Caution,
                    collidable: false);
            }
        }

        private void BuildSlidingRails(AimRangeLayout layout)
        {
            foreach (SlidingTargetTrackDefinition track in layout.SlidingTargetTracks)
            {
                Vector3 center = Vector3.Lerp(track.EndpointA, track.EndpointB, 0.5f);
                float length = Vector3.Distance(track.EndpointA, track.EndpointB) + 0.6f;
                AddBox(
                    new Vector3(center.x, layout.DeckY + 0.03f, center.z),
                    new Vector3(0.18f, 0.05f, length),
                    StationPalette.BulkheadRib,
                    collidable: false);
            }
        }

        /// <summary>
        /// Three deliberately strong lights over the firing line. Do not regress to a dense grid or HDR
        /// materials: this exists so the six physical counter guns are obvious, nothing more.
        /// </summary>
        private void BuildCounterLighting(AimRangeLayout layout)
        {
            foreach (float lateral in new[] { layout.LaneOneCenter, layout.LaneTwoCenter, layout.LaneThreeCenter })
            {
                AddLight(layout.Range(lateral, 3.8f, 1.2f), CounterLightIntensity, CounterLightRange);
            }
        }

        private void BuildSignage(AimRangeLayout layout, bool chinese)
        {
            AddLabel(layout.Range(0f, 3.9f, -2.6f),
                chinese ? "瞄准训练舱" : "AIM BAY", 420f);
            AddLabel(layout.Range(layout.LaneOneCenter, 3.4f, 1.4f),
                chinese ? "1 实战机器人" : "1  LIVE BOTS", 300f);
            AddLabel(layout.Range(layout.LaneTwoCenter, 3.4f, 1.4f),
                chinese ? "2 平移靶" : "2  SLIDING", 300f);
            AddLabel(layout.Range(layout.LaneThreeCenter, 3.4f, 1.4f),
                chinese ? "3 球形反应" : "3  SPHERES", 300f);
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
            light.Color = StationPalette.DeckLight;
            light.Intensity = intensity;
            light.Range = range;
            light.Type = LightType.Point;
            light.ShadowType = LightShadows.None;
            light.IsStatic = true;
            light.Spawn();
        }

        /// <summary>Range signage is read by a shooter standing at the counter facing downrange (+X).</summary>
        private void AddLabel(Vector3 center, string text, float width)
        {
            TextToy label = TextToy.Create(
                center, WarmupHallLayout.FacingViewer(Vector3.right), Vector3.one * 0.18f, networkSpawn: false);
            _toys.Add(label);
            label.TextFormat = "<align=center><b>" + text + "</b></align>";
            label.DisplaySize = new Vector2(width, 50f);
            label.IsStatic = true;
            label.Spawn();
        }

        /// <summary>
        /// Fails the range closed when the bay cannot actually host all three lanes. The sphere check is
        /// the tight one: lane 3 is the widest lane precisely because the cloud needs the most clear width.
        /// </summary>
        private static void ValidateLayout(AimRangeLayout layout)
        {
            if (layout.ShelfAnchors.Count != 6 ||
                layout.AttachmentWorkstationAnchors.Count != 2 ||
                layout.SlidingTargetTracks.Count != 3 ||
                layout.BotPaths.Count != 2 ||
                layout.BotCovers.Count != 6)
            {
                throw new InvalidOperationException("authored range gameplay anchors are incomplete");
            }

            if (layout.SphereBayWidth < SphereTargetLayout.RequiredClearWidth ||
                layout.BackstopX - layout.ShootingLineX < SphereTargetLayout.RequiredClearDepth ||
                layout.ShellWidth < AimRangeLayout.Width - 0.01f)
            {
                throw new InvalidOperationException("aim bay is too small for the three authored lanes");
            }
        }
    }
}
