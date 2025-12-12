using HarmonyLib;
using RimWorld;
using Verse;

namespace AvoidFriendlyFire
{
    [HarmonyPatch(typeof(Targeter), "TargeterUpdate")]
    public static class Targeter_TargeterUpdate_Patch
    {
        public static void Postfix(ref Targeter __instance)
        {
            var scope = PerfMetrics.Measure(PerfSection.Patch_Targeter_TargeterUpdate);
            try
            {
            if (!Main.Instance.IsModEnabled())
                return;

            var shouldEnableOverlay = false;
            if (__instance.targetingSource != null &&
                !__instance.targetingSource.IsMeleeAttack &&
                __instance.targetingSource is Verb verb &&
                verb.HighlightFieldRadiusAroundTarget(out _) <= 0.2f)
            {
                shouldEnableOverlay = true;
            }

            Main.Instance.UpdateFireConeOverlay(shouldEnableOverlay);
            }
            finally
            {
                scope.Dispose();
            }
        }
    }
}
