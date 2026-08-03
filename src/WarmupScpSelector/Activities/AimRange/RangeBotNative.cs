using System;
using System.Linq;
using InventorySystem.Items;
using LabApi.Features.Wrappers;
using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using RelativePositioning;
using UnityEngine;

namespace WarmupScpSelector.Activities.AimRange
{
    /// <summary>Current-build native dummy, firearm, aim/LOS, and owned-entity operations.</summary>
    internal static class RangeBotNative
    {
        public static bool HasDummyAction(ReferenceHub hub, string actionName)
        {
            if (hub == null || hub.gameObject == null || !hub.IsDummy || string.IsNullOrWhiteSpace(actionName))
            {
                return false;
            }

            return DummyActionCollector.ServerGetActions(hub).Any(action =>
                action.Action != null && string.Equals(action.Name, actionName, StringComparison.OrdinalIgnoreCase));
        }

        public static bool TryInvokeDummyAction(ReferenceHub hub, string actionName)
        {
            if (hub == null || hub.gameObject == null || !hub.IsDummy || string.IsNullOrWhiteSpace(actionName))
            {
                return false;
            }

            foreach (DummyAction action in DummyActionCollector.ServerGetActions(hub))
            {
                if (action.Action != null && string.Equals(action.Name, actionName, StringComparison.OrdinalIgnoreCase))
                {
                    action.Action.Invoke();
                    return true;
                }
            }

            return false;
        }

        public static int FirearmAmmoUnits(FirearmItem firearm) =>
            firearm == null ? -1 : firearm.StoredAmmo + firearm.ChamberedAmmo;

        public static Vector3 AimPoint(ReferenceHub hub)
        {
            HitboxIdentity? best = null;
            int bestRank = int.MaxValue;
            foreach (HitboxIdentity hitbox in HitboxIdentity.Instances)
            {
                if (hitbox == null || hitbox.TargetHub != hub)
                {
                    continue;
                }

                int rank = hitbox.HitboxType == HitboxType.Headshot ? 0 : hitbox.HitboxType == HitboxType.Body ? 1 : 2;
                if (rank < bestRank)
                {
                    best = hitbox;
                    bestRank = rank;
                }
            }

            if (best != null)
            {
                return best.CenterOfMass;
            }

            Vector3 body = hub.transform.position;
            return hub.PlayerCameraReference != null ? hub.PlayerCameraReference.position : body + Vector3.up * 1.65f;
        }

        public static bool HasLineOfSight(ReferenceHub shooter, ReferenceHub victim, Vector3 aimPoint)
        {
            Vector3 origin = shooter.PlayerCameraReference.position;
            Vector3 delta = aimPoint - origin;
            float distance = delta.magnitude;
            if (distance < 0.05f)
            {
                return true;
            }

            foreach (RaycastHit hit in Physics.RaycastAll(
                         origin,
                         delta / distance,
                         distance,
                         FpcStateProcessor.Mask,
                         QueryTriggerInteraction.Ignore).OrderBy(hit => hit.distance))
            {
                if (hit.collider == null)
                {
                    continue;
                }

                ReferenceHub? hitHub = hit.collider.GetComponentInParent<ReferenceHub>();
                if (hitHub == null)
                {
                    HitboxIdentity? hitbox = hit.collider.GetComponentInParent<HitboxIdentity>();
                    hitHub = hitbox?.TargetHub;
                }

                if (hitHub == shooter)
                {
                    continue;
                }

                return hitHub == victim;
            }

            return true;
        }

        public static void ApplyLook(ReferenceHub hub, Vector3 worldPoint)
        {
            if (hub.roleManager.CurrentRole is not IFpcRole fpc)
            {
                return;
            }

            FpcMouseLook mouseLook = fpc.FpcModule.MouseLook;
            Vector3 delta = worldPoint - hub.PlayerCameraReference.position;
            Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
            float yaw = horizontal.sqrMagnitude < 0.000001f
                ? mouseLook.CurrentHorizontal
                : Quaternion.LookRotation(horizontal, Vector3.up).eulerAngles.y;
            float pitch = Mathf.Atan2(delta.y, horizontal.magnitude) * Mathf.Rad2Deg;
            mouseLook.CurrentHorizontal = yaw;
            mouseLook.CurrentVertical = pitch;
        }

