using HarmonyLib;
using RimWorld;
using Verse;

namespace AvoidFriendlyFire
{
    [HarmonyPatch(typeof(Building_TurretGun), "OrderAttack")]
    public class Building_TurretGun_OrderAttack_Patch
    {
        public static bool Prefix(Building_TurretGun __instance, LocalTargetInfo targ)
        {
            if (!Main.Instance.IsModEnabled())
                return true;

            if (!targ.IsValid || rootDistanceTooShort(__instance.Position, targ.Cell))
                return true;

            if (!Main.Instance.GetExtendedDataStorage().ShouldTurretAvoidFriendlyFire(__instance))
                return true;

            return Main.Instance.GetFireManager().CanHitTargetSafely(
                new FireProperties(__instance, __instance.AttackVerb, targ.Cell));
        }

        private static bool rootDistanceTooShort(IntVec3 root, IntVec3 target)
        {
            return root.DistanceTo(target) <= 2f;
        }
    }
}
