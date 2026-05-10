using System;
using System.Collections.Generic;
using System.Linq;
using Exiled.API.Enums;
using Exiled.API.Extensions;
using Exiled.API.Features;
using Exiled.API.Features.Pickups;
using Exiled.API.Features.Toys;
using Exiled.Events.EventArgs.Player;
using GameCore;
using MEC;
using PlayerRoles;
using UnityEngine;

namespace ScpslCustomRoomPlugin
{
    internal sealed class WarmupSelectionController
    {
        private readonly Plugin plugin;
        private readonly Dictionary<ushort, RoleTypeId> selectorCoins = new Dictionary<ushort, RoleTypeId>();
        private readonly Dictionary<string, RoleTypeId> playerSelections = new Dictionary<string, RoleTypeId>();
        private readonly List<AdminToy> spawnedToys = new List<AdminToy>();
        private readonly List<Pickup> spawnedPickups = new List<Pickup>();
        private readonly System.Random random = new System.Random();

        private CoroutineHandle countdownCoroutine;
        private CoroutineHandle playerMaintenanceCoroutine;
        private bool warmupActive;
        private bool roundSelectionPending;
        private bool countdownPausedLogged;
        private Vector3 roomOrigin;

        public static bool SuppressVanillaWaitingUi { get; private set; }

        public WarmupSelectionController(Plugin plugin)
        {
            this.plugin = plugin;
        }

        public void BeginWarmup()
        {
            CleanupRoom();
            playerSelections.Clear();
            selectorCoins.Clear();

            warmupActive = true;
            roundSelectionPending = false;
            SuppressVanillaWaitingUi = true;
            roomOrigin = VectorParser.ParseVector3(plugin.Config.RoomOrigin, new Vector3(0f, 1030f, 0f));

            if (plugin.Config.LockLobbyDuringWarmup)
            {
                Round.IsLobbyLocked = true;
            }

            BuildSelectionRoom();

            foreach (Player player in Player.List.Where(player => player.IsConnected && player.IsVerified))
            {
                MovePlayerToWarmupRoom(player);
            }

            countdownCoroutine = Timing.RunCoroutine(WarmupCountdown());
            playerMaintenanceCoroutine = Timing.RunCoroutine(MaintainWarmupPlayers());
            Log.Info("Warmup SCP class selector room is active.");
        }

        public void EndWarmup()
        {
            warmupActive = false;
            SuppressVanillaWaitingUi = false;
            Timing.KillCoroutines(countdownCoroutine);
            Timing.KillCoroutines(playerMaintenanceCoroutine);

            if (plugin.Config.LockLobbyDuringWarmup)
            {
                Round.IsLobbyLocked = false;
            }
        }

        public void CleanupRoom()
        {
            Timing.KillCoroutines(countdownCoroutine);
            Timing.KillCoroutines(playerMaintenanceCoroutine);
            SuppressVanillaWaitingUi = false;

            foreach (Pickup pickup in spawnedPickups)
            {
                pickup.Destroy();
            }

            foreach (AdminToy toy in spawnedToys)
            {
                toy.Destroy();
            }

            spawnedPickups.Clear();
            spawnedToys.Clear();
            selectorCoins.Clear();
            warmupActive = false;
            countdownPausedLogged = false;
        }

        public void OnVerified(VerifiedEventArgs ev)
        {
            if (!warmupActive)
            {
                return;
            }

            Log.Info($"{ev.Player.Nickname} ({ev.Player.UserId}) verified during warmup; moving to selector room.");
            Timing.CallDelayed(0.5f, () => MovePlayerToWarmupRoom(ev.Player));
        }

        public void OnLeft(LeftEventArgs ev)
        {
            playerSelections.Remove(GetPlayerKey(ev.Player));
        }

        public void OnSpawning(SpawningEventArgs ev)
        {
            if (!warmupActive || ev.NewRole != RoleTypeId.Tutorial)
            {
                return;
            }

            ev.Position = GetTutorialSpawnPosition();
        }

        public void OnPickingUpItem(PickingUpItemEventArgs ev)
        {
            if (!warmupActive || !selectorCoins.TryGetValue(ev.Pickup.Serial, out RoleTypeId selectedRole))
            {
                return;
            }

            ev.IsAllowed = false;
            string playerKey = GetPlayerKey(ev.Player);
            playerSelections[playerKey] = selectedRole;

            string roleName = selectedRole.GetFullName();
            Log.Info($"{ev.Player.Nickname} ({ev.Player.UserId}) selected {roleName} during warmup.");
            ev.Player.ShowHint($"Selected: {roleName}", 3f);
        }

        public void OnRoundStarted()
        {
            EndWarmup();
            roundSelectionPending = true;
            Timing.CallDelayed(plugin.Config.RoleSwapDelaySeconds, ApplySelectionsAfterVanillaAssignment);
        }

