using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;
using UnityEngine;
using WarmupScpSelector.Activities.Parkour;
using Logger = LabApi.Features.Console.Logger;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace WarmupScpSelector.Warmup
{
    /// <summary>Static, low-cost parkour course fitted into the Aim hall's empty far-left wing.</summary>
    internal sealed class ParkourWorld
    {
        private static readonly Color PlatformColor = Hex("#D8D0B5");
        private static readonly Color TealColor = Hex("#33EEDA");
        private static readonly Color GoldColor = Hex("#FFD24D");
        private static readonly Color DividerColor = Hex("#202A3A");
        private static readonly Color FinishColor = Hex("#5BFF80");
        private readonly List<AdminToy> _toys = new List<AdminToy>();
        private Pickup? _resetCoin;

        public ParkourLayout? Layout { get; private set; }

        public ushort ResetCoinSerial => _resetCoin?.Serial ?? 0;

        public bool IsSpawned { get; private set; }

        public bool Build(AimRangeLayout aimLayout)
        {
            Despawn();
            try
            {
                Layout = new ParkourLayout(aimLayout);

                // Full-height partition keeps bot fire and target sightlines out of the movement course.
                AddBox(Layout.DividerCenter, Layout.DividerSize, DividerColor, true);
                AddBox(Layout.StartPlate.Center, Layout.StartPlate.Size, TealColor, true);
                AddBox(Layout.FinishPlate.Center, Layout.FinishPlate.Size, FinishColor, true);

                Vector3 previous = Layout.StartPlate.Center;
                foreach (ParkourPlatform platform in Layout.Platforms)
                {
                    AddBox(platform.Center, platform.Size, PlatformColor, true);
                    AddBox(
                        new Vector3(platform.Center.x, platform.SurfaceY + 0.015f, platform.Center.z),
                        new Vector3(platform.Size.x - 0.12f, 0.03f, platform.Size.z - 0.12f),
                        platform.GoldCut ? GoldColor : TealColor,
                        false);
                    AddRouteStrip(previous, platform.Center, platform.GoldCut ? GoldColor : TealColor);
                    previous = platform.Center;
                }

                AddRouteStrip(previous, Layout.FinishPlate.Center, TealColor);
                AddLabel(Layout.StartPlate.Center + new Vector3(0f, 2.25f, 0.25f), "PULSE LINE · 脉冲路线", 330f);
                AddLabel(Layout.StartPlate.Center + new Vector3(0f, 1.75f, 0.25f), "START · 起点", 185f);
                AddLabel(Layout.FinishPlate.Center + new Vector3(0f, 1.15f, 0.25f), "FIN · 终点", 155f);
                AddLabel(Layout.ResetCoinPosition + new Vector3(0f, 0.65f, 0f), "RESET · 重置", 155f);

                _resetCoin = Pickup.Create(ItemType.Coin, Layout.ResetCoinPosition, Quaternion.Euler(90f, 0f, 0f), Vector3.one * 3.2f, networkSpawn: false);
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

        private void AddRouteStrip(Vector3 from, Vector3 to, Color color)
        {
            Vector3 start = new Vector3(from.x, from.y + 0.14f, from.z);
            Vector3 end = new Vector3(to.x, to.y + 0.14f, to.z);
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length < 0.05f)
            {
                return;
            }

            Vector3 center = Vector3.Lerp(start, end, 0.5f);
            PrimitiveObjectToy toy = PrimitiveObjectToy.Create(
                center,
                Quaternion.LookRotation(delta.normalized, Vector3.up),
                new Vector3(0.07f, 0.025f, length),
                networkSpawn: false);
            _toys.Add(toy);
            toy.Type = PrimitiveType.Cube;
            toy.Color = color;
            toy.Flags = PrimitiveFlags.Visible;
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

        private void AddLabel(Vector3 center, string text, float width)
        {
            TextToy label = TextToy.Create(center, Quaternion.Euler(0f, 180f, 0f), Vector3.one * 0.16f, networkSpawn: false);
            _toys.Add(label);
            label.TextFormat = "<align=center><b>" + text + "</b></align>";
            label.DisplaySize = new Vector2(width, 48f);
            label.IsStatic = true;
            label.Spawn();
        }

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.magenta;
    }
}
