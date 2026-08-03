using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WarmupScpSelector.Models;

namespace WarmupScpSelector.Warmup
{
    /// <summary>
    /// Converts authored rack cradle markers into pickup pivots and verifies them against the layout.
    /// The 0.25 m face clearance and small height offsets are the same values hard-checked by
    /// tools/build_room_preview.py; keeping them here makes runtime visuals and gameplay fail closed on drift.
    /// </summary>
    internal static class AimRangeMarkerAlignment
    {
        public const float PositionTolerance = 0.01f;
        public const float RotationToleranceDegrees = 0.5f;

        private readonly struct SlotBinding
        {
            public SlotBinding(int slotId, string marker, float heightOffset)
            {
                SlotId = slotId;
                Marker = marker;
                HeightOffset = heightOffset;
            }

            public int SlotId { get; }
            public string Marker { get; }
            public float HeightOffset { get; }
        }

        private static readonly SlotBinding[] LeftBindings =
        {
            new SlotBinding(0, "marker_shelf_0", -0.03f),
            new SlotBinding(1, "marker_shelf_1", 0.05f),
            new SlotBinding(2, "marker_shelf_2", -0.03f),
        };

        private static readonly SlotBinding[] RightBindings =
        {
            new SlotBinding(3, "marker_shelf_2", -0.03f),
            new SlotBinding(4, "marker_shelf_3", 0.05f),
            new SlotBinding(5, "marker_shelf_0", -0.03f),
        };

        public static bool TryResolveRack(
            bool left,
            IReadOnlyDictionary<string, MerWorldTransform> markers,
            IReadOnlyList<AimShelfAnchor> expectedAnchors,
            out IReadOnlyList<AimShelfAnchor> resolvedAnchors,
            out string error)
        {
            List<AimShelfAnchor> resolved = new List<AimShelfAnchor>(3);
            resolvedAnchors = resolved;
            error = string.Empty;
            if (markers == null || expectedAnchors == null)
            {
                error = "marker or layout anchor collection is missing";
                return false;
            }

            Dictionary<int, AimShelfAnchor> expected = expectedAnchors.ToDictionary(anchor => anchor.SlotId);
            float faceClearanceX = left ? 0.25f : -0.25f;
            foreach (SlotBinding binding in left ? LeftBindings : RightBindings)
            {
                if (!markers.TryGetValue(binding.Marker, out MerWorldTransform marker))
                {
                    error = $"slot {binding.SlotId} marker '{binding.Marker}' is missing";
                    return false;
                }

                if (!expected.TryGetValue(binding.SlotId, out AimShelfAnchor authored))
                {
                    error = $"layout anchor {binding.SlotId} is missing";
                    return false;
                }

                MerWorldTransform pickup = new MerWorldTransform(
                    new Vector3(
                        marker.Position.x + faceClearanceX,
                        marker.Position.y + binding.HeightOffset,
                        marker.Position.z),
                    marker.Rotation,
                    new Vector3(1f, 1f, 1f));
                MerWorldTransform expectedTransform = new MerWorldTransform(
                    authored.LocalPosition,
                    authored.LocalRotation,
                    new Vector3(1f, 1f, 1f));
                if (!MerWorldTransformComposer.Approximately(
                        pickup,
                        expectedTransform,
                        PositionTolerance,
                        RotationToleranceDegrees))
                {
                    error = $"slot {binding.SlotId} marker-derived pickup {Format(pickup.Position)} does not match layout {Format(authored.LocalPosition)}";
                    return false;
                }

                resolved.Add(new AimShelfAnchor(binding.SlotId, pickup.Position, pickup.Rotation));
            }

            return true;
        }

        public static bool IsCarrierMountAligned(MerWorldTransform mount, Vector3 expectedPosition)
        {
            return MerWorldTransformComposer.Distance(mount.Position, expectedPosition) <= PositionTolerance;
        }

        private static string Format(Vector3 value) => $"({value.x:F3}, {value.y:F3}, {value.z:F3})";
    }
}
