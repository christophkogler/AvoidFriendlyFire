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

        private readonly Dictionary<PerTickSafetyKey, PerTickSafetyResult> _perTickSafeShotCache =
            new Dictionary<PerTickSafetyKey, PerTickSafetyResult>();

        private int _perTickCacheTick = -1;

        private readonly List<Pawn> _relevantPawnsCache = new List<Pawn>();

        private int _relevantPawnsCacheTick = -1;

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

            EnsurePerTickCacheFresh();

            var casterMap = fireProperties.CasterMap;

            var perTickSafetyKey = CreatePerTickSafetyKey(fireProperties);
            if (_perTickSafeShotCache.TryGetValue(perTickSafetyKey, out var cachedResult))
            {
                if (!cachedResult.IsSafe)
                {
                    var blockerPawn = cachedResult.BlockerPawn;
                    if (blockerPawn != null &&
                        blockerPawn.Spawned &&
                        !blockerPawn.Dead &&
                        blockerPawn.Map == casterMap)
                    {
                        Main.Instance.PawnStatusTracker.AddBlockedShooter(fireProperties.Caster, blockerPawn);
                    }
                }

                return cachedResult.IsSafe;
            }

            EnsureRelevantPawnsCacheFresh(casterMap);

            HashSet<int> cachedFireCone;
            bool originAlreadyAdjusted = false;

            fireProperties.AdjustForLeaning();
            originAlreadyAdjusted = true;

            if (!TryGetCachedFireConeFor(fireProperties, out cachedFireCone))
            {
                var approximateMissRadius = fireProperties.GetApproximateMissRadius();
                var capsulePrefilterScope = PerfMetrics.Measure(PerfSection.CapsulePrefilter);
                bool capsuleFoundPawn;
                try
                {
                    capsuleFoundPawn = AreAnyRelevantPawnsInApproximateDangerZone(
                        fireProperties,
                        approximateMissRadius,
                        _relevantPawnsCache);
                }
                finally
                {
                    capsulePrefilterScope.Dispose();
                }

                if (!capsuleFoundPawn)
                {
                    _perTickSafeShotCache[perTickSafetyKey] = PerTickSafetyResult.Safe();
                    return true;
                }

                PerfMetrics.IncrementCounter(PerfCounter.CapsuleHitPawn);
                PerfMetrics.IncrementCounter(PerfCounter.ExactConeBuiltAfterCapsuleHit);
            }

            HashSet<int> fireCone = cachedFireCone ?? GetOrCreatedCachedFireConeFor(fireProperties, originAlreadyAdjusted);
            if (fireCone == null)
            {
                _perTickSafeShotCache[perTickSafetyKey] = PerTickSafetyResult.Safe();
                return true;
            }

            if (_relevantPawnsCache.Count == 0)
            {
                _perTickSafeShotCache[perTickSafetyKey] = PerTickSafetyResult.Safe();
                return true;
            }

            var cellIndices = casterMap.cellIndices;
            var originCell = fireProperties.Origin;
            var targetCell = fireProperties.Target;
            var shooterPawn = fireProperties.Caster;

            for (int pawnIndex = 0; pawnIndex < _relevantPawnsCache.Count; pawnIndex++)
            {
                Pawn candidatePawn = _relevantPawnsCache[pawnIndex];
                if (candidatePawn == shooterPawn)
                    continue;

                if (candidatePawn.Position == originCell || candidatePawn.Position == targetCell)
                    continue;

                var candidateCellIndex = cellIndices.CellToIndex(candidatePawn.Position);
                if (!fireCone.Contains(candidateCellIndex))
                    continue;

                Main.Instance.PawnStatusTracker.AddBlockedShooter(shooterPawn, candidatePawn);
                _perTickSafeShotCache[perTickSafetyKey] = PerTickSafetyResult.Unsafe(candidatePawn);
                return false;
            }

            _perTickSafeShotCache[perTickSafetyKey] = PerTickSafetyResult.Safe();
            return true;
            }
            finally
            {
                canHitTargetSafelyScope.Dispose();
            }
        }

        private static bool AreAnyRelevantPawnsInApproximateDangerZone(
            FireProperties fireProperties,
            float missRadiusCells,
            List<Pawn> relevantPawns)
        {
            if (relevantPawns.Count == 0)
                return false;

            var shooterPawn = fireProperties.Caster;
            var originCell = fireProperties.Origin;
            var targetCell = fireProperties.Target;

            var radiusAtTarget = missRadiusCells + 1.5f;

            for (int pawnIndex = 0; pawnIndex < relevantPawns.Count; pawnIndex++)
            {
                Pawn candidatePawn = relevantPawns[pawnIndex];
                if (candidatePawn == shooterPawn)
                    continue;

                if (candidatePawn.Position == originCell || candidatePawn.Position == targetCell)
                    continue;

                if (!FireCalculations.IsCellWithinApproximateTaperedCapsule(originCell, targetCell, candidatePawn.Position, radiusAtTarget))
                    continue;

                return true;
            }

            return false;
        }

        private void EnsureRelevantPawnsCacheFresh(Map map)
        {
            var currentTick = Find.TickManager?.TicksGame ?? -1;
            if (currentTick == _relevantPawnsCacheTick)
                return;

            _relevantPawnsCacheTick = currentTick;
            _relevantPawnsCache.Clear();

            var allPawnsSpawned = map.mapPawns?.AllPawnsSpawned;
            if (allPawnsSpawned == null || allPawnsSpawned.Count == 0)
                return;

            for (int pawnIndex = 0; pawnIndex < allPawnsSpawned.Count; pawnIndex++)
            {
                Pawn candidatePawn = allPawnsSpawned[pawnIndex];
                if (candidatePawn == null || candidatePawn.RaceProps == null || candidatePawn.Dead)
                    continue;

                var candidateFaction = candidatePawn.Faction;
                if (candidateFaction == null)
                    continue;

                if (candidatePawn.RaceProps.Humanlike)
                {
                    if (candidatePawn.IsPrisoner || candidatePawn.HostileTo(Faction.OfPlayer))
                        continue;
                }
                else if (!ShouldProtectAnimal(candidatePawn))
                {
                    continue;
                }

                if (IsPawnWearingUsefulShield(candidatePawn))
                    continue;

                _relevantPawnsCache.Add(candidatePawn);
            }
        }

        private bool TryGetCachedFireConeFor(FireProperties fireProperties, out HashSet<int> fireCone)
        {
            fireCone = null;

            var originIndex = fireProperties.OriginIndex;
            var targetIndex = fireProperties.TargetIndex;

            if (!_cachedFireCones.TryGetValue(originIndex, out var cachedFireConesFromOrigin))
                return false;

            if (!cachedFireConesFromOrigin.TryGetValue(targetIndex, out var cachedFireCone))
                return false;

            if (cachedFireCone.IsExpired())
                return false;

            cachedFireCone.Prolong();
            fireCone = cachedFireCone.FireCone;
            return true;
        }

        private void EnsurePerTickCacheFresh()
        {
            var currentTick = Find.TickManager?.TicksGame ?? -1;
            if (currentTick == _perTickCacheTick)
                return;

            _perTickCacheTick = currentTick;
            _perTickSafeShotCache.Clear();
        }

        private static PerTickSafetyKey CreatePerTickSafetyKey(FireProperties fireProperties)
        {
            var pawn = fireProperties.Caster;
            var map = fireProperties.CasterMap;
            var shooterPositionIndex = map.cellIndices.CellToIndex(pawn.Position);
            var primaryWeaponId = pawn.equipment?.Primary?.thingIDNumber ?? 0;

            return new PerTickSafetyKey(
                map.uniqueID,
                pawn.thingIDNumber,
                shooterPositionIndex,
                fireProperties.TargetIndex,
                primaryWeaponId);
        }

        private readonly struct PerTickSafetyResult
        {
            public readonly bool IsSafe;
            public readonly Pawn BlockerPawn;

            private PerTickSafetyResult(bool isSafe, Pawn blockerPawn)
            {
                IsSafe = isSafe;
                BlockerPawn = blockerPawn;
            }

            public static PerTickSafetyResult Safe()
            {
                return new PerTickSafetyResult(true, null);
            }

            public static PerTickSafetyResult Unsafe(Pawn blockerPawn)
            {
                return new PerTickSafetyResult(false, blockerPawn);
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

        private readonly struct PerTickSafetyKey : System.IEquatable<PerTickSafetyKey>
        {
            private readonly int _mapId;
            private readonly int _shooterId;
            private readonly int _shooterPositionIndex;
            private readonly int _targetIndex;
            private readonly int _primaryWeaponId;

            public PerTickSafetyKey(
                int mapId,
                int shooterId,
                int shooterPositionIndex,
                int targetIndex,
                int primaryWeaponId)
            {
                _mapId = mapId;
                _shooterId = shooterId;
                _shooterPositionIndex = shooterPositionIndex;
                _targetIndex = targetIndex;
                _primaryWeaponId = primaryWeaponId;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 31) + _mapId;
                    hash = (hash * 31) + _shooterId;
                    hash = (hash * 31) + _shooterPositionIndex;
                    hash = (hash * 31) + _targetIndex;
                    hash = (hash * 31) + _primaryWeaponId;
                    return hash;
                }
            }

            public override bool Equals(object obj)
            {
                if (!(obj is PerTickSafetyKey))
                    return false;

                return Equals((PerTickSafetyKey)obj);
            }

            public bool Equals(PerTickSafetyKey otherKey)
            {
                return _mapId == otherKey._mapId &&
                       _shooterId == otherKey._shooterId &&
                       _shooterPositionIndex == otherKey._shooterPositionIndex &&
                       _targetIndex == otherKey._targetIndex &&
                       _primaryWeaponId == otherKey._primaryWeaponId;
            }
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

        private HashSet<int> GetOrCreatedCachedFireConeFor(FireProperties fireProperties, bool originAlreadyAdjusted)
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
            var newFireCone = new CachedFireCone(FireCalculations.GetFireCone(fireProperties, originAlreadyAdjusted));

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
