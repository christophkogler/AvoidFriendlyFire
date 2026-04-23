using HarmonyLib;
using Verse;

namespace AvoidFriendlyFire
{
    [HarmonyPatch(typeof(Verb), "CanHitTargetFrom")]
    public class Verb_CanHitTargetFrom_Patch
    {
        public static void Postfix(ref Verb __instance, ref bool __result, IntVec3 root,
            LocalTargetInfo targ)
        {
            var scope = PerfMetrics.Measure(PerfSection.Patch_Verb_CanHitTargetFrom);
            try
            {
            using (FireProperties.SuppressDynamicPawnVerbLookup())
            {
            if (!Main.Instance.IsModEnabled())
                return;

            if (!__result || !targ.IsValid)
                return;

            if (targ.Thing != null && targ.Thing == __instance.caster)
                return;

            if (root.DistanceTo(targ.Cell) <= 2f)
                return;

            if (__instance.caster is Pawn pawn)
            {
                if (!Main.Instance.GetExtendedDataStorage().ShouldPawnAvoidFriendlyFire(pawn, __instance))
                    return;

                __result = Main.Instance.GetFireManager().CanHitTargetSafely(
                    new FireProperties(pawn, __instance, targ.Cell));
                return;
            }

            if (!(__instance.caster is Thing casterThing))
                return;

            if (!Main.Instance.GetExtendedDataStorage().ShouldTurretAvoidFriendlyFire(casterThing))
                return;

            __result = Main.Instance.GetFireManager().CanHitTargetSafely(
                new FireProperties(casterThing, __instance, targ.Cell));
            }
            }
            finally
            {
                scope.Dispose();
            }
        }
    }
}
