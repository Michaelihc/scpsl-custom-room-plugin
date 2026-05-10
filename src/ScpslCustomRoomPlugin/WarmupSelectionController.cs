using System;
using System.Collections.Generic;
using System.Linq;
using AdminToys;
using Exiled.API.Enums;
using Exiled.API.Extensions;
using Exiled.API.Features;
using Exiled.API.Features.Pickups;
using Exiled.API.Features.Toys;
using Exiled.API.Features.Doors;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using GameCore;
using MEC;
using PlayerRoles;
using UnityEngine;
using LabPrimitive = LabApi.Features.Wrappers.PrimitiveObjectToy;

namespace ScpslCustomRoomPlugin
{
    internal sealed class WarmupSelectionController
    {
        private readonly Plugin plugin;
        private readonly Dictionary<ushort, RoleTypeId> selectorCoins = new Dictionary<ushort, RoleTypeId>();
        private readonly Dictionary<string, RoleTypeId> playerSelections = new Dictionary<string, RoleTypeId>();
        private readonly Dictionary<string, RoleTypeId> vanillaRoleAssignments = new Dictionary<string, RoleTypeId>();
        private readonly List<AdminToy> spawnedToys = new List<AdminToy>();
        private readonly List<LabPrimitive> spawnedPrimitives = new List<LabPrimitive>();
        private readonly List<Pickup> spawnedPickups = new List<Pickup>();
        private readonly System.Random random = new System.Random();

        private CoroutineHandle countdownCoroutine;
        private CoroutineHandle playerMaintenanceCoroutine;
        private bool warmupActive;
        private bool roundSelectionPending;
        private bool pendingForcedWarmupRoundStart;
        private int forceRoundStartAttempts;
        private bool countdownPausedLogged;
        private bool releasedPlayersForRoundStart;
        private bool applyingSelectionSwaps;
        private bool selectionApplyScheduled;
        private Vector3 roomOrigin;
        private Quaternion roomRotation = Quaternion.identity;
        private Door? lockedConnectorDoor;
        private Door? openedAnchorDoor;

        public WarmupSelectionController(Plugin plugin)
        {
            this.plugin = plugin;
        }

        public void BeginWarmup()
        {
            if (plugin.Config.ForceRoundStartForWarmup && !IsRoundStarted())
            {
                if (ReadyWarmupPlayerCount() == 0)
                {
                    pendingForcedWarmupRoundStart = true;
                    forceRoundStartAttempts = 0;
                    Log.Info("Waiting for a verified player before force-starting selector warmup.");
                    return;
                }

                ForceStartWarmupRound("waiting for players");
                return;
            }

            StartWarmup("waiting for players");
        }

        private void ForceStartWarmupRound(string reason)
        {
            pendingForcedWarmupRoundStart = true;
            forceRoundStartAttempts++;
            Log.Info($"Force-starting a warmup round to avoid vanilla waiting-for-players UI ({reason}, attempt {forceRoundStartAttempts}).");
            SetNativeLobbyTimer(-1);
            Timing.CallDelayed(1.5f, () =>
            {
                if (!pendingForcedWarmupRoundStart)
                {
                    return;
                }

                if (!IsRoundStarted())
                {
                    if (forceRoundStartAttempts >= 5)
                    {
                        Log.Error("Forced warmup round did not start after 5 attempts; selector warmup is still pending.");
                        return;
                    }

                    Log.Warn("Forced warmup round has not started yet; retrying native timer start.");
                    ForceStartWarmupRound(reason);
                    return;
                }

                pendingForcedWarmupRoundStart = false;
                forceRoundStartAttempts = 0;
                StartWarmupAfterRoleCapture($"{reason}; delayed forced round start");
            });
        }

        private void StartWarmupAfterRoleCapture(string reason)
        {
            try
            {
                CaptureVanillaRoleAssignments();
                StartWarmup(reason);
            }
            catch (Exception exception)
            {
                Log.Error($"Failed to start selector warmup ({reason}): {exception}");
                CleanupRoom();
            }
        }

