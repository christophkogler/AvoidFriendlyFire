using System;
using System.Collections.Generic;
using Verse;

namespace AvoidFriendlyFire
{
    public struct MissAreaDescriptor
    {
        public IntVec3[] AdjustmentVector;
        public int AdjustmentStartIndex;
        public int AdjustmentCount;

        public MissAreaDescriptor(IntVec3[] adjustmentVector, int adjustmentCount)
        {
            AdjustmentVector = adjustmentVector;
            AdjustmentStartIndex = 0;
            AdjustmentCount = adjustmentCount;
        }

        public MissAreaDescriptor(IntVec3[] adjustmentVector, int adjustmentStartIndex, int adjustmentCount)
        {
            AdjustmentVector = adjustmentVector;
            AdjustmentStartIndex = adjustmentStartIndex;
            AdjustmentCount = adjustmentCount;
        }
    }


    public class FireProperties
    {
        public IntVec3 Target;

        public Map CasterMap => Caster.Map;

        public IntVec3 Origin;

        public float ForcedMissRadius => _weaponVerb.verbProps.ForcedMissRadius;

        public int OriginIndex => CasterMap.cellIndices.CellToIndex(Origin);

        public int TargetIndex => CasterMap.cellIndices.CellToIndex(Target);

        public Pawn Caster { get; }

        private readonly Verb _weaponVerb;

        public FireProperties(Pawn caster, IntVec3 target)
        {
            Target = target;
            Caster = caster;
            _weaponVerb = GetEquippedWeaponVerb(caster);
            Origin = Caster.Position;
        }

        public bool ArePointsVisibleAndValid()
        {
            if (Target == Origin)
                return false;

            if (!Target.InBounds(CasterMap) || Target.Fogged(CasterMap))
                return false;

            return true;
        }

        public void AdjustForLeaning()
        {
            if (HasClearShotFrom(Origin))
                return;

            // If we got this far the shot is possible, but there is no straight LoS.
            // Must be shooting around a corner, so we need to use a different origin.
            var leaningPositions = new List<IntVec3>();
            ShootLeanUtility.LeanShootingSourcesFromTo(Origin, Target, CasterMap, leaningPositions);
            foreach (var leaningPosition in leaningPositions)
            {
                if (HasClearShotFrom(leaningPosition))
                {
                    Origin = leaningPosition;
                    return;
                }
            }

        }

        public float GetAimOnTargetChance()
        {
            var distance = (Target - Origin).LengthHorizontal;

            var factorFromShooterAndDist = ShotReport.HitFactorFromShooter(Caster, distance);

            var factorFromEquipment = _weaponVerb.verbProps.GetHitChanceFactor(
                _weaponVerb.EquipmentSource, distance);

            var factorFromWeather = 1f;
            if (!Caster.Position.Roofed(CasterMap) || !Target.Roofed(CasterMap))
            {
                factorFromWeather = CasterMap.weatherManager.CurWeatherAccuracyMultiplier;
            }

            var factorFromCoveringGas = 1f;
            foreach (var point in GenSight.PointsOnLineOfSight(Origin, Target))
            {
                if (!point.CanBeSeenOver(CasterMap))
                    break;

                if (point.AnyGas(CasterMap, GasType.BlindSmoke))
                    factorFromCoveringGas = 1f - GasUtility.BlindingGasAccuracyPenalty;
            }

            var result = factorFromShooterAndDist * factorFromEquipment * factorFromWeather *
                         factorFromCoveringGas;
            if (result < 0.0201f)
                result = 0.0201f;

            return result;
        }

        public float GetApproximateMissRadius()
        {
            var adjustedMissRadius = CalculateAdjustedForcedMiss();
            if (adjustedMissRadius > 0.5f)
                return ForcedMissRadius;

            if (!Main.Instance.ShouldEnableAccurateMissRadius())
                return 2f;

            var missRadius = ShootTuning.MissDistanceFromAimOnChanceCurves.Evaluate(GetAimOnTargetChance(), 1f);
            if (missRadius < 0f)
                return 2f;

            return missRadius;
        }

        private bool HasClearShotFrom(IntVec3 tryFromOrigin)
        {
            var lineStarted = false;
            foreach (var point in GenSight.PointsOnLineOfSight(tryFromOrigin, Target))
            {
                if (!point.CanBeSeenOver(CasterMap))
                    return false;

                lineStarted = true;
            }

            return lineStarted;
        }


        public MissAreaDescriptor GetMissAreaDescriptor()
        {
            var adjustedMissRadius = CalculateAdjustedForcedMiss();

            if (adjustedMissRadius > 0.5f)
            {
                // Build a hollow ring with adjustable thickness at the outer edge of forced miss radius
                int ringWidthCells = Main.Instance.GetMinCheckedDiskWidth();
                if (ringWidthCells < 1) ringWidthCells = 1;
                float innerRadius = ForcedMissRadius - ringWidthCells;
                if (innerRadius < 0f) innerRadius = 0f;
                int forcedMissOuterCount = GenRadial.NumCellsInRadius(ForcedMissRadius);
                // If inner radius collapses to 0 or less, include center by starting from index 0
                int forcedMissInnerCount = innerRadius <= 0f ? 0 : GenRadial.NumCellsInRadius(innerRadius);
                int forcedMissRingCount = forcedMissOuterCount - forcedMissInnerCount;
                return new MissAreaDescriptor(GenRadial.RadialPattern, forcedMissInnerCount, forcedMissRingCount);
            }

            if (!Main.Instance.ShouldEnableAccurateMissRadius())
            {
                // Keep adjacent cells behavior when accurate miss radius is disabled
                return new MissAreaDescriptor(GenAdj.AdjacentCells, 8);
            }

            var missRadius = ShootTuning.MissDistanceFromAimOnChanceCurves.Evaluate(
                GetAimOnTargetChance(), 1f);

            if (missRadius < 0)
                return new MissAreaDescriptor(GenAdj.AdjacentCells, 8);

            // Use a hollow ring with adjustable thickness at the outer edge of computed miss radius
            int minRingWidthCells = Main.Instance.GetMinCheckedDiskWidth();
            if (minRingWidthCells < 1) minRingWidthCells = 1;
            float innerMissRadius = missRadius - minRingWidthCells;
            if (innerMissRadius < 0f) innerMissRadius = 0f;
            int outerCount = GenRadial.NumCellsInRadius(missRadius);
            // If inner radius collapses to 0 or less, include center by starting from index 0
            int innerCount = innerMissRadius <= 0f ? 0 : GenRadial.NumCellsInRadius(innerMissRadius);
            int ringCount = outerCount - innerCount;
            return new MissAreaDescriptor(GenRadial.RadialPattern, innerCount, ringCount);
        }

        

        private float CalculateAdjustedForcedMiss()
        {
            return ForcedMissRadius <= 0.5f
                ? 0f
                : VerbUtility.CalculateAdjustedForcedMiss(ForcedMissRadius, Target - Origin);
        }

        public static Verb GetEquippedWeaponVerb(Pawn pawn)
        {
            return pawn.equipment?.PrimaryEq?.PrimaryVerb;
        }
    }
}