        /// <summary>
        /// Feeds one nearby world-space target into the native FPC motor. A distant ReceivedPosition is
        /// rejected as desync and freezes a dummy in place, so callers must stream small steps.
        /// </summary>
        public static bool WalkTowards(ReferenceHub hub, Vector3 worldTarget, float stepMeters = 0.45f)
        {
            if (hub?.roleManager.CurrentRole is not IFpcRole fpc || stepMeters <= 0f)
            {
                return false;
            }

            Vector3 current = hub.transform.position;
            Vector3 delta = worldTarget - current;
            delta.y = 0f;
            float distance = delta.magnitude;
            Vector3 received = distance <= stepMeters || distance <= 0.0001f
                ? worldTarget
                : current + delta / distance * stepMeters;
            received.y = current.y;
            fpc.FpcModule.Motor.ReceivedPosition = new RelativePosition(received);
            return true;
        }

        public static float FlatDistance(ReferenceHub hub, Vector3 worldPoint)
        {
            if (hub == null)
            {
                return float.PositiveInfinity;
            }

            Vector3 delta = worldPoint - hub.transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        public static void ApplyYaw(ReferenceHub hub, Vector3 worldDirection)
        {
            if (hub?.roleManager.CurrentRole is not IFpcRole fpc)
            {
                return;
            }

            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude > 0.0001f)
            {
                fpc.FpcModule.MouseLook.CurrentHorizontal = Quaternion.LookRotation(worldDirection, Vector3.up).eulerAngles.y;
            }
        }

        public static float AimErrorDegrees(ReferenceHub hub, Vector3 worldPoint)
        {
            Vector3 toTarget = (worldPoint - hub.PlayerCameraReference.position).normalized;
            return Vector3.Angle(hub.PlayerCameraReference.forward, toTarget);
        }

        public static void DestroySerial(ushort serial)
        {
            if (serial == 0)
            {
                return;
            }

            try
            {
                if (Item.TryGet(serial, out Item? item) && item?.CurrentOwner is Player owner)
                {
                    owner.RemoveItem(item);
                }
            }
            catch { }

            try
            {
                if (Pickup.TryGet(serial, out Pickup? pickup) && pickup != null && !pickup.IsDestroyed)
                {
                    pickup.Destroy();
                }
            }
            catch { }
        }

        public static void DestroyCarrier(WaypointToy? carrier)
        {
            try
            {
                if (carrier != null && !carrier.IsDestroyed)
                {
                    carrier.Destroy();
                }
            }
            catch { }
        }

        public static void ClearReserveAmmo(ReferenceHub? hub)
        {
            try
            {
                hub?.inventory.UserInventory.ReserveAmmo.Clear();
            }
            catch { }
        }

        public static void DestroyOwnedHub(ReferenceHub? hub)
        {
            try
            {
                if (hub?.gameObject != null)
                {
                    ClearReserveAmmo(hub);
                    NetworkServer.Destroy(hub.gameObject);
                }
            }
            catch { }
        }

        public static bool IsSupportedBotRole(RoleTypeId role)
        {
            switch (role)
            {
                case RoleTypeId.ClassD:
                case RoleTypeId.Scientist:
                case RoleTypeId.FacilityGuard:
                case RoleTypeId.NtfPrivate:
                case RoleTypeId.NtfSergeant:
                case RoleTypeId.NtfSpecialist:
                case RoleTypeId.NtfCaptain:
                case RoleTypeId.ChaosConscript:
                case RoleTypeId.ChaosRifleman:
                case RoleTypeId.ChaosRepressor:
                case RoleTypeId.ChaosMarauder:
                case RoleTypeId.Tutorial:
                    return role.TryGetRoleTemplate<IFpcRole>(out _) &&
                        HitboxIdentity.IsEnemy(role, RoleTypeId.Tutorial);
                default:
                    return false;
            }
        }
    }
}