        private void StartWarmup(string reason)
        {
            CleanupRoom();
            playerSelections.Clear();
            selectorCoins.Clear();

            warmupActive = true;
            roundSelectionPending = false;
            releasedPlayersForRoundStart = false;
            applyingSelectionSwaps = false;
            selectionApplyScheduled = false;
            ResolveRoomOrigin();

            if (plugin.Config.LockLobbyDuringWarmup)
            {
                Round.IsLobbyLocked = true;
                Log.Warn("LockLobbyDuringWarmup is enabled; the game's native lobby countdown will stay paused until warmup ends.");
            }

            LockWarmupDoors();
            HideNativeWaitingUi();
            BuildSelectionRoom();
            Log.Info($"Selector room spawned {spawnedPrimitives.Count} collidable primitive(s), {spawnedToys.Count} text toy(s), and {spawnedPickups.Count} selector pickup(s).");

            foreach (Player player in Player.List.Where(IsWarmupParticipant))
            {
                MovePlayerToWarmupRoom(player);
            }

            Log.Info($"Warmup SCP class selector room is active ({reason}); using the game's native lobby countdown.");
            countdownCoroutine = Timing.RunCoroutine(WarmupCountdown());
            playerMaintenanceCoroutine = Timing.RunCoroutine(MaintainWarmupPlayers());
        }

        public void EndWarmup()
        {
            warmupActive = false;
            releasedPlayersForRoundStart = false;
            applyingSelectionSwaps = false;
            Timing.KillCoroutines(countdownCoroutine);
            Timing.KillCoroutines(playerMaintenanceCoroutine);

            if (plugin.Config.LockLobbyDuringWarmup)
            {
                Round.IsLobbyLocked = false;
            }

            CloseWarmupAnchorDoor();
            UnlockWarmupDoors();
        }

        public void CleanupRoom()
        {
            Timing.KillCoroutines(countdownCoroutine);
            Timing.KillCoroutines(playerMaintenanceCoroutine);

            foreach (Pickup pickup in spawnedPickups)
            {
                pickup.Destroy();
            }

            foreach (LabPrimitive primitive in spawnedPrimitives)
            {
                if (!primitive.IsDestroyed)
                {
                    primitive.Destroy();
                }
            }

            foreach (AdminToy toy in spawnedToys)
            {
                toy.Destroy();
            }

            spawnedPrimitives.Clear();
            spawnedPickups.Clear();
            spawnedToys.Clear();
            selectorCoins.Clear();
            CloseWarmupAnchorDoor();
            UnlockWarmupDoors();
            warmupActive = false;
            roundSelectionPending = false;
            countdownPausedLogged = false;
            releasedPlayersForRoundStart = false;
            applyingSelectionSwaps = false;
            selectionApplyScheduled = false;
        }

