using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using UnityEngine;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace WarmupScpSelector.Models
{
    /// <summary>
    /// Spawns visible embedded MER primitives at an authored world transform and resolves named marker
    /// transforms without spawning marker geometry. One-level ProjectMER empty parents are retained so
    /// non-uniform parent scale plus child rotation produces the same shear as the authored asset.
    /// </summary>
    internal sealed class MerWorldSpawner
    {
        private readonly List<MerWorldInstance> _instances = new List<MerWorldInstance>();

        public bool TrySpawn(
            IReadOnlyList<MerPrimitive> primitives,
            IReadOnlyList<string> requiredMarkers,
            MerWorldTransform root,
            bool isStatic,
            out MerWorldInstance? instance,
            out string error)
        {
            instance = null;
            error = string.Empty;
            if (primitives == null || primitives.Count == 0)
            {
                error = "asset is empty";
                return false;
            }

            MerWorldInstance candidate = new MerWorldInstance(root, isStatic);
            try
            {
                candidate.Build(primitives, requiredMarkers ?? Array.Empty<string>());
                _instances.Add(candidate);
                instance = candidate;
                return true;
            }
            catch (Exception ex)
            {
                candidate.Destroy();
                error = ex.GetBaseException().Message;
                return false;
            }
        }

        public void Despawn(MerWorldInstance? instance)
        {
            if (instance == null)
            {
                return;
            }

            _instances.Remove(instance);
            instance.Destroy();
        }

        public void DespawnAll()
        {
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                _instances[i].Destroy();
            }

            _instances.Clear();
        }
    }

    /// <summary>One independently movable and independently destructible MER world instance.</summary>
    internal sealed class MerWorldInstance
    {
        private const byte RawMovementSmoothing = 60;

        private sealed class RootBinding
        {
            public RootBinding(PrimitiveObjectToy toy, MerPrimitive primitive)
            {
                Toy = toy;
                Primitive = primitive;
            }

            public PrimitiveObjectToy Toy { get; }
            public MerPrimitive Primitive { get; }
        }

        private sealed class ParentBinding
        {
            public ParentBinding(PrimitiveObjectToy toy, MerTransform transform)
            {
                Toy = toy;
                Transform = transform;
            }

            public PrimitiveObjectToy Toy { get; }
            public MerTransform Transform { get; }
        }

        private sealed class MarkerBinding
        {
            public MarkerBinding(MerPrimitive primitive)
            {
                Primitive = primitive;
            }

            public MerPrimitive Primitive { get; }
        }

        private readonly bool _isStatic;
        private readonly List<AdminToy> _toys = new List<AdminToy>();
        private readonly List<RootBinding> _rootBindings = new List<RootBinding>();
        private readonly Dictionary<int, ParentBinding> _parentBindings = new Dictionary<int, ParentBinding>();
        private readonly Dictionary<string, MarkerBinding> _markerBindings = new Dictionary<string, MarkerBinding>(StringComparer.Ordinal);
        private readonly Dictionary<string, MerWorldTransform> _markers = new Dictionary<string, MerWorldTransform>(StringComparer.Ordinal);
        private PrimitiveObjectToy? _dynamicRootToy;
        private MerWorldTransform _root;
        private bool _destroyed;

        internal MerWorldInstance(MerWorldTransform root, bool isStatic)
        {
            _root = root;
            _isStatic = isStatic;
        }

        public IReadOnlyDictionary<string, MerWorldTransform> Markers => _markers;
        public bool IsDestroyed => _destroyed;

        internal void Build(IReadOnlyList<MerPrimitive> primitives, IReadOnlyList<string> requiredMarkers)
        {
            HashSet<string> markerNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (MerPrimitive primitive in primitives)
            {
                if (!primitive.IsMarker)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(primitive.Name) || !markerNames.Add(primitive.Name))
                {
                    throw new InvalidOperationException($"duplicate or empty MER marker '{primitive.Name}'");
                }

                _markerBindings.Add(primitive.Name, new MarkerBinding(primitive));
            }

            string[] missing = requiredMarkers.Where(marker => !markerNames.Contains(marker)).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException($"missing MER markers: {string.Join(", ", missing)}");
            }

            // Flat moving props (the Aim carrier) use one replicated animated root, so the scheduler changes
            // one transform instead of every visible primitive. Assets with authored empty parents keep the
            // proven world-baked one-level hierarchy and update those parent/root bindings directly.
            if (!_isStatic && primitives.All(primitive => primitive.IsMarker || primitive.ParentTransform == null))
            {
                _dynamicRootToy = PrimitiveObjectToy.Create(_root.Position, _root.Rotation, _root.Scale, networkSpawn: false);
                _toys.Add(_dynamicRootToy);
                _dynamicRootToy.Type = PrimitiveType.Cube;
                _dynamicRootToy.Color = Color.clear;
                _dynamicRootToy.Flags = PrimitiveFlags.None;
                ConfigureMovement(_dynamicRootToy, isStatic: false);
                _dynamicRootToy.Spawn();
            }

            foreach (MerPrimitive primitive in primitives)
            {
                if (primitive.IsMarker)
                {
                    continue;
                }

                SpawnPrimitive(primitive);
            }

            ResolveMarkers();
        }

        public bool TrySetWorldTransform(MerWorldTransform root)
        {
            if (_destroyed)
            {
                return false;
            }

            _root = root;
            if (_dynamicRootToy != null)
            {
                Apply(_dynamicRootToy, _root);
            }

            foreach (RootBinding binding in _rootBindings)
            {
                MerWorldTransform world = Compose(_root, binding.Primitive);
                Apply(binding.Toy, world);
            }

            foreach (ParentBinding binding in _parentBindings.Values)
            {
                MerWorldTransform world = Compose(_root, binding.Transform);
                Apply(binding.Toy, world);
            }

            ResolveMarkers();
            return true;
        }

        public bool TryGetMarker(string name, out MerWorldTransform marker)
        {
            if (!_destroyed && !string.IsNullOrEmpty(name) && _markers.TryGetValue(name, out marker))
            {
                return true;
            }

            marker = default;
            return false;
        }

        public void Destroy()
        {
            if (_destroyed)
            {
                return;
            }

            _destroyed = true;
            for (int i = _toys.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (!_toys[i].IsDestroyed)
                    {
                        _toys[i].Destroy();
                    }
                }
                catch
                {
                    // Teardown is intentionally per-toy and idempotent; callers log at the subsystem boundary.
                }
            }

            _toys.Clear();
            _dynamicRootToy = null;
            _rootBindings.Clear();
            _parentBindings.Clear();
            _markerBindings.Clear();
            _markers.Clear();
        }

        private void SpawnPrimitive(MerPrimitive primitive)
        {
            if (_dynamicRootToy != null)
            {
                PrimitiveObjectToy child = PrimitiveObjectToy.Create(
                    primitive.Position,
                    MerWorldTransformComposer.QuaternionFromEuler(primitive.Rotation),
                    primitive.Scale,
                    _dynamicRootToy.Transform,
                    networkSpawn: false);
                TrackAndConfigure(child, primitive, isStatic: true);
                return;
            }

            if (primitive.ParentTransform is MerTransform parentTransform)
            {
                ParentBinding parent = GetOrSpawnParent(parentTransform);
                PrimitiveObjectToy child = PrimitiveObjectToy.Create(
                    primitive.Position,
                    MerWorldTransformComposer.QuaternionFromEuler(primitive.Rotation),
                    primitive.Scale,
                    parent.Toy.Transform,
                    networkSpawn: false);
                TrackAndConfigure(child, primitive, isStatic: true);
                return;
            }

            MerWorldTransform world = Compose(_root, primitive);
            PrimitiveObjectToy toy = PrimitiveObjectToy.Create(world.Position, world.Rotation, world.Scale, networkSpawn: false);
            TrackAndConfigure(toy, primitive, _isStatic);
            _rootBindings.Add(new RootBinding(toy, primitive));
        }

        private ParentBinding GetOrSpawnParent(MerTransform parentTransform)
        {
            if (_parentBindings.TryGetValue(parentTransform.ObjectId, out ParentBinding? existing))
            {
                return existing;
            }

            MerWorldTransform world = Compose(_root, parentTransform);
            PrimitiveObjectToy toy = PrimitiveObjectToy.Create(world.Position, world.Rotation, world.Scale, networkSpawn: false);
            _toys.Add(toy);
            toy.Type = PrimitiveType.Cube;
            toy.Color = Color.clear;
            toy.Flags = PrimitiveFlags.None;
            ConfigureMovement(toy, _isStatic);
            toy.Spawn();

            ParentBinding binding = new ParentBinding(toy, parentTransform);
            _parentBindings.Add(parentTransform.ObjectId, binding);
            return binding;
        }

        private void TrackAndConfigure(PrimitiveObjectToy toy, MerPrimitive primitive, bool isStatic)
        {
            _toys.Add(toy);
            toy.Type = primitive.Type;
            toy.Color = primitive.Color;
            // MER props are visual-only. Gameplay collision is authored explicitly by AimRangeWorld.
            toy.Flags = PrimitiveFlags.Visible;
            ConfigureMovement(toy, isStatic);
            toy.Spawn();
        }

        private static void ConfigureMovement(PrimitiveObjectToy toy, bool isStatic)
        {
            toy.IsStatic = isStatic;
            if (!isStatic)
            {
                toy.Base.NetworkMovementSmoothing = RawMovementSmoothing;
                toy.SyncInterval = 0f;
            }
        }

        private void ResolveMarkers()
        {
            _markers.Clear();
            foreach (KeyValuePair<string, MarkerBinding> pair in _markerBindings)
            {
                MerPrimitive marker = pair.Value.Primitive;
                MerWorldTransform world;
                if (marker.ParentTransform is MerTransform parent)
                {
                    world = MerWorldTransformComposer.Compose(Compose(_root, parent), marker.Position, marker.Rotation, marker.Scale);
                }
                else
                {
                    world = Compose(_root, marker);
                }

                _markers.Add(pair.Key, world);
            }
        }

        private static MerWorldTransform Compose(MerWorldTransform parent, MerTransform child)
        {
            return MerWorldTransformComposer.Compose(parent, child.Position, child.Rotation, child.Scale);
        }

        private static void Apply(PrimitiveObjectToy toy, MerWorldTransform transform)
        {
            toy.Position = transform.Position;
            toy.Rotation = transform.Rotation;
            toy.Scale = transform.Scale;
        }
    }
}
