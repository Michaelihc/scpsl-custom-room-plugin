using System;
using System.Collections.Generic;
using System.Linq;
using InventorySystem.Items;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Features.Wrappers;
using MEC;
using PlayerRoles;
using UnityEngine;
using WarmupScpSelector.Text;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Replacement;

/// <summary>
/// LabAPI runtime adapter for the Augaton/Jon M early-round SCP replacement flow. Candidate ownership is
/// always by authenticated UserId; round and lottery generations make delayed callbacks harmless after reset.
/// </summary>
internal sealed class ScpReplacementService
{
    private const string LogPrefix = "[WarmupScpSelector:Replacement]";

    private readonly WarmupScpSelectorPlugin _plugin;
    private readonly ScpReplacementState _state = new();
    private readonly Dictionary<RoleTypeId, CoroutineHandle> _lotteryHandles = new();

    public ScpReplacementService(WarmupScpSelectorPlugin plugin)
    {
        _plugin = plugin;
        ValidateConfig();

        if (Round.IsRoundStarted)
        {
            _state.BeginRound();
        }
        else
        {
            _state.EndRound();
        }
    }

    private ScpReplacementConfig Config => _plugin.Config.ScpReplacement;

    private bool Chinese => string.Equals(_plugin.Config.Language, "cn", StringComparison.OrdinalIgnoreCase);

    public void OnRoundStarted()
    {
        CancelLotteries();
        if (!Config.IsEnabled)
        {
            _state.EndRound();
            return;
        }

        _state.BeginRound();
        if (!Config.AllowHumanCommand || Config.HumanHintSeconds == 0)
        {
            return;
        }

        foreach (Player player in Player.ReadyList.ToArray())
        {
            if (IsLiveHuman(player) && player.IsSCP && ScpReplacementPolicy.IsMainScp(player.Role) &&
                !Config.IgnoredRoles.Contains(player.Role))
            {
                SendBroadcast(player, ScpReplacementText.HumanHint(Chinese), Config.HumanHintSeconds);
            }
        }
    }

    public void OnRoundEnded(RoundEndedEventArgs _)
    {
        CleanupRound();
    }

    public void OnRoundRestarted()
    {
        CleanupRound();
    }

    /// <summary>
    /// Removes the departing UserId from every lottery first, then snapshots a qualifying SCP departure while
    /// LabAPI still exposes its role and health on the Left event wrapper.
    /// </summary>
    public void OnPlayerLeft(PlayerLeftEventArgs ev)
    {
        try
        {
            Player? player = ev?.Player;
            if (player == null)
            {
                return;
            }

            string userId = StableUserId(player);
            _state.RemoveVolunteer(userId);

            if (!Config.IsEnabled || !_state.RoundActive || !Round.IsRoundStarted || !IsDepartingHuman(player))
            {
                return;
            }

            TryOpenDeparture(player, announce: true, out _, out _);
        }
        catch (Exception ex)
        {
            Logger.Error($"{LogPrefix} Player-left processing failed: {ex}");
        }
    }

    public bool ExecuteVolunteer(Player? player, string? argument, out string response)
    {
        if (!Config.IsEnabled || !_state.RoundActive || !Round.IsRoundStarted)
        {
            response = ScpReplacementText.Disabled(Chinese);
            return false;
        }

        if (!IsLiveHuman(player))
        {
            response = ScpReplacementText.PlayerOnly(Chinese);
            return false;
        }

        string userId = StableUserId(player!);
        if (!TryConsumeCooldown(userId, out response))
        {
            return false;
        }

        if (_state.PendingCount == 0)
        {
            response = ScpReplacementText.NoPending(Chinese);
            return false;
        }

        if (ElapsedSeconds > Config.VolunteerCutoffSeconds)
        {
            response = ScpReplacementText.TooLate(Chinese);
            return false;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            response = ScpReplacementText.Available(_state.PendingRoles, Chinese);
            return true;
        }

        if (!ScpReplacementPolicy.CanVolunteer(player!.Role, player.IsAlive, Config.AllowAliveVolunteers))
        {
            response = player.IsSCP
                ? ScpReplacementText.ScpCannotVolunteer(Chinese)
                : ScpReplacementText.SpectatorOnly(Chinese);
            return false;
        }

        if (!_state.TryFind(argument!, out PendingScpReplacement? entry) || entry == null)
        {
            response = ScpReplacementText.Invalid(Chinese) + " " + ScpReplacementText.Available(_state.PendingRoles, Chinese);
            return false;
        }

        if (!_state.TryVolunteer(entry.Role, userId, out _, out bool startLottery, out int lotteryToken))
        {
            response = ScpReplacementText.AlreadyEntered(Chinese);
            return false;
        }

        if (startLottery)
        {
            int roundGeneration = entry.RoundGeneration;
            float delay = Math.Max(0.05f, Config.LotterySeconds);
            try
            {
                _lotteryHandles[entry.Role] = Timing.CallDelayed(
                    delay,
                    () => ResolveLottery(entry.Role, roundGeneration, lotteryToken));
            }
            catch (Exception ex)
            {
                _state.RemovePending(entry.Role, entry);
                Logger.Error($"{LogPrefix} Could not schedule SCP-{ScpReplacementPolicy.ScpNumber(entry.Role)} lottery: {ex}");
                response = ScpReplacementText.RoleChangeFailed(Chinese);
                return false;
            }
        }

        SendBroadcast(player, ScpReplacementText.EnteredBroadcast(entry.Role, Chinese), Config.ResultBroadcastSeconds);
        response = ScpReplacementText.Entered(entry.Role, Chinese);
        return true;
    }

