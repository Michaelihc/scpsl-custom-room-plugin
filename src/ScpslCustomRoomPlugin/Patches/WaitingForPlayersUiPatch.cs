using HarmonyLib;

namespace ScpslCustomRoomPlugin.Patches
{
    [HarmonyPatch(typeof(GameCore.RoundStart), nameof(GameCore.RoundStart.Update))]
    internal static class WaitingForPlayersUiPatch
    {
        private static bool Prefix()
        {
            return !WarmupSelectionController.SuppressVanillaWaitingUi;
        }
    }
}
