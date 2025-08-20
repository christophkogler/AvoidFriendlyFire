using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AvoidFriendlyFire
{
    public class FireCalculations
    {
        public static HashSet<int> GetFireCone(FireProperties fireProperties)
        {
            if (!fireProperties.ArePointsVisibleAndValid())
                return null;

            fireProperties.AdjustForLeaning();

            // Compute original and optimized cones side-by-side for verification.
            var originalDescriptor = fireProperties.GetMissAreaDescriptor();
            var optimizedDescriptor = fireProperties.GetMissAreaDescriptorOptimized();

            var originalCone = ComputeFireConeFromDescriptor(fireProperties, originalDescriptor, false);
            var optimizedCone = ComputeFireConeFromDescriptor(fireProperties, optimizedDescriptor, true);

            if (!originalCone.SetEquals(optimizedCone))
            {
                var onlyInOriginal = originalCone.Count(idx => !optimizedCone.Contains(idx));
                var onlyInOptimized = optimizedCone.Count(idx => !originalCone.Contains(idx));
                Log.Warning($"AvoidFriendlyFire: Optimized fire cone differs from original. Original={originalCone.Count}, Optimized={optimizedCone.Count}, OnlyInOriginal={onlyInOriginal}, OnlyInOptimized={onlyInOptimized}");
            }

            // Preserve current behavior by returning the original result for now.
            return originalCone;
        }

        private static IEnumerable<int> GetShootablePointsBetween(
            IntVec3 origin, IntVec3 target, Map map)
        {
            foreach (var point in GenSight.PointsOnLineOfSight(origin, target))
            {
                if (!point.CanBeSeenOver(map))
                    yield break;

                // Nearby pawns do not receive friendly fire
                if (IsInCloseRange(origin, point))
                {
                    continue;
                }

                yield return map.cellIndices.CellToIndex(point.x, point.z);
            }

            if (!IsAdjacent(origin, target))
                yield return map.cellIndices.CellToIndex(target.x, target.z);
        }

        private static bool IsInCloseRange(IntVec3 origin, IntVec3 point)
        {
            var checkedCellToOriginDistance = point - origin;
            var xDiff = Math.Abs(checkedCellToOriginDistance.x);
            var zDiff = Math.Abs(checkedCellToOriginDistance.z);
            if ((xDiff == 0 && zDiff < 5) || (zDiff == 0 && xDiff < 5))
                return true;

            if (xDiff > 0 && zDiff > 0 && xDiff + zDiff < 6)
                return true;

            return false;
        }

        private static bool IsAdjacent(IntVec3 origin, IntVec3 point)
        {
            IntVec3[] adjustmentVector = GenAdj.AdjacentCells;
            const int adjustmentCount = 8;
            for (var i = 0; i < adjustmentCount; i++)
            {
                var adjacentPoint = origin + adjustmentVector[i];
                if (point == adjacentPoint)
                    return true;
            }

            return false;
        }

        private static HashSet<int> ComputeFireConeFromDescriptor(
            FireProperties fireProperties,
            MissAreaDescriptor missAreaDescriptor,
            bool applyFarSideFilter)
        {
            var result = new HashSet<int>();
            var map = Find.CurrentMap;

            // Vector from target to origin for far-side checks
            IntVec3 centerToOrigin = fireProperties.Origin - fireProperties.Target;

            for (var i = 0; i < missAreaDescriptor.AdjustmentCount; i++)
            {
                var offset = missAreaDescriptor.AdjustmentVector[i];
                var splashTarget = fireProperties.Target + offset;

                if (applyFarSideFilter)
                {
                    IntVec3 centerToSample = splashTarget - fireProperties.Target;
                    long dot = (long)centerToOrigin.x * centerToSample.x + (long)centerToOrigin.z * centerToSample.z;
                    if (dot > 0)
                        continue; // Skip near-side samples
                }

                result.UnionWith(GetShootablePointsBetween(fireProperties.Origin, splashTarget, map));
            }

            return result;
        }
    }
}
