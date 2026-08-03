using System;
using System.Collections.Generic;

namespace WarmupScpSelector.Activities.AimRange
{
    public readonly struct RangeBotIdentity
    {
        public RangeBotIdentity(int slotId, int spawnGeneration, int hubInstanceId, uint networkId, int playerId)
        {
            SlotId = slotId;
            SpawnGeneration = spawnGeneration;
            HubInstanceId = hubInstanceId;
            NetworkId = networkId;
            PlayerId = playerId;
        }

        public int SlotId { get; }
        public int SpawnGeneration { get; }
        public int HubInstanceId { get; }
        public uint NetworkId { get; }
        public int PlayerId { get; }
    }

    /// <summary>Pure ownership registry. It deliberately never indexes bots by their shared UserId.</summary>
    public sealed class RangeBotRegistry
    {
        private readonly Dictionary<int, RangeBotIdentity> _bySlot = new Dictionary<int, RangeBotIdentity>();
        private readonly Dictionary<int, int> _byHub = new Dictionary<int, int>();
        private readonly Dictionary<uint, int> _byNetwork = new Dictionary<uint, int>();
        private readonly Dictionary<int, int> _byPlayer = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _generationBySlot = new Dictionary<int, int>();

        public int Count => _bySlot.Count;

        public int BeginSpawn(int slotId)
        {
            RemoveSlot(slotId);
            _generationBySlot.TryGetValue(slotId, out int current);
            current = current == int.MaxValue ? 1 : current + 1;
            _generationBySlot[slotId] = current;
            return current;
        }

        public bool Register(RangeBotIdentity identity)
        {
            if (!_generationBySlot.TryGetValue(identity.SlotId, out int generation) || generation != identity.SpawnGeneration)
            {
                return false;
            }

            RemoveSlot(identity.SlotId);
            _bySlot[identity.SlotId] = identity;
            if (identity.HubInstanceId != 0) _byHub[identity.HubInstanceId] = identity.SlotId;
            if (identity.NetworkId != 0) _byNetwork[identity.NetworkId] = identity.SlotId;
            if (identity.PlayerId > 0) _byPlayer[identity.PlayerId] = identity.SlotId;
            return true;
        }

        public bool IsCurrent(int slotId, int spawnGeneration) =>
            _generationBySlot.TryGetValue(slotId, out int current) && current == spawnGeneration &&
            _bySlot.TryGetValue(slotId, out RangeBotIdentity identity) && identity.SpawnGeneration == spawnGeneration;

        public bool TryByHub(int hubInstanceId, out RangeBotIdentity identity) => TryResolve(_byHub, hubInstanceId, out identity);
        public bool TryByNetwork(uint networkId, out RangeBotIdentity identity) => TryResolve(_byNetwork, networkId, out identity);
        public bool TryByPlayer(int playerId, out RangeBotIdentity identity) => TryResolve(_byPlayer, playerId, out identity);

        public RangeBotIdentity? InvalidateSlot(int slotId)
        {
            RangeBotIdentity? removed = _bySlot.TryGetValue(slotId, out RangeBotIdentity identity) ? identity : (RangeBotIdentity?)null;
            RemoveSlot(slotId);
            _generationBySlot.TryGetValue(slotId, out int current);
            _generationBySlot[slotId] = current == int.MaxValue ? 1 : current + 1;
            return removed;
        }

        public List<RangeBotIdentity> InvalidateAll()
        {
            List<RangeBotIdentity> all = new List<RangeBotIdentity>(_bySlot.Values);
            foreach (RangeBotIdentity identity in all)
            {
                _generationBySlot.TryGetValue(identity.SlotId, out int current);
                _generationBySlot[identity.SlotId] = current == int.MaxValue ? 1 : current + 1;
            }

            _bySlot.Clear();
            _byHub.Clear();
            _byNetwork.Clear();
            _byPlayer.Clear();
            return all;
        }

        private bool TryResolve<TKey>(Dictionary<TKey, int> index, TKey key, out RangeBotIdentity identity)
        {
            if (index.TryGetValue(key, out int slotId) && _bySlot.TryGetValue(slotId, out identity))
            {
                return true;
            }

            identity = default;
            return false;
        }

        private void RemoveSlot(int slotId)
        {
            if (!_bySlot.TryGetValue(slotId, out RangeBotIdentity prior))
            {
                return;
            }

            _bySlot.Remove(slotId);
            if (prior.HubInstanceId != 0) _byHub.Remove(prior.HubInstanceId);
            if (prior.NetworkId != 0) _byNetwork.Remove(prior.NetworkId);
            if (prior.PlayerId > 0) _byPlayer.Remove(prior.PlayerId);
        }
    }
}
