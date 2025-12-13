using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AvoidFriendlyFire
{
    public class FireCalculations
    {
        // Approximation helper for the fast prefilter and (optionally) the overlay.
        //
        // Treat the shot path as a 2D segment (x/z) and the "danger zone" as a tapered capsule around it.
        // The capsule radius starts small near the shooter and increases linearly toward the target:
        //
        //   radius(t) = radiusAtOrigin + (radiusAtTarget - radiusAtOrigin) * t
        //
        // where t is the normalized position of the closest point on the segment to the candidate cell.
        public static bool IsCellWithinApproximateTaperedCapsule(
            IntVec3 originCell,
            IntVec3 targetCell,
            IntVec3 candidateCell,
            float radiusAtTarget)
        {
            const float radiusAtOrigin = 1f;

            float originX = originCell.x;
            float originZ = originCell.z;
            float targetX = targetCell.x;
            float targetZ = targetCell.z;
            float pointX = candidateCell.x;
            float pointZ = candidateCell.z;

            float segmentVectorX = targetX - originX;
            float segmentVectorZ = targetZ - originZ;

            float originToPointX = pointX - originX;
            float originToPointZ = pointZ - originZ;

            float segmentLengthSquared = segmentVectorX * segmentVectorX + segmentVectorZ * segmentVectorZ;
            if (segmentLengthSquared <= 0.0001f)
            {
                float dx = originToPointX;
                float dz = originToPointZ;
                return (dx * dx + dz * dz) <= (radiusAtOrigin * radiusAtOrigin);
            }

            float projection = originToPointX * segmentVectorX + originToPointZ * segmentVectorZ;

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

            float deltaX = pointX - closestX;
            float deltaZ = pointZ - closestZ;
            float distanceSquaredToSegment = (deltaX * deltaX) + (deltaZ * deltaZ);

            float localRadius = radiusAtOrigin + ((radiusAtTarget - radiusAtOrigin) * t);
            float localRadiusSquared = localRadius * localRadius;

            return distanceSquaredToSegment <= localRadiusSquared;
        }

        public static HashSet<int> GetApproximateFireCone(FireProperties fireProperties)
        {
            if (!fireProperties.ArePointsVisibleAndValid())
                return null;

            fireProperties.AdjustForLeaning();

            var map = fireProperties.CasterMap;
            var cellIndices = map.cellIndices;
            var originCell = fireProperties.Origin;
            var targetCell = fireProperties.Target;

            var radiusAtTarget = fireProperties.GetApproximateMissRadius() + 1.5f;
            var padding = (int)Math.Ceiling(radiusAtTarget);

            int minX = Math.Min(originCell.x, targetCell.x) - padding;
            int maxX = Math.Max(originCell.x, targetCell.x) + padding;
            int minZ = Math.Min(originCell.z, targetCell.z) - padding;
            int maxZ = Math.Max(originCell.z, targetCell.z) + padding;

            if (minX < 0) minX = 0;
            if (minZ < 0) minZ = 0;
            if (maxX >= map.Size.x) maxX = map.Size.x - 1;
            if (maxZ >= map.Size.z) maxZ = map.Size.z - 1;

            var result = new HashSet<int>();
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    var cell = new IntVec3(x, 0, z);
                    if (!IsCellWithinApproximateTaperedCapsule(originCell, targetCell, cell, radiusAtTarget))
                        continue;

                    result.Add(cellIndices.CellToIndex(x, z));
                }
            }

            return result;
        }

        public static HashSet<int> GetFireCone(FireProperties fireProperties)
        {
            return GetFireCone(fireProperties, false);
        }

        public static HashSet<int> GetFireCone(FireProperties fireProperties, bool originAlreadyAdjusted)
        {
            var getFireConeScope = PerfMetrics.Measure(PerfSection.GetFireCone);
            try
            {
            if (!fireProperties.ArePointsVisibleAndValid())
                return null;

            if (!originAlreadyAdjusted)
                fireProperties.AdjustForLeaning();

            // Build descriptor using optimized logic and compute cone once
            var descriptor = fireProperties.GetMissAreaDescriptor();
            var applyFarSideFilter = Main.Instance.ShouldUseFarSideFilter();
            return ComputeFireConeFromDescriptor(fireProperties, descriptor, applyFarSideFilter);
            }
            finally
            {
                getFireConeScope.Dispose();
            }
        }

        private static void AddShootablePointsBetween(
            IntVec3 origin,
            IntVec3 target,
            Map map,
            HashSet<int> destinationCellIndices)
        {
            var scope = PerfMetrics.Measure(PerfSection.AddShootablePointsBetween);
            try
            {
            var cellIndices = map.cellIndices;

            foreach (var point in GenSight.PointsOnLineOfSight(origin, target))
            {
                if (!point.CanBeSeenOver(map))
                    return;

                if (IsInCloseRange(origin, point))
                    continue;

                destinationCellIndices.Add(cellIndices.CellToIndex(point.x, point.z));
            }

            if (!IsAdjacent(origin, target))
                destinationCellIndices.Add(cellIndices.CellToIndex(target.x, target.z));
            }
            finally
            {
                scope.Dispose();
            }
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
            var scope = PerfMetrics.Measure(PerfSection.ComputeFireCone);
            try
            {
                var result = new HashSet<int>();
                var map = fireProperties.CasterMap;

            // Vector from target to origin for far-side checks
            IntVec3 centerToOrigin = fireProperties.Origin - fireProperties.Target;

            for (var i = 0; i < missAreaDescriptor.AdjustmentCount; i++)
            {
                var offset = missAreaDescriptor.AdjustmentVector[missAreaDescriptor.AdjustmentStartIndex + i];
                var splashTarget = fireProperties.Target + offset;

                    if (applyFarSideFilter)
                    {
                        IntVec3 centerToSample = splashTarget - fireProperties.Target;
                        long dot = (long)centerToOrigin.x * centerToSample.x + (long)centerToOrigin.z * centerToSample.z;
                        if (dot > 0)
                            continue; // Skip near-side samples
                    }

                    AddShootablePointsBetween(fireProperties.Origin, splashTarget, map, result);
                }

                return result;
            }
            finally
            {
                scope.Dispose();
            }
        }
    }
}