        public void OnVerified(VerifiedEventArgs ev)
        {
            if (pendingForcedWarmupRoundStart && !warmupActive && plugin.Config.ForceRoundStartForWarmup)
            {
                Log.Info($"{ev.Player.Nickname} ({ev.Player.UserId}) verified; starting forced selector warmup round.");
                Timing.CallDelayed(1.5f, () =>
                {
                    if (pendingForcedWarmupRoundStart && !warmupActive && !IsRoundStarted())
                    {
                        ForceStartWarmupRound("first verified player");
                    }
                });
                return;
            }

            if (!warmupActive)
            {
                return;
            }

            if (releasedPlayersForRoundStart)
            {
                MovePlayerToSpectatorForRoundStart(ev.Player);
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

        public void OnChangingRole(ChangingRoleEventArgs ev)
        {
            if (applyingSelectionSwaps || (!releasedPlayersForRoundStart && !roundSelectionPending && !IsRoundStarted()))
            {
                return;
            }

            if (ev.NewRole is RoleTypeId.None or RoleTypeId.Spectator or RoleTypeId.Tutorial)
            {
                return;
            }

            vanillaRoleAssignments[GetPlayerKey(ev.Player)] = ev.NewRole;
            Log.Debug($"Captured vanilla role assignment for {ev.Player.Nickname}: {ev.NewRole.GetFullName()}.");
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
            ev.Player.ShowHint(BuildWarmupStatusHint(ev.Player, GetCurrentCountdownLine()), 1.25f);
        }

        public void OnRoundStarted()
        {
            Log.Info($"RoundStarted event received (warmupActive={warmupActive}, released={releasedPlayersForRoundStart}, pending={roundSelectionPending}).");

            if (pendingForcedWarmupRoundStart)
            {
                pendingForcedWarmupRoundStart = false;
                forceRoundStartAttempts = 0;
                Timing.CallDelayed(plugin.Config.RoleSwapDelaySeconds, () =>
                {
                    StartWarmupAfterRoleCapture("forced round started");
                });
                return;
            }

            if (!warmupActive)
            {
                return;
            }

            QueueSelectionApply("round started");
        }

        public void OnAllPlayersSpawned()
        {
            Log.Info($"AllPlayersSpawned event received (warmupActive={warmupActive}, released={releasedPlayersForRoundStart}, pending={roundSelectionPending}).");

            if (!warmupActive && !releasedPlayersForRoundStart && !roundSelectionPending)
            {
                return;
            }

            QueueSelectionApply("all players spawned");
        }

        public void OnEndingRound(EndingRoundEventArgs ev)
        {
            if (warmupActive && plugin.Config.SuppressRoundEndDuringWarmup)
            {
                ev.IsAllowed = false;
            }
        }

        private void BuildSelectionRoom()
        {
            if (!plugin.Config.UseExistingLobbyRoom)
            {
                AddPrimitive(TransformLobbyOffset(new Vector3(0f, -0.15f, 0f)), new Vector3(24f, 0.3f, 18f), new Color(0.16f, 0.16f, 0.18f, 1f));
                AddPrimitive(TransformLobbyOffset(new Vector3(0f, -0.7f, 0f)), new Vector3(30f, 0.6f, 24f), new Color(0.16f, 0.16f, 0.18f, 1f));
                AddPrimitive(TransformLobbyOffset(new Vector3(0f, 3.1f, 8.8f)), new Vector3(24f, 6.5f, 0.3f), new Color(0.08f, 0.08f, 0.1f, 1f));
                AddPrimitive(TransformLobbyOffset(new Vector3(0f, 3.1f, -8.8f)), new Vector3(24f, 6.5f, 0.3f), new Color(0.08f, 0.08f, 0.1f, 1f));
                AddPrimitive(TransformLobbyOffset(new Vector3(-11.8f, 3.1f, 0f)), new Vector3(0.3f, 6.5f, 18f), new Color(0.08f, 0.08f, 0.1f, 1f));
                AddPrimitive(TransformLobbyOffset(new Vector3(11.8f, 3.1f, 0f)), new Vector3(0.3f, 6.5f, 18f), new Color(0.08f, 0.08f, 0.1f, 1f));
            }

            Vector2 displaySize = VectorParser.ParseVector2(plugin.Config.FloatingTextDisplaySize, new Vector2(2.5f, 1f));
            Vector3 coinScale = VectorParser.ParseVector3(plugin.Config.SelectorCoinScale, new Vector3(2.5f, 2.5f, 2.5f));
            foreach (ScpClassOption option in plugin.Config.ScpClassOptions.Where(option => option.Role.IsScp()))
            {
                Vector3 textPosition = TransformLobbyOffset(VectorParser.ParseVector3(option.TextOffset, Vector3.zero));
                Text text = Text.Create(textPosition, roomRotation * Quaternion.Euler(0f, 180f, 0f), Vector3.one, option.Label, displaySize, null, true);
                spawnedToys.Add(text);

                Vector3 coinPosition = TransformLobbyOffset(VectorParser.ParseVector3(option.CoinOffset, Vector3.zero));
                Pickup coin = Pickup.CreateAndSpawn(plugin.Config.SelectorItemType, coinPosition, roomRotation, null);
                coin.IsLocked = false;
                coin.Scale = coinScale;
                spawnedPickups.Add(coin);
                selectorCoins[coin.Serial] = option.Role;
            }
        }

        private void AddPrimitive(Vector3 position, Vector3 scale, Color color)
        {
            LabPrimitive primitive = LabPrimitive.Create(position, Quaternion.identity, scale);
            primitive.Type = PrimitiveType.Cube;
            primitive.Color = color;
            primitive.Flags = PrimitiveFlags.Collidable | PrimitiveFlags.Visible;
            spawnedPrimitives.Add(primitive);
        }

        private void MovePlayerToWarmupRoom(Player player)
        {
            if (!player.IsConnected || releasedPlayersForRoundStart)
            {
                return;
            }

            if (player.Role.Type != RoleTypeId.Tutorial)
            {
                player.Role.Set(RoleTypeId.Tutorial, SpawnReason.ForceClass);
            }

            Timing.CallDelayed(0.1f, () =>
            {
                if (player.IsConnected && warmupActive && !releasedPlayersForRoundStart)
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
            while (warmupActive && !releasedPlayersForRoundStart)
            {
                foreach (Player player in Player.List.Where(IsWarmupParticipant))
                {
                    if (player.Role.Type != RoleTypeId.Tutorial || Vector3.Distance(player.Position, roomOrigin) > 24f)
                    {
                        MovePlayerToWarmupRoom(player);
                    }
                }

                yield return Timing.WaitForSeconds(1f);
            }
        }

        private IEnumerator<float> WarmupCountdown()
        {
            short lastLoggedTimer = short.MinValue;

            while (warmupActive)
            {
                if (IsRoundStarted())
                {
                    QueueSelectionApply("native round start observed");
                    yield break;
                }

                int playerCount = CurrentServerPlayerCount();
                int maxPlayers = Server.MaxPlayerCount > 0 ? Server.MaxPlayerCount : Math.Max(playerCount, 1);
                short nativeTimer = GetNativeLobbyTimer();
                string countdownLine = BuildCountdownLine(nativeTimer, playerCount, maxPlayers);

                if (nativeTimer == -2)
                {
                    if (!countdownPausedLogged)
                    {
                        Log.Info($"Native lobby countdown paused; waiting for players ({playerCount}/{maxPlayers}).");
                        countdownPausedLogged = true;
                    }

                    ShowWarmupStatusHint(countdownLine);
                    yield return Timing.WaitForSeconds(1f);
                    continue;
                }

                if (countdownPausedLogged)
                {
                    Log.Info($"Native lobby countdown resumed with {playerCount}/{maxPlayers} players.");
                    countdownPausedLogged = false;
                }

                if (nativeTimer != lastLoggedTimer && nativeTimer >= 0 && nativeTimer % 10 == 0)
                {
                    Log.Debug($"Native lobby countdown timer is {nativeTimer}s with {playerCount}/{maxPlayers} players.");
                    lastLoggedTimer = nativeTimer;
                }

                if (nativeTimer is >= 0 and <= 1)
                {
                    ReleaseWarmupPlayersForVanillaAssignment();
                }

                ShowWarmupStatusHint(countdownLine);

                yield return Timing.WaitForSeconds(1f);
            }
        }

        private void ReleaseWarmupPlayersForVanillaAssignment()
        {
            if (releasedPlayersForRoundStart)
            {
                return;
            }

            releasedPlayersForRoundStart = true;
            Timing.KillCoroutines(playerMaintenanceCoroutine);

            foreach (Player player in Player.List.Where(IsWarmupParticipant))
            {
                MovePlayerToSpectatorForRoundStart(player);
            }

            Log.Info("Released warmup players to spectator for vanilla round role assignment.");
        }

        private void QueueSelectionApply(string reason)
        {
            if (selectionApplyScheduled)
            {
                Log.Debug($"Selector swap application is already queued; ignoring {reason} trigger.");
                return;
            }

            if (warmupActive || releasedPlayersForRoundStart)
            {
                EndWarmup();
            }

            roundSelectionPending = true;
            selectionApplyScheduled = true;
            Log.Info($"Queued selector swap application ({reason}) in {plugin.Config.RoleSwapDelaySeconds:0.##}s.");
            Timing.CallDelayed(plugin.Config.RoleSwapDelaySeconds, ApplySelectionsAfterVanillaAssignment);
        }

        private void MovePlayerToSpectatorForRoundStart(Player player)
        {
            if (!IsWarmupParticipant(player))
            {
                return;
            }

            player.Role.Set(RoleTypeId.Spectator, SpawnReason.ForceClass);
        }

        private void ApplySelectionsAfterVanillaAssignment()
        {
            if (!roundSelectionPending)
            {
                return;
            }

            roundSelectionPending = false;
            selectionApplyScheduled = false;
            CleanupRoom();

            Dictionary<RoleTypeId, List<Player>> selectedPools = BuildSelectedPools();
            Dictionary<Player, RoleTypeId> originalRoles = BuildVanillaRoleAssignments();
            Dictionary<Player, RoleTypeId> finalRoles = new Dictionary<Player, RoleTypeId>(originalRoles);
            Log.Info($"Applying selector swaps with {selectedPools.Sum(pair => pair.Value.Count)} selected player(s) and {originalRoles.Count} resolved vanilla role(s).");
            Log.Info($"Resolved vanilla SCP distribution: {FormatScpRoleDistribution(originalRoles)}.");
            if (selectedPools.Count == 0)
            {
                Log.Info("No SCP class selections were made during warmup.");
                return;
            }

            List<RoleTypeId> roleOrder = plugin.Config.ScpClassOptions
                .Where(option => option.Role.IsScp())
                .Select(option => option.Role)
                .ToList();

            Dictionary<RoleTypeId, IReadOnlyList<Player>> plannerPools = selectedPools
                .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<Player>)pair.Value);
            SelectionSwapPlan<Player> plan = SelectionSwapPlanner.BuildPlan(
                roleOrder,
                originalRoles,
                plannerPools,
                candidates => candidates[random.Next(candidates.Count)]);

            foreach (RoleTypeId skippedRole in plan.SkippedUnspawnedRoles)
            {
                Log.Info($"Skipping {skippedRole.GetFullName()} selections because vanilla round start did not assign that SCP role. Available SCP roles: {FormatScpRoleDistribution(originalRoles)}.");
            }

            foreach (SelectionNatural<Player> naturalSelection in plan.NaturalSelections)
            {
                Log.Info($"{naturalSelection.Player.Nickname} naturally received their selected {naturalSelection.TargetRole.GetFullName()} role.");
            }

            foreach (SelectionUnresolved<Player> unresolvedSelection in plan.UnresolvedSelections)
            {
                Log.Warn($"Skipping {unresolvedSelection.SelectedPlayer.Nickname}'s {unresolvedSelection.TargetRole.GetFullName()} swap because their vanilla role could not be resolved.");
            }

            foreach (SelectionSwap<Player> swap in plan.Swaps)
            {
                Log.Info($"{swap.SelectedPlayer.Nickname} was selected from the {swap.TargetRole.GetFullName()} pool and swapped with {swap.Holder.Nickname}.");
            }

            ApplyChangedRoles(originalRoles, plan.FinalRoles);
        }

        private Dictionary<RoleTypeId, List<Player>> BuildSelectedPools()
        {
            Dictionary<RoleTypeId, List<Player>> selectedPools = new Dictionary<RoleTypeId, List<Player>>();

            foreach (KeyValuePair<string, RoleTypeId> selection in playerSelections)
            {
                Player player = Player.List.FirstOrDefault(candidate => IsWarmupParticipant(candidate) && GetPlayerKey(candidate) == selection.Key);
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

        private void CaptureVanillaRoleAssignments()
        {
            vanillaRoleAssignments.Clear();

            foreach (Player player in Player.List.Where(IsWarmupParticipant))
            {
                RoleTypeId role = player.Role.Type;
                if (role is RoleTypeId.None or RoleTypeId.Spectator or RoleTypeId.Tutorial)
                {
                    continue;
                }

                vanillaRoleAssignments[GetPlayerKey(player)] = role;
            }

            Log.Info($"Captured {vanillaRoleAssignments.Count} vanilla role assignment(s) before selector warmup.");
        }

        private Dictionary<Player, RoleTypeId> BuildVanillaRoleAssignments()
        {
            Dictionary<Player, RoleTypeId> originalRoles = new Dictionary<Player, RoleTypeId>();

            foreach (Player player in Player.List.Where(IsWarmupParticipant))
            {
                RoleTypeId currentRole = player.Role.Type;
                vanillaRoleAssignments.TryGetValue(GetPlayerKey(player), out RoleTypeId capturedRole);
                RoleTypeId? fallbackRole = vanillaRoleAssignments.ContainsKey(GetPlayerKey(player)) ? capturedRole : null;

                if (VanillaRoleAssignmentResolver.TryResolve(currentRole, fallbackRole, out RoleTypeId resolvedRole))
                {
                    originalRoles[player] = resolvedRole;
                }
            }

            return originalRoles;
        }

        private void ApplyChangedRoles(Dictionary<Player, RoleTypeId> originalRoles, Dictionary<Player, RoleTypeId> finalRoles)
        {
            applyingSelectionSwaps = true;
            try
            {
                foreach (KeyValuePair<Player, RoleTypeId> finalRole in finalRoles)
                {
                    if (!originalRoles.TryGetValue(finalRole.Key, out RoleTypeId originalRole) || originalRole == finalRole.Value)
                    {
                        continue;
                    }

                    finalRole.Key.Role.Set(finalRole.Value, SpawnReason.ForceClass);
                }
            }
            finally
            {
                applyingSelectionSwaps = false;
            }
        }

        private static string FormatScpRoleDistribution(Dictionary<Player, RoleTypeId> roles)
        {
            List<string> roleCounts = roles
                .Where(pair => pair.Value.IsScp())
                .GroupBy(pair => pair.Value)
                .OrderBy(group => group.Key.ToString())
                .Select(group => $"{group.Key.GetFullName()}={group.Count()}")
                .ToList();

            return roleCounts.Count == 0 ? "none" : string.Join(", ", roleCounts);
        }

        private Vector3 GetTutorialSpawnPosition()
        {
            return TransformLobbyOffset(VectorParser.ParseVector3(plugin.Config.TutorialSpawnOffset, new Vector3(0f, 1.2f, -7f)));
        }

        private void ResolveRoomOrigin()
        {
            if (!plugin.Config.UseExistingLobbyRoom)
            {
                roomOrigin = VectorParser.ParseVector3(plugin.Config.RoomOrigin, new Vector3(0f, 1030f, 0f));
                roomRotation = Quaternion.identity;
                Log.Info($"Using floating selector room at {roomOrigin}.");
                return;
            }

            Room? lobbyRoom = Room.Get(plugin.Config.ExistingLobbyRoomType) ?? Room.Get(RoomType.Lcz173);
            if (lobbyRoom is null)
            {
                roomOrigin = VectorParser.ParseVector3(plugin.Config.RoomOrigin, new Vector3(0f, 1030f, 0f));
                roomRotation = Quaternion.identity;
                Log.Warn($"Existing lobby room {plugin.Config.ExistingLobbyRoomType} was not found; falling back to floating selector room at {roomOrigin}.");
                return;
            }

            Door? anchorDoor = Door.Get(plugin.Config.ExistingLobbyAnchorDoorType);
            if (anchorDoor is not null)
            {
                OpenWarmupAnchorDoor(anchorDoor);
                Vector3 anchorOffset = VectorParser.ParseVector3(plugin.Config.ExistingLobbyAnchorDoorOffset, new Vector3(0f, 0f, -2f));
                roomOrigin = anchorDoor.Transform.TransformPoint(anchorOffset);
                roomRotation = anchorDoor.Rotation;
                Log.Info($"Using existing selector lobby anchor door {anchorDoor.Type} at {roomOrigin}.");
                return;
            }

            Vector3 localOffset = VectorParser.ParseVector3(plugin.Config.ExistingLobbyRoomOffset, new Vector3(17f, 13f, 8f));
            Vector3 localRotation = VectorParser.ParseVector3(plugin.Config.ExistingLobbyRoomRotation, new Vector3(0f, -90f, 0f));
            roomOrigin = lobbyRoom.Transform.TransformPoint(localOffset);
            roomRotation = lobbyRoom.Rotation * Quaternion.Euler(localRotation);
            Log.Info($"Using existing selector lobby room {lobbyRoom.Type} at {roomOrigin}.");
        }

        private Vector3 TransformLobbyOffset(Vector3 localOffset)
        {
            return roomOrigin + (roomRotation * localOffset);
        }

        private void HideNativeWaitingUi()
        {
            if (!plugin.Config.HideNativeWaitingUi)
            {
                return;
            }

            GameObject startRound = GameObject.Find("StartRound");
            if (startRound is null)
            {
                Log.Debug("Native StartRound UI object was not found.");
                return;
            }

            startRound.transform.localScale = Vector3.zero;
        }

        private void LockWarmupDoors()
        {
            if (!plugin.Config.LockScp173ConnectorDuringWarmup)
            {
                return;
            }

            lockedConnectorDoor = Door.Get(DoorType.Scp173Connector);
            if (lockedConnectorDoor is null)
            {
                Log.Warn("Could not find SCP-173 connector door to lock.");
                return;
            }

            lockedConnectorDoor.IsOpen = false;
            lockedConnectorDoor.Lock(DoorLockType.AdminCommand);
            Log.Info("Locked SCP-173 connector door for selector lobby.");
        }

        private void OpenWarmupAnchorDoor(Door anchorDoor)
        {
            if (!plugin.Config.OpenExistingLobbyAnchorDoor)
            {
                return;
            }

            openedAnchorDoor = anchorDoor;
            openedAnchorDoor.Unlock();
            openedAnchorDoor.IsOpen = true;
            Log.Info($"Opened selector lobby anchor door {openedAnchorDoor.Type}.");
        }

        private void CloseWarmupAnchorDoor()
        {
            if (openedAnchorDoor is null)
            {
                return;
            }

            openedAnchorDoor.IsOpen = false;
            openedAnchorDoor = null;
        }

        private void UnlockWarmupDoors()
        {
            if (lockedConnectorDoor is null)
            {
                return;
            }

            lockedConnectorDoor.Unlock();
            lockedConnectorDoor = null;
        }

        private static int ReadyWarmupPlayerCount()
        {
            return Player.List.Count(IsWarmupParticipant);
        }

        private static int CurrentServerPlayerCount()
        {
            return Server.PlayerCount;
        }

        private void ShowWarmupStatusHint(string countdownLine)
        {
            if (!plugin.Config.ShowCountdownHints)
            {
                return;
            }

            foreach (Player player in Player.List.Where(IsWarmupParticipant))
            {
                player.ShowHint(BuildWarmupStatusHint(player, countdownLine), 1.25f);
            }
        }

        private string BuildWarmupStatusHint(Player player, string countdownLine)
        {
            string playerKey = GetPlayerKey(player);
            string selection = playerSelections.TryGetValue(playerKey, out RoleTypeId selectedRole)
                ? selectedRole.GetFullName()
                : "None";

            return $"{countdownLine}\nSelected SCP: {selection}\nInteract with a coin to change selection.";
        }

        private static string GetCurrentCountdownLine()
        {
            int playerCount = CurrentServerPlayerCount();
            int maxPlayers = Server.MaxPlayerCount > 0 ? Server.MaxPlayerCount : Math.Max(playerCount, 1);
            return BuildCountdownLine(GetNativeLobbyTimer(), playerCount, maxPlayers);
        }

        private static string BuildCountdownLine(short nativeTimer, int playerCount, int maxPlayers)
        {
            if (nativeTimer == -2)
            {
                return $"Countdown: Waiting for players\nPlayers: {playerCount}/{maxPlayers}";
            }

            string timerLine = nativeTimer <= 0
                ? "Countdown: Round starting"
                : $"Countdown: {nativeTimer}s";

            return $"{timerLine}\nPlayers: {playerCount}/{maxPlayers}";
        }

        private static bool IsWarmupParticipant(Player player)
        {
            return !player.IsHost;
        }

        private static void SetNativeLobbyTimer(short value)
        {
            if (RoundStart.singleton is not null && !RoundStart.RoundStarted)
            {
                RoundStart.singleton.NetworkTimer = value;
            }
        }

        private static short GetNativeLobbyTimer()
        {
            return RoundStart.singleton is null ? (short)-2 : RoundStart.singleton.NetworkTimer;
        }

        private static bool IsRoundStarted()
        {
            return Round.IsStarted || RoundStart.RoundStarted;
        }

        private static string GetPlayerKey(Player player)
        {
            return string.IsNullOrWhiteSpace(player.UserId) ? player.Id.ToString() : player.UserId;
        }
    }
}
