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
                    var pawns = casterMap.mapPawns?.AllPawnsSpawned;
                    if (pawns != null && cachedResult.BlockerPawnThingId != 0)
                    {
                        for (int i = 0; i < pawns.Count; i++)
                        {
                            var candidate = pawns[i];
                            if (candidate != null && candidate.thingIDNumber == cachedResult.BlockerPawnThingId)
                            {
                                Main.Instance.PawnStatusTracker.AddBlockedShooter(fireProperties.Caster, candidate);
                                break;
                            }
                        }
                    }
                }

                return cachedResult.IsSafe;
            }

            HashSet<int> cachedFireCone;
            bool originAlreadyAdjusted = false;
            if (!TryGetCachedFireConeFor(fireProperties, out cachedFireCone))
            {
                fireProperties.AdjustForLeaning();
                originAlreadyAdjusted = true;

                var approximateMissRadius = fireProperties.GetApproximateMissRadius();
                if (!AreAnyRelevantPawnsInApproximateDangerZone(fireProperties, approximateMissRadius))
                {
                    _perTickSafeShotCache[perTickSafetyKey] = PerTickSafetyResult.Safe();
                    return true;
                }
            }

            HashSet<int> fireCone = cachedFireCone ?? GetOrCreatedCachedFireConeFor(fireProperties, originAlreadyAdjusted);
            if (fireCone == null)
            {
                _perTickSafeShotCache[perTickSafetyKey] = PerTickSafetyResult.Safe();
                return true;
            }

            var allPawnsSpawned = casterMap.mapPawns?.AllPawnsSpawned;
            if (allPawnsSpawned == null || allPawnsSpawned.Count == 0)
            {
                _perTickSafeShotCache[perTickSafetyKey] = PerTickSafetyResult.Safe();
                return true;
            }

            var cellIndices = casterMap.cellIndices;
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
                _perTickSafeShotCache[perTickSafetyKey] = PerTickSafetyResult.Unsafe(candidatePawn.thingIDNumber);
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

        private bool AreAnyRelevantPawnsInApproximateDangerZone(FireProperties fireProperties, float missRadiusCells)
        {
            var map = fireProperties.CasterMap;
            var allPawnsSpawned = map.mapPawns?.AllPawnsSpawned;
            if (allPawnsSpawned == null || allPawnsSpawned.Count == 0)
                return false;

            var shooterPawn = fireProperties.Caster;
            var originCell = fireProperties.Origin;
            var targetCell = fireProperties.Target;

            var radiusAtTarget = missRadiusCells + 1.5f;

            for (int pawnIndex = 0; pawnIndex < allPawnsSpawned.Count; pawnIndex++)
            {
                Pawn candidatePawn = allPawnsSpawned[pawnIndex];
                if (candidatePawn == null || candidatePawn.RaceProps == null || candidatePawn.Dead)
                    continue;

                if (candidatePawn == shooterPawn)
                    continue;

                if (candidatePawn.Position == originCell || candidatePawn.Position == targetCell)
                    continue;

                if (!IsCellWithinApproximateTaperedCapsule(originCell, targetCell, candidatePawn.Position, radiusAtTarget))
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

                return true;
            }

            return false;
        }

        // Fast approximation: treat the shot path as a 2D segment (x/z) and the "danger zone" as a
        // tapered capsule around it. The capsule radius starts small near the shooter and increases
        // linearly toward the target:
        //
        //   radius(t) = radiusAtOrigin + (radiusAtTarget - radiusAtOrigin) * t
        //
        // where t is the normalized position of the closest point on the segment to the candidate cell.
        //
        // This is deliberately conservative/cheap: it only decides whether we should run the more
        // expensive LoS-based cone check, not whether the shot is safe.
        private static bool IsCellWithinApproximateTaperedCapsule(
            IntVec3 originCell,
            IntVec3 targetCell,
            IntVec3 candidateCell,
            float radiusAtTarget)
        {
            // Radius is intentionally small near the shooter because friendly fire close to the shooter
            // is already unlikely and the precise cone will exclude close-range pawns anyway.
            const float radiusAtOrigin = 1f;

            // Convert IntVec3s to 2D points on the map plane.
            float originX = originCell.x;
            float originZ = originCell.z;
            float targetX = targetCell.x;
            float targetZ = targetCell.z;
            float pointX = candidateCell.x;
            float pointZ = candidateCell.z;

            // v = segment direction from origin to target
            float segmentVectorX = targetX - originX;
            float segmentVectorZ = targetZ - originZ;

            // w = vector from origin to candidate point
            float originToPointX = pointX - originX;
            float originToPointZ = pointZ - originZ;

            // Compute squared segment length: |v|^2
            float segmentLengthSquared = segmentVectorX * segmentVectorX + segmentVectorZ * segmentVectorZ;
            if (segmentLengthSquared <= 0.0001f)
            {
                // Degenerate segment (origin ~= target): use the smaller radius at origin.
                float dx = originToPointX;
                float dz = originToPointZ;
                return (dx * dx + dz * dz) <= (radiusAtOrigin * radiusAtOrigin);
            }

            // projection = dot(w, v) gives how far along the segment the point projects (in |v|^2 units).
            float projection = originToPointX * segmentVectorX + originToPointZ * segmentVectorZ;

            // Clamp projection to the segment and compute t in [0, 1].
            // t represents where the closest point lies: 0 at origin, 1 at target.
            float t;
            float closestX;
            float closestZ;

            if (projection <= 0f)
            {
                t = 0f;
                closestX = originX;
                closestZ = originZ;
            }
            else if (projection >= segmentLengthSquared)
            {
                t = 1f;
                closestX = targetX;
                closestZ = targetZ;
            }
            else
            {
                t = projection / segmentLengthSquared;
                closestX = originX + (t * segmentVectorX);
                closestZ = originZ + (t * segmentVectorZ);
            }

            // Compute squared distance from candidate point to the closest point on the segment.
            float deltaX = pointX - closestX;
            float deltaZ = pointZ - closestZ;
            float distanceSquaredToSegment = (deltaX * deltaX) + (deltaZ * deltaZ);

            // Tapered radius at that position t. This produces a "cone-like" widening around the segment.
            // Using squared comparisons avoids an expensive sqrt on the distance.
            float localRadius = radiusAtOrigin + ((radiusAtTarget - radiusAtOrigin) * t);
            float localRadiusSquared = localRadius * localRadius;

            return distanceSquaredToSegment <= localRadiusSquared;
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
            public readonly int BlockerPawnThingId;

            private PerTickSafetyResult(bool isSafe, int blockerPawnThingId)
            {
                IsSafe = isSafe;
                BlockerPawnThingId = blockerPawnThingId;
            }

            public static PerTickSafetyResult Safe()
            {
                return new PerTickSafetyResult(true, 0);
            }

            public static PerTickSafetyResult Unsafe(int blockerPawnThingId)
            {
                return new PerTickSafetyResult(false, blockerPawnThingId);
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
