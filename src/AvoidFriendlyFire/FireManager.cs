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

        private readonly Dictionary<PerTickSafetyKey, bool> _perTickSafeShotCache =
            new Dictionary<PerTickSafetyKey, bool>();

        private int _perTickCacheTick = -1;

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

            var perTickSafetyKey = CreatePerTickSafetyKey(fireProperties);
            if (_perTickSafeShotCache.ContainsKey(perTickSafetyKey))
                return true;

            HashSet<int> fireCone = GetOrCreatedCachedFireConeFor(fireProperties);
            if (fireCone == null)
            {
                _perTickSafeShotCache[perTickSafetyKey] = true;
                return true;
            }

            var map = fireProperties.CasterMap;
            var allPawnsSpawned = map.mapPawns?.AllPawnsSpawned;
            if (allPawnsSpawned == null || allPawnsSpawned.Count == 0)
            {
                _perTickSafeShotCache[perTickSafetyKey] = true;
                return true;
            }

            var cellIndices = map.cellIndices;
            var originCell = fireProperties.Origin;
            var targetCell = fireProperties.Target;
            var shooterPawn = fireProperties.Caster;

            for (int pawnIndex = 0; pawnIndex < allPawnsSpawned.Count; pawnIndex++)
            {
                Pawn candidatePawn = allPawnsSpawned[pawnIndex];
                if (candidatePawn == null || candidatePawn.RaceProps == null || candidatePawn.Dead)
                    continue;

                if (candidatePawn == shooterPawn)
                    continue;

                if (candidatePawn.Position == originCell || candidatePawn.Position == targetCell)
                    continue;

                var candidateCellIndex = cellIndices.CellToIndex(candidatePawn.Position);
                if (!fireCone.Contains(candidateCellIndex))
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

                Main.Instance.PawnStatusTracker.AddBlockedShooter(shooterPawn, candidatePawn);
                return false;
            }

            _perTickSafeShotCache[perTickSafetyKey] = true;
            return true;
            }
            finally
            {
                canHitTargetSafelyScope.Dispose();
            }
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
                if (obj is not PerTickSafetyKey otherKey)
                    return false;

                return Equals(otherKey);
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
