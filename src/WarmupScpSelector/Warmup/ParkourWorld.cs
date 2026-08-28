using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;
using UnityEngine;
using WarmupScpSelector.Activities.Parkour;
using Logger = LabApi.Features.Console.Logger;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace WarmupScpSelector.Warmup
{
    /// <summary>
    /// Builds the Pulse Line inside the station's parkour shaft: the start block, every generated
    /// landing, the lit trace between them, the finish pad, and the reset coin.
    ///
    /// The shaft's own deck, bulkheads, and overhead come from <see cref="StationShellBuilder"/>; this
    /// only adds the course, so the route can be regenerated without touching the room.
    /// </summary>
    internal sealed class ParkourWorld
    {
        private const float LandingLipProud = 0.015f;
        private const float TraceThickness = 0.06f;

        private readonly List<AdminToy> _toys = new List<AdminToy>();
        private Pickup? _resetCoin;

        public ParkourLayout? Layout { get; private set; }

        public ushort ResetCoinSerial => _resetCoin?.Serial ?? 0;

        public bool IsSpawned { get; private set; }

        public bool Build(WarmupHallLayout hall, ParkourJumpModel model, bool chinese)
        {
            Despawn();
            try
            {
                ParkourLayout layout = new ParkourLayout(hall, model);
                Layout = layout;

                AddPad(layout.StartPlate.Center, layout.StartPlate.Size, StationPalette.DeckPanel, StationPalette.Guide);
                AddPad(layout.FinishPlate.Center, layout.FinishPlate.Size, StationPalette.DeckPanel, StationPalette.Signal);

                Vector3 previous = layout.StartPlate.Center;
                foreach (ParkourPlatform landing in layout.Platforms)
                {
                    Color lip = landing.GoldCut ? StationPalette.Gold : StationPalette.Cyan;
                    AddPad(landing.Center, landing.Size, StationPalette.DeckPanel, lip);
                    AddTrace(previous, landing.Center, lip);
                    previous = landing.Center;
                }

                AddTrace(previous, layout.FinishPlate.Center, StationPalette.Signal);
                AddShaftRunningLights(hall, layout);
                AddSignage(layout, chinese);

                _resetCoin = Pickup.Create(
                    ItemType.Coin,
                    layout.ResetCoinPosition,
                    Quaternion.Euler(90f, 0f, 0f),
                    Vector3.one * 3.2f,
                    networkSpawn: false);
                if (_resetCoin == null)
                {
                    throw new InvalidOperationException("parkour reset coin could not be created");
                }

                _resetCoin.IsLocked = false;
                _resetCoin.Spawn();
                if (_resetCoin.Rigidbody != null)
                {
                    _resetCoin.Rigidbody.isKinematic = true;
                }

                IsSpawned = true;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Parkour world startup failed: {ex.Message}");
                Despawn();
                return false;
            }
        }

        public void Despawn()
        {
            IsSpawned = false;
            try
            {
                _resetCoin?.Destroy();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Parkour reset-coin cleanup failed: {ex.Message}");
            }

            _resetCoin = null;
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
                    Logger.Warn($"[WarmupScpSelector] Parkour toy cleanup failed: {ex.Message}");
                }
            }

            _toys.Clear();
            Layout = null;
        }

        /// <summary>A landing: solid slab plus a thin emissive lip so its edges read against the void.</summary>
        private void AddPad(Vector3 center, Vector3 size, Color body, Color lip)
        {
            AddBox(center, size, body, collidable: true);
            AddBox(
                new Vector3(center.x, center.y + size.y / 2f + LandingLipProud, center.z),
                new Vector3(size.x - 0.1f, 0.03f, size.z - 0.1f),
                lip,
                collidable: false);
        }

        /// <summary>The lit trace the course is named for, drawn landing-to-landing through the shaft.</summary>
        private void AddTrace(Vector3 from, Vector3 to, Color color)
        {
            Vector3 start = new Vector3(from.x, from.y + 0.12f, from.z);
            Vector3 end = new Vector3(to.x, to.y + 0.12f, to.z);
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length < 0.05f)
            {
                return;
            }

            PrimitiveObjectToy toy = PrimitiveObjectToy.Create(
                Vector3.Lerp(start, end, 0.5f),
                Quaternion.LookRotation(delta.normalized, Vector3.up),
                new Vector3(TraceThickness, TraceThickness / 2f, length),
                networkSpawn: false);
            _toys.Add(toy);
            toy.Type = PrimitiveType.Cube;
            toy.Color = color;
            toy.Flags = PrimitiveFlags.Visible;
            toy.IsStatic = true;
            toy.Spawn();
        }

        /// <summary>
        /// Running lights climbing both shaft walls. They give the void a sense of scale and height that
        /// floating pads alone do not, and they cost one thin primitive per rung.
        /// </summary>
        private void AddShaftRunningLights(WarmupHallLayout hall, ParkourLayout layout)
        {
            StationZone shaft = hall.ParkourShaft;
            for (float z = shaft.MinZ + 5f; z < shaft.MaxZ - 2f; z += 6f)
            {
                float progress = Mathf.InverseLerp(shaft.MinZ, shaft.MaxZ, z);
                float y = Mathf.Lerp(1.6f, 10.4f, progress);
                foreach (float x in new[] { shaft.MinX + 0.22f, shaft.MaxX - 0.22f })
                {
                    AddBox(hall.World(x, y, z), new Vector3(0.06f, 0.9f, 0.12f), StationPalette.Cyan, collidable: false);
                }
            }

            _ = layout;
        }

        private void AddSignage(ParkourLayout layout, bool chinese)
        {
            AddLabel(layout.StartPlate.Center + new Vector3(0f, 2.6f, 0.3f),
                chinese ? "脉冲路线" : "PULSE LINE", 340f, 0.2f);
            AddLabel(layout.StartPlate.Center + new Vector3(0f, 2.05f, 0.3f),
                chinese ? "<color=#33EEDA>起点</color>" : "<color=#33EEDA>START</color>", 200f, 0.16f);
            AddLabel(layout.FinishPlate.Center + new Vector3(0f, 1.35f, 0.3f),
                chinese ? "<color=#5BFF80>终点</color>" : "<color=#5BFF80>FINISH</color>", 200f, 0.16f);
            AddLabel(layout.ResetCoinPosition + new Vector3(0f, 0.7f, 0f),
                chinese ? "<color=#FFB020>重置</color>" : "<color=#FFB020>RESET</color>", 180f, 0.14f);
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

        private void AddLabel(Vector3 center, string text, float width, float scale)
        {
            // Runners climb the shaft away from the hub, so they read these looking +Z.
            TextToy label = TextToy.Create(
                center, WarmupHallLayout.FacingViewer(Vector3.forward), Vector3.one * scale, networkSpawn: false);
            _toys.Add(label);
            label.TextFormat = "<align=center><b>" + text + "</b></align>";
            label.DisplaySize = new Vector2(width, 48f);
            label.IsStatic = true;
            label.Spawn();
        }
    }
}
