using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace AvoidFriendlyFire
{
    [HarmonyPatch(typeof(AttackTargetFinder), "BestAttackTarget")]
    public class AttackTargetFinder_BestAttackTarget_Patch
    {
        public static bool Prefix(IAttackTargetSearcher searcher, ref Predicate<Thing> validator)
        {
            var scope = PerfMetrics.Measure(PerfSection.Patch_AttackTargetFinder_BestAttackTarget);
            try
            {
            if (!Main.Instance.IsModEnabled())
                return true;

            if (validator != null)
                return true;

            var shooter = searcher.Thing as Pawn;
            if (!Main.Instance.GetExtendedDataStorage().ShouldPawnAvoidFriendlyFire(shooter))
                return true;

            validator = target => Main.Instance.GetFireManager().CanHitTargetSafely(
                new FireProperties(shooter, target.Position));

            return true;
            }
            finally
            {
                scope.Dispose();
            }
        }
   }
}
