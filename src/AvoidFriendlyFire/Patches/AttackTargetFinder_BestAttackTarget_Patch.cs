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

            var shooterThing = searcher.Thing;
            if (shooterThing is Pawn shooterPawn)
            {
                if (!Main.Instance.GetExtendedDataStorage().ShouldPawnAvoidFriendlyFire(shooterPawn))
                    return true;

                var shooterVerb = FireProperties.GetRangedAttackVerb(shooterPawn);
                validator = target => Main.Instance.GetFireManager().CanHitTargetSafely(
                    new FireProperties(shooterPawn, shooterVerb, target.Position));
                return true;
            }

            if (!Main.Instance.GetExtendedDataStorage().ShouldTurretAvoidFriendlyFire(shooterThing))
                return true;

            var turretVerb = ExtendedDataStorage.GetTurretVerb(shooterThing);
            validator = target => Main.Instance.GetFireManager().CanHitTargetSafely(
                new FireProperties(shooterThing, turretVerb, target.Position));

            return true;
            }
            finally
            {
                scope.Dispose();
            }
        }
   }
}