    public bool ExecuteHuman(Player? player, out string response)
    {
        if (!Config.IsEnabled || !_state.RoundActive || !Round.IsRoundStarted)
        {
            response = ScpReplacementText.Disabled(Chinese);
            return false;
        }

        if (!Config.AllowHumanCommand)
        {
            response = ScpReplacementText.HumanDisabled(Chinese);
            return false;
        }

        if (!IsLiveHuman(player))
        {
            response = ScpReplacementText.PlayerOnly(Chinese);
            return false;
        }

        string userId = StableUserId(player!);
        if (!TryConsumeCooldown(userId, out response))
        {
            return false;
        }

        if (!player!.IsSCP || !ScpReplacementPolicy.IsMainScp(player.Role))
        {
            response = ScpReplacementText.HumanNotScp(Chinese);
            return false;
        }

        if (Config.IgnoredRoles.Contains(player.Role))
        {
            response = ScpReplacementText.HumanIgnored(Chinese);
            return false;
        }

        if (ElapsedSeconds > Config.DepartureCutoffSeconds)
        {
            response = ScpReplacementText.TooLate(Chinese);
            return false;
        }

        if (!ScpReplacementPolicy.CanOpenDeparture(
                player.Role,
                player.Health,
                player.MaxHealth,
                ElapsedSeconds,
                Config.DepartureCutoffSeconds,
                Config.RequiredHealthPercentage,
                Config.IgnoredRoles))
        {
            response = ScpReplacementText.HumanLowHealth(Chinese);
            return false;
        }

        if (!TryOpenDeparture(player, announce: false, out PendingScpReplacement? opened, out OpenFailure failure) || opened == null)
        {
            response = failure == OpenFailure.Capacity
                ? ScpReplacementText.CapacityReached(Chinese)
                : ScpReplacementText.SlotAlreadyOpen(Chinese);
            return false;
        }

        RoleTypeId oldRole = player.Role;
        RoleTypeId humanRole = ScpReplacementPolicy.PickWeightedHumanRole(
            Config.HumanCommandRoles,
            UnityEngine.Random.Range(0, int.MaxValue));

        try
        {
            player.DisableAllEffects();
            player.SetRole(humanRole, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.All);
        }
        catch (Exception ex)
        {
            _state.RemovePending(oldRole, opened);
            Logger.Error($"{LogPrefix} .human role change failed for {userId}: {ex}");
            response = ScpReplacementText.RoleChangeFailed(Chinese);
            return false;
        }

        if (player.Role != humanRole)
        {
            _state.RemovePending(oldRole, opened);
            response = ScpReplacementText.RoleChangeFailed(Chinese);
            return false;
        }

        if (humanRole == RoleTypeId.ClassD)
        {
            foreach (ItemType item in Config.ClassDBonusItems ?? Enumerable.Empty<ItemType>())
            {
                if (item != ItemType.None)
                {
                    try
                    {
                        player.AddItem(item, ItemAddReason.AdminCommand);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"{LogPrefix} Could not grant .human bonus item {item} to {userId}: {ex.Message}");
                    }
                }
            }
        }

