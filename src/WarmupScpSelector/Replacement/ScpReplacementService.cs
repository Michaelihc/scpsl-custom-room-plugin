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
using WarmupScpSelector.Roles;
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
    private const float HumanSwapCutoffSeconds = 30f;

    private readonly WarmupScpSelectorPlugin _plugin;
    private readonly ScpReplacementState _state = new();
    private readonly Dictionary<RoleTypeId, CoroutineHandle> _lotteryHandles = new();
    private readonly Dictionary<RoleTypeId, SwapOffer> _offers = new();

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
            foreach (RoleTypeId offered in _offers.Where(pair => pair.Value.UserId == userId).Select(pair => pair.Key).ToList())
            {
                _offers.Remove(offered);
            }

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

        PruneOffers();
        if (_state.PendingCount == 0 && _offers.Count == 0)
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
            response = ScpReplacementText.Available(AvailableRoles(), Chinese);
            return true;
        }

        if (!ScpReplacementPolicy.CanVolunteer(player!.Role, player.IsAlive, Config.AllowAliveVolunteers))
        {
            response = player.IsSCP
                ? ScpReplacementText.ScpCannotVolunteer(Chinese)
                : ScpReplacementText.SpectatorOnly(Chinese);
            return false;
        }

        // A player whose role another plugin owns (SCP-999, Facility Manager, GOC spy, reinforcements, ...)
        // must not be pulled into an SCP slot.
        if (RoundRoles.IsClaimed(player))
        {
            response = ScpReplacementText.ClaimedRole(Chinese);
            return false;
        }

        SwapOffer? offer = _offers.Values.FirstOrDefault(candidate =>
            ScpReplacementPolicy.MatchesScpArgument(candidate.Role, argument!));
        if (offer != null)
        {
            return AcceptSwap(player, offer, out response);
        }

        if (!_state.TryFind(argument!, out PendingScpReplacement? entry) || entry == null)
        {
            response = ScpReplacementText.Invalid(Chinese) + " " + ScpReplacementText.Available(AvailableRoles(), Chinese);
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

        if (ElapsedSeconds >= HumanSwapCutoffSeconds)
        {
            response = ScpReplacementText.TooLate(Chinese);
            return false;
        }

        if (!ScpReplacementPolicy.CanOpenDeparture(
                player.Role,
                player.Health,
                player.MaxHealth,
                ElapsedSeconds,
                HumanSwapCutoffSeconds,
                Config.RequiredHealthPercentage,
                Config.IgnoredRoles))
        {
            response = ScpReplacementText.HumanLowHealth(Chinese);
            return false;
        }

        if (_state.IsCapacityReserved(Config.MaxReplacementsPerRound))
        {
            response = ScpReplacementText.CapacityReached(Chinese);
            return false;
        }

        if (FindOffer(player) != null)
        {
            response = ScpReplacementText.SwapAlreadyOffered(Chinese);
            return false;
        }

        RoleTypeId role = player.Role;
        _offers[role] = new SwapOffer(player, userId, role);
        foreach (Player other in Player.ReadyList.ToArray())
        {
            if (IsLiveHuman(other) && !other.IsSCP)
            {
                SendBroadcast(other, ScpReplacementText.SwapOffered(role, Chinese), Config.AnnounceBroadcastSeconds);
                if (Config.AnnounceInConsole)
                {
                    SendConsoleMessage(other, StripHeader(ScpReplacementText.SwapOffered(role, Chinese)));
                }
            }
        }

        Logger.Info($"{LogPrefix} {player.Nickname} ({userId}) offered to swap SCP-{ScpReplacementPolicy.ScpNumber(role)}.");
        response = ScpReplacementText.SwapOfferOpened(role, Chinese);
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
                                 ScpReplacementPolicy.CanVolunteer(player.Role, player.IsAlive, Config.AllowAliveVolunteers) &&
                                 !RoundRoles.IsClaimed(player))
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

                RoundRoles.TryClaim(candidate, RoundRoles.ScpClaim);
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

    private SwapOffer? FindOffer(Player player) =>
        _offers.Values.FirstOrDefault(offer => offer.UserId == StableUserId(player));

    /// <summary>Drops expired offers and offers whose SCP died, left, or changed role.</summary>
    private void PruneOffers()
    {
        if (ElapsedSeconds >= HumanSwapCutoffSeconds)
        {
            _offers.Clear();
            return;
        }

        foreach (RoleTypeId role in _offers.Where(pair => !pair.Value.IsStillValid()).Select(pair => pair.Key).ToList())
        {
            _offers.Remove(role);
        }
    }

    private IReadOnlyList<RoleTypeId> AvailableRoles() =>
        _state.PendingRoles.Concat(_offers.Keys).Distinct().OrderBy(role => (int)role).ToArray();

    /// <summary>
    /// Trades roles between an SCP that used <c>.human</c> and the first living, unclaimed human to accept.
    /// The human becomes the SCP where it stands, with its health, Hume Shield and ScpTiers progression; the
    /// SCP takes the human's role, position, health, items and ammo. The SCP claim moves with the role.
    /// </summary>
    private bool AcceptSwap(Player human, SwapOffer offer, out string response)
    {
        if (ElapsedSeconds >= HumanSwapCutoffSeconds)
        {
            _offers.Clear();
            response = ScpReplacementText.TooLate(Chinese);
            return false;
        }

        if (!offer.IsStillValid())
        {
            _offers.Remove(offer.Role);
            response = ScpReplacementText.SwapUnavailable(Chinese);
            return false;
        }

        if (!human.IsAlive || !human.IsHuman || human.IsSCP || human == offer.Scp)
        {
            response = ScpReplacementText.SwapNeedsHuman(Chinese);
            return false;
        }

        Player scp = offer.Scp;
        RoleTypeId scpRole = offer.Role;
        RoleTypeId humanRole = human.Role;
        UnityEngine.Vector3 scpPosition = scp.Position;
        UnityEngine.Vector3 humanPosition = human.Position;
        float scpHealth = scp.Health;
        float scpHume = scp.HumeShield;
        float humanHealth = human.Health;
        object? tiers = ScpTiersBridge.Capture(scp);
        List<ItemType> items = human.Items.Select(item => item.Type).ToList();
        Dictionary<ItemType, ushort> ammo = new(human.Ammo);

        _offers.Remove(scpRole);
        bool transferred = RoundRoles.TryTransfer(scp, human, RoundRoles.ScpClaim) ||
                           RoundRoles.TryClaim(human, RoundRoles.ScpClaim);
        try
        {
            human.DisableAllEffects();
            human.ClearInventory();
            human.SetRole(scpRole, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);
            if (human.Role != scpRole)
            {
                throw new InvalidOperationException("the SCP role change was blocked");
            }

            if (scpRole != RoleTypeId.Scp079)
            {
                human.Position = scpPosition;
                human.Health = Mathf.Min(scpHealth, human.MaxHealth);
                human.HumeShield = Mathf.Min(scpHume, human.MaxHumeShield);
            }

            if (tiers != null && !ScpTiersBridge.Transfer(human, tiers))
            {
                Logger.Warn($"{LogPrefix} ScpTiers progression could not be restored on {human.Nickname}.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"{LogPrefix} Swap into SCP-{ScpReplacementPolicy.ScpNumber(scpRole)} failed: {ex.Message}");
            if (transferred)
            {
                RoundRoles.Release(human, RoundRoles.ScpClaim);
                RoundRoles.TryClaim(scp, RoundRoles.ScpClaim);
            }

            response = ScpReplacementText.SwapFailed(Chinese);
            return false;
        }

        try
        {
            scp.DisableAllEffects();
            scp.SetRole(humanRole, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);
            scp.Position = humanPosition;
            scp.ClearInventory();
            foreach (ItemType item in items)
            {
                scp.AddItem(item, ItemAddReason.AdminCommand);
            }

            foreach (KeyValuePair<ItemType, ushort> pair in ammo)
            {
                scp.SetAmmo(pair.Key, pair.Value);
            }

            scp.Health = Mathf.Min(humanHealth, scp.MaxHealth);
        }
        catch (Exception ex)
        {
            // The human already holds the SCP; the former SCP keeps whatever state the failure left.
            Logger.Error($"{LogPrefix} Former SCP {scp.Nickname} could not fully take {humanRole}: {ex.Message}");
        }

        _state.MarkReplacementSucceeded();
        SendBroadcast(human, ScpReplacementText.SwapDone(scpRole, isNewScp: true, Chinese), Config.ResultBroadcastSeconds);
        SendBroadcast(scp, ScpReplacementText.SwapDone(scpRole, isNewScp: false, Chinese), Config.ResultBroadcastSeconds);
        foreach (Player other in Player.ReadyList.ToArray())
        {
            if (IsLiveHuman(other) && other != human && other != scp && !other.IsSCP)
            {
                SendBroadcast(other, ScpReplacementText.SwapTaken(scpRole, Chinese), Config.ResultBroadcastSeconds);
            }
        }

        Logger.Info($"{LogPrefix} {human.Nickname} swapped with {scp.Nickname}: SCP-{ScpReplacementPolicy.ScpNumber(scpRole)} <-> {humanRole}.");
        response = ScpReplacementText.SwapDone(scpRole, isNewScp: true, Chinese);
        return true;
    }

    private sealed class SwapOffer
    {
        public SwapOffer(Player scp, string userId, RoleTypeId role)
        {
            Scp = scp;
            UserId = userId;
            Role = role;
            LifeId = scp.LifeId;
        }

        public Player Scp { get; }

        public string UserId { get; }

        public RoleTypeId Role { get; }

        public int LifeId { get; }

        public bool IsStillValid() =>
            Scp is { IsDestroyed: false, IsAlive: true } && Scp.Role == Role && Scp.LifeId == LifeId;
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
        _offers.Clear();
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
