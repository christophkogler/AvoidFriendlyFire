using HarmonyLib;
using RimWorld;
using Verse;

namespace AvoidFriendlyFire
{
    [HarmonyPatch(typeof(Building_TurretGun), "TryFindNewTarget")]
    public class Building_TurretGun_TryFindNewTarget_Patch
    {
        public static void Postfix(Building_TurretGun __instance, ref LocalTargetInfo __result)
        {
            if (!Main.Instance.IsModEnabled())
                return;

            if (!__result.IsValid)
                return;

            if (!Main.Instance.GetExtendedDataStorage().ShouldTurretAvoidFriendlyFire(__instance))
                return;

            if (__instance.Position.DistanceTo(__result.Cell) <= 2f)
                return;

            if (Main.Instance.GetFireManager().CanHitTargetSafely(
                new FireProperties(__instance, __instance.AttackVerb, __result.Cell)))
            {
                return;
            }

            __result = LocalTargetInfo.Invalid;
        }
    }
}