        private void BuildSelectionRoom()
        {
            AddPrimitive(roomOrigin + new Vector3(0f, -0.15f, 0f), new Vector3(24f, 0.3f, 18f), new Color(0.16f, 0.16f, 0.18f, 1f));
            AddPrimitive(roomOrigin + new Vector3(0f, 3.1f, 8.8f), new Vector3(24f, 6.5f, 0.3f), new Color(0.08f, 0.08f, 0.1f, 1f));
            AddPrimitive(roomOrigin + new Vector3(0f, 3.1f, -8.8f), new Vector3(24f, 6.5f, 0.3f), new Color(0.08f, 0.08f, 0.1f, 1f));
            AddPrimitive(roomOrigin + new Vector3(-11.8f, 3.1f, 0f), new Vector3(0.3f, 6.5f, 18f), new Color(0.08f, 0.08f, 0.1f, 1f));
            AddPrimitive(roomOrigin + new Vector3(11.8f, 3.1f, 0f), new Vector3(0.3f, 6.5f, 18f), new Color(0.08f, 0.08f, 0.1f, 1f));

            Vector2 displaySize = VectorParser.ParseVector2(plugin.Config.FloatingTextDisplaySize, new Vector2(2.5f, 1f));
            foreach (ScpClassOption option in plugin.Config.ScpClassOptions.Where(option => option.Role.IsScp()))
            {
                Vector3 textPosition = roomOrigin + VectorParser.ParseVector3(option.TextOffset, Vector3.zero);
                Text text = Text.Create(textPosition, Quaternion.Euler(0f, 180f, 0f), Vector3.one, option.Label, displaySize, null, true);
                spawnedToys.Add(text);

                Vector3 coinPosition = roomOrigin + VectorParser.ParseVector3(option.CoinOffset, Vector3.zero);
                Pickup coin = Pickup.CreateAndSpawn(plugin.Config.SelectorItemType, coinPosition, Quaternion.identity, null);
                coin.IsLocked = false;
                spawnedPickups.Add(coin);
                selectorCoins[coin.Serial] = option.Role;
            }
        }

        private void AddPrimitive(Vector3 position, Vector3 scale, Color color)
        {
            Primitive primitive = Primitive.Create(PrimitiveType.Cube, position, Vector3.zero, scale, true, color);
            primitive.Collidable = true;
            primitive.Visible = true;
            primitive.IsStatic = true;
            spawnedToys.Add(primitive);
        }

        private void MovePlayerToWarmupRoom(Player player)
        {
            if (!player.IsConnected)
            {
                return;
            }

            if (player.Role.Type != RoleTypeId.Tutorial)
            {
                player.Role.Set(RoleTypeId.Tutorial, SpawnReason.ForceClass);
            }

            Timing.CallDelayed(0.1f, () =>
            {
                if (player.IsConnected && warmupActive)
                {
                    player.ClearInventory();
                    player.Position = GetTutorialSpawnPosition();
                    player.ShowHint("Choose an SCP class by interacting with its coin.", 4f);
                    Log.Debug($"Moved {player.Nickname} ({player.UserId}) to selector room at {player.Position}.");
                }
            });
        }

        private IEnumerator<float> MaintainWarmupPlayers()
        {
            while (warmupActive)
            {
                foreach (Player player in Player.List.Where(player => player.IsConnected && player.IsVerified))
                {
                    if (player.Role.Type != RoleTypeId.Tutorial || Vector3.Distance(player.Position, GetTutorialSpawnPosition()) > 3f)
                    {
                        MovePlayerToWarmupRoom(player);
                    }
                }

                yield return Timing.WaitForSeconds(1f);
            }
        }

        private IEnumerator<float> WarmupCountdown()
        {
            int remainingSeconds = Math.Max(1, plugin.Config.WarmupSeconds);

            while (warmupActive && remainingSeconds > 0)
            {
                int readyPlayers = ReadyWarmupPlayerCount();
                int minimumPlayers = Math.Max(1, plugin.Config.MinimumPlayersToCountdown);

                if (readyPlayers < minimumPlayers)
                {
                    SetNativeLobbyTimer(-2);
                    if (!countdownPausedLogged)
                    {
                        Log.Info($"Warmup countdown paused; waiting for {minimumPlayers} verified players ({readyPlayers}/{minimumPlayers}).");
                        countdownPausedLogged = true;
                    }

                    foreach (Player player in Player.List.Where(player => player.IsConnected && player.IsVerified))
                    {
                        player.ShowHint($"Waiting for players ({readyPlayers}/{minimumPlayers})\nChoose an SCP class in the selector room.", 1.25f);
                    }

                    yield return Timing.WaitForSeconds(1f);
                    continue;
                }

                if (countdownPausedLogged)
                {
                    Log.Info($"Warmup countdown resumed with {readyPlayers} verified players.");
                    countdownPausedLogged = false;
                }

                SetNativeLobbyTimer((short)Math.Min(short.MaxValue, remainingSeconds));

                if (plugin.Config.ShowCountdownHints)
                {
                    string message = $"Round starts in {remainingSeconds}s\nChoose an SCP class in the selector room.";
                    foreach (Player player in Player.List.Where(player => player.IsConnected && player.IsVerified))
                    {
                        player.ShowHint(message, 1.25f);
                    }
                }

                yield return Timing.WaitForSeconds(1f);
                remainingSeconds--;
            }

            if (!warmupActive)
            {
                yield break;
            }

            if (plugin.Config.LockLobbyDuringWarmup)
            {
                Round.IsLobbyLocked = false;
            }

            Round.Start();
        }

