using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace AvoidFriendlyFire
{
    public class FireManager
    {
        public bool SkipNextCheck;

        private readonly Dictionary<int, Dictionary<int, CachedFireCone>> _cachedFireCones
            = new Dictionary<int, Dictionary<int, CachedFireCone>>();

        private int _lastCleanupTick;

        public bool CanHitTargetSafely(FireProperties fireProperties)
        {
            var canHitTargetSafelyScope = PerfMetrics.Measure(PerfSection.CanHitTargetSafely);
            try
            {
            if (SkipNextCheck)
            {
                SkipNextCheck = false;
                return true;
            }

            Main.Instance.PawnStatusTracker.Remove(fireProperties.Caster);

            HashSet<int> fireCone = GetOrCreatedCachedFireConeFor(fireProperties);
            if (fireCone == null)
                return true;

            var map = fireProperties.CasterMap;
            foreach (int cellIndex in fireCone)
            {
                var cell = map.cellIndices.IndexToCell(cellIndex);
                if (cell == fireProperties.Origin || cell == fireProperties.Target)
                    continue;

                var thingsInCell = map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < thingsInCell.Count; i++)
                {
                    Pawn pawn = thingsInCell[i] as Pawn;
                    if (pawn == null || pawn.RaceProps == null || pawn.Dead)
                        continue;

                    if (pawn.Faction == null)
                        continue;

                    if (pawn.RaceProps.Humanlike)
                    {
                        if (pawn.IsPrisoner || pawn.HostileTo(Faction.OfPlayer))
                            continue;
                    }
                    else if (!ShouldProtectAnimal(pawn))
                    {
                        continue;
                    }

                    if (IsPawnWearingUsefulShield(pawn))
                        continue;

                    Main.Instance.PawnStatusTracker.AddBlockedShooter(fireProperties.Caster, pawn);

                    return false;
                }
            }

            return true;
            }
            finally
            {
                canHitTargetSafelyScope.Dispose();
            }
        }

        private bool IsPawnWearingUsefulShield(Pawn pawn)
        {
            if (!Main.Instance.ShouldIgnoreShieldedPawns())
                return false;

            if (pawn.apparel == null)
                return false;

            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                if (apparel.def != ThingDefOf.Apparel_ShieldBelt)
                    continue;

                var shield = apparel.AllComps.FirstOrDefault(c => c is CompShield) as CompShield;
                if (shield.ShieldState != ShieldState.Active)
                    return false;

                var energyMax = shield.parent.GetStatValue(StatDefOf.EnergyShieldEnergyMax);
                return shield.Energy / energyMax > 0.1f;
            }

            return false;
        }

        private static bool ShouldProtectAnimal(Pawn animal)
        {
            if (animal.Faction != Faction.OfPlayer)
                return false;

            if (Main.Instance.ShouldProtectAllColonyAnimals())
                return true;

            if (!Main.Instance.ShouldProtectPets())
                return false;

            return animal.playerSettings?.Master != null;
        }

        public void RemoveExpiredCones(int currentTick)
        {
            if (currentTick - _lastCleanupTick < 400)
                return;

            _lastCleanupTick = currentTick;

            var origins = _cachedFireCones.Keys.ToList();
            foreach (var origin in origins)
            {
                var cachedFireConesFromOneOrigin = _cachedFireCones[origin];
                var targets = cachedFireConesFromOneOrigin.Keys.ToList();
                foreach (var target in targets)
                {
                    var cachedFireCone = cachedFireConesFromOneOrigin[target];
                    if (cachedFireCone.IsExpired())
                    {
                        cachedFireConesFromOneOrigin.Remove(target);
                    }
                }

                if (cachedFireConesFromOneOrigin.Count == 0)
                {
                    _cachedFireCones.Remove(origin);
                }
            }
        }

        private HashSet<int> GetOrCreatedCachedFireConeFor(FireProperties fireProperties)
        {
            var scope = PerfMetrics.Measure(PerfSection.GetOrCreateCachedFireCone);
            try
            {
            var originIndex = fireProperties.OriginIndex;
            var targetIndex = fireProperties.TargetIndex;

            if (_cachedFireCones.TryGetValue(originIndex, out var cachedFireConesFromOrigin))
            {
                if (cachedFireConesFromOrigin.TryGetValue(targetIndex, out var cachedFireCone))
                {
                    if (!cachedFireCone.IsExpired())
                    {
                        cachedFireCone.Prolong();
                        return cachedFireCone.FireCone;
                    }
                }
            }

            // No cached firecone, create one
            var newFireCone = new CachedFireCone(FireCalculations.GetFireCone(fireProperties));

            if (!_cachedFireCones.ContainsKey(originIndex))
                _cachedFireCones.Add(originIndex, new Dictionary<int, CachedFireCone>());

            _cachedFireCones[originIndex][targetIndex] = newFireCone;

            return newFireCone.FireCone;
            }
            finally
            {
                scope.Dispose();
            }
        }
    }
}