        AnnounceDeparture(oldRole);
        string success = ScpReplacementText.HumanSuccess(humanRole, Chinese);
        SendBroadcast(player, "<color=#d6b35a>[SCP REPLACEMENT]</color>\n" + success, Config.ResultBroadcastSeconds);
        Logger.Info($"{LogPrefix} {player.Nickname} ({userId}) gave up {oldRole} and became {humanRole}.");
        response = success;
        return true;
    }

    public void Cleanup()
    {
        CleanupRound();
    }

    private bool TryOpenDeparture(
        Player player,
        bool announce,
        out PendingScpReplacement? opened,
        out OpenFailure failure)
    {
        opened = null;
        failure = OpenFailure.Ineligible;
        RoleTypeId role = player.Role;

        if (!ScpReplacementPolicy.CanOpenDeparture(
                role,
                player.Health,
                player.MaxHealth,
                ElapsedSeconds,
                Config.DepartureCutoffSeconds,
                Config.RequiredHealthPercentage,
                Config.IgnoredRoles))
        {
            return false;
        }

        if (_state.IsCapacityReserved(Config.MaxReplacementsPerRound))
        {
            failure = OpenFailure.Capacity;
            return false;
        }

        if (_state.TryFind(role, out _))
        {
            failure = OpenFailure.AlreadyPending;
            return false;
        }

        if (!_state.TryOpen(role, Config.MaxReplacementsPerRound, out opened) || opened == null)
        {
            failure = OpenFailure.Ineligible;
            return false;
        }

        failure = OpenFailure.None;
        Logger.Info($"{LogPrefix} SCP-{ScpReplacementPolicy.ScpNumber(role)} opened after {ElapsedSeconds:0.0}s.");
        if (announce)
        {
            AnnounceDeparture(role);
        }

        return true;
    }

    private void ResolveLottery(RoleTypeId role, int roundGeneration, int lotteryToken)
    {
        _lotteryHandles.Remove(role);
        try
        {
            if (!_state.TryTakeLottery(role, roundGeneration, lotteryToken, out PendingScpReplacement? entry) || entry == null)
            {
                return;
            }

            if (!Config.IsEnabled || !Round.IsRoundStarted || HasLiveRoleHolder(role))
            {
                AnnounceNoWinner(role, roleAlreadyFilled: HasLiveRoleHolder(role));
                return;
            }

            List<Player> candidates = Player.ReadyList
                .Where(player => IsLiveHuman(player) && entry.Volunteers.Contains(StableUserId(player)) &&
                                 ScpReplacementPolicy.CanVolunteer(player.Role, player.IsAlive, Config.AllowAliveVolunteers))
                .ToList();

            Shuffle(candidates);
            foreach (Player candidate in candidates)
            {
                if (HasLiveRoleHolder(role))
                {
                    AnnounceNoWinner(role, roleAlreadyFilled: true);
                    return;
                }

                try
                {
                    candidate.DisableAllEffects();
                    candidate.SetRole(role, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.All);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"{LogPrefix} Candidate {StableUserId(candidate)} could not receive {role}: {ex.Message}");
                    continue;
                }

                if (candidate.Role != role)
                {
                    continue;
                }

                _state.RemoveVolunteer(StableUserId(candidate));
                _state.MarkReplacementSucceeded();
                AnnounceWinner(candidate, role);
                Logger.Info($"{LogPrefix} {candidate.Nickname} ({StableUserId(candidate)}) replaced SCP-{ScpReplacementPolicy.ScpNumber(role)}.");
                return;
            }

            AnnounceNoWinner(role, roleAlreadyFilled: false);
        }
        catch (Exception ex)
        {
            Logger.Error($"{LogPrefix} SCP-{ScpReplacementPolicy.ScpNumber(role)} lottery failed: {ex}");
        }
    }

    private void AnnounceDeparture(RoleTypeId role)
    {
        string message = ScpReplacementText.Departure(role, Chinese);
        foreach (Player player in Player.ReadyList.ToArray())
        {
            if (!IsLiveHuman(player) || player.IsSCP)
            {
                continue;
            }

            SendBroadcast(player, message, Config.AnnounceBroadcastSeconds);
            if (Config.AnnounceInConsole)
            {
                SendConsoleMessage(player, StripHeader(message));
            }
        }
    }

    private void AnnounceWinner(Player winner, RoleTypeId role)
    {
        foreach (Player player in Player.ReadyList.ToArray())
        {
            if (IsLiveHuman(player))
            {
                SendBroadcast(player, ScpReplacementText.Winner(role, player == winner, Chinese), Config.ResultBroadcastSeconds);
            }
        }
    }

    private void AnnounceNoWinner(RoleTypeId role, bool roleAlreadyFilled)
    {
        string message = ScpReplacementText.NoWinner(role, roleAlreadyFilled, Chinese);
        foreach (Player player in Player.ReadyList.ToArray())
        {
            if (IsLiveHuman(player) && !player.IsSCP)
            {
                SendBroadcast(player, message, Config.ResultBroadcastSeconds);
            }
        }
    }

    private bool TryConsumeCooldown(string userId, out string response)
    {
        if (_state.TryConsumeCooldown(userId, Time.realtimeSinceStartup, Config.CommandCooldownSeconds, out double remaining))
        {
            response = string.Empty;
            return true;
        }

        response = ScpReplacementText.Cooldown(remaining, Chinese);
        return false;
    }

    private bool HasLiveRoleHolder(RoleTypeId role) =>
        Player.ReadyList.Any(player => IsLiveHuman(player) && player.Role == role);

    private void CleanupRound()
    {
        CancelLotteries();
        _state.EndRound();
    }

    private void CancelLotteries()
    {
        foreach (CoroutineHandle handle in _lotteryHandles.Values)
        {
            Timing.KillCoroutines(handle);
        }

        _lotteryHandles.Clear();
    }

    private void ValidateConfig()
    {
        ScpReplacementConfig config = _plugin.Config.ScpReplacement ?? new ScpReplacementConfig();
        _plugin.Config.ScpReplacement = config;

        if (!IsFinite(config.DepartureCutoffSeconds) || config.DepartureCutoffSeconds < 0f)
        {
            Logger.Warn($"{LogPrefix} Invalid DepartureCutoffSeconds; using 60.");
            config.DepartureCutoffSeconds = 60f;
        }

        if (!IsFinite(config.VolunteerCutoffSeconds) || config.VolunteerCutoffSeconds < config.DepartureCutoffSeconds)
        {
            Logger.Warn($"{LogPrefix} VolunteerCutoffSeconds must be at least DepartureCutoffSeconds; aligning it.");
            config.VolunteerCutoffSeconds = config.DepartureCutoffSeconds;
        }

        if (!IsFinite(config.RequiredHealthPercentage) || config.RequiredHealthPercentage < 0f || config.RequiredHealthPercentage > 100f)
        {
            Logger.Warn($"{LogPrefix} Invalid RequiredHealthPercentage; using 95.");
            config.RequiredHealthPercentage = 95f;
        }

        if (!IsFinite(config.LotterySeconds) || config.LotterySeconds <= 0f)
        {
            Logger.Warn($"{LogPrefix} Invalid LotterySeconds; using 10.");
            config.LotterySeconds = 10f;
        }

        if (!IsFinite(config.CommandCooldownSeconds) || config.CommandCooldownSeconds < 0f)
        {
            Logger.Warn($"{LogPrefix} Invalid CommandCooldownSeconds; using 0.");
            config.CommandCooldownSeconds = 0f;
        }

        config.MaxReplacementsPerRound = Math.Max(0, config.MaxReplacementsPerRound);
        config.IgnoredRoles ??= new List<RoleTypeId> { RoleTypeId.Scp0492 };
        config.ClassDBonusItems ??= new List<ItemType>();

        bool hasValidHumanRole = config.HumanCommandRoles?.Any(pair =>
            pair.Value > 0 && ScpReplacementPolicy.IsHumanCommandRole(pair.Key)) ?? false;
        if (!hasValidHumanRole)
        {
            Logger.Warn($"{LogPrefix} HumanCommandRoles has no valid positive human role; using ClassD.");
            config.HumanCommandRoles = new Dictionary<RoleTypeId, int> { [RoleTypeId.ClassD] = 1 };
        }
    }

    private static void SendBroadcast(Player player, string message, ushort duration)
    {
        try
        {
            player.SendBroadcast(message, duration, Broadcast.BroadcastFlags.Normal, shouldClearPrevious: false);
        }
        catch (Exception ex)
        {
            Logger.Warn($"{LogPrefix} Broadcast failed for {StableUserId(player)}: {ex.Message}");
        }
    }

    private static void SendConsoleMessage(Player player, string message)
    {
        try
        {
            player.SendConsoleMessage(message, "yellow");
        }
        catch (Exception ex)
        {
            Logger.Warn($"{LogPrefix} Console announcement failed for {StableUserId(player)}: {ex.Message}");
        }
    }

    private static string StripHeader(string message)
    {
        int newline = message.IndexOf('\n');
        return newline >= 0 ? message.Substring(newline + 1) : message;
    }

    private static void Shuffle<T>(IList<T> values)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }

    private static bool IsLiveHuman(Player? player) =>
        player != null && player.ReferenceHub != null && !player.IsDestroyed && !player.IsHost &&
        !player.IsDummy && !player.IsNpc && player.IsPlayer && player.IsReady &&
        !string.IsNullOrWhiteSpace(player.UserId);

    private static bool IsDepartingHuman(Player player) =>
        player.ReferenceHub != null && !player.IsHost && !player.IsDummy && !player.IsNpc &&
        player.IsPlayer && !string.IsNullOrWhiteSpace(player.UserId);

    private static string StableUserId(Player player) => player.UserId?.Trim() ?? string.Empty;

    private double ElapsedSeconds => Math.Max(0d, Round.Duration.TotalSeconds);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private enum OpenFailure
    {
        None,
        Ineligible,
        Capacity,
        AlreadyPending,
    }
}