        private void ApplySelectionsAfterVanillaAssignment()
        {
            if (!roundSelectionPending)
            {
                return;
            }

            roundSelectionPending = false;
            CleanupRoom();

            Dictionary<RoleTypeId, List<Player>> selectedPools = BuildSelectedPools();
            if (selectedPools.Count == 0)
            {
                Log.Info("No SCP class selections were made during warmup.");
                return;
            }

            Dictionary<Player, RoleTypeId> currentRoles = Player.List
                .Where(player => player.IsConnected)
                .ToDictionary(player => player, player => player.Role.Type);

            HashSet<Player> alreadyChosen = new HashSet<Player>();

            foreach (ScpClassOption option in plugin.Config.ScpClassOptions.Where(option => option.Role.IsScp()))
            {
                RoleTypeId targetRole = option.Role;
                if (!selectedPools.TryGetValue(targetRole, out List<Player> pool))
                {
                    continue;
                }

                List<Player> currentHolders = currentRoles
                    .Where(pair => pair.Value == targetRole && pair.Key.IsConnected)
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (Player holder in currentHolders)
                {
                    Player? selectedPlayer = ChooseSelectedPlayer(pool, alreadyChosen, holder);
                    if (selectedPlayer is null)
                    {
                        break;
                    }

                    alreadyChosen.Add(selectedPlayer);

                    if (selectedPlayer == holder)
                    {
                        Log.Info($"{holder.Nickname} naturally received their selected {targetRole.GetFullName()} role.");
                        continue;
                    }

                    RoleTypeId replacementRole = selectedPlayer.Role.Type;
                    holder.Role.Set(replacementRole, SpawnReason.ForceClass);
                    selectedPlayer.Role.Set(targetRole, SpawnReason.ForceClass);

                    Log.Info($"{selectedPlayer.Nickname} was selected from the {targetRole.GetFullName()} pool and swapped with {holder.Nickname}.");
                }
            }
        }

        private Dictionary<RoleTypeId, List<Player>> BuildSelectedPools()
        {
            Dictionary<RoleTypeId, List<Player>> selectedPools = new Dictionary<RoleTypeId, List<Player>>();

            foreach (KeyValuePair<string, RoleTypeId> selection in playerSelections)
            {
                Player player = Player.List.FirstOrDefault(candidate => candidate.IsConnected && GetPlayerKey(candidate) == selection.Key);
                if (player is null)
                {
                    continue;
                }

                if (!selectedPools.TryGetValue(selection.Value, out List<Player> players))
                {
                    players = new List<Player>();
                    selectedPools[selection.Value] = players;
                }

                players.Add(player);
            }

            return selectedPools;
        }

        private Player? ChooseSelectedPlayer(List<Player> pool, HashSet<Player> alreadyChosen, Player currentHolder)
        {
            if (pool.Contains(currentHolder) && !alreadyChosen.Contains(currentHolder))
            {
                return currentHolder;
            }

            List<Player> candidates = pool
                .Where(player => player.IsConnected && !alreadyChosen.Contains(player))
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            return candidates[random.Next(candidates.Count)];
        }

        private Vector3 GetTutorialSpawnPosition()
        {
            return roomOrigin + VectorParser.ParseVector3(plugin.Config.TutorialSpawnOffset, new Vector3(0f, 1.2f, -7f));
        }

        private static int ReadyWarmupPlayerCount()
        {
            return Player.List.Count(player => player.IsConnected && player.IsVerified);
        }

        private static void SetNativeLobbyTimer(short value)
        {
            if (RoundStart.singleton is not null && !RoundStart.RoundStarted)
            {
                RoundStart.singleton.NetworkTimer = value;
            }
        }

        private static string GetPlayerKey(Player player)
        {
            return string.IsNullOrWhiteSpace(player.UserId) ? player.Id.ToString() : player.UserId;
        }
    }
}
