using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace AvoidFriendlyFire
{
    public class ExtendedDataStorage : WorldComponent
    {
        private Dictionary<int, ExtendedPawnData> _store =
            new Dictionary<int, ExtendedPawnData>();

        private Dictionary<int, ExtendedTurretData> _turretStore =
            new Dictionary<int, ExtendedTurretData>();

        private List<int> _idWorkingList;

        private List<ExtendedPawnData> _extendedPawnDataWorkingList;

        private List<int> _turretIdWorkingList;

        private List<ExtendedTurretData> _extendedTurretDataWorkingList;


        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(
                ref _store, "store", 
                LookMode.Value, LookMode.Deep, 
                ref _idWorkingList, ref _extendedPawnDataWorkingList);
            Scribe_Collections.Look(
                ref _turretStore, "turretStore",
                LookMode.Value, LookMode.Deep,
                ref _turretIdWorkingList, ref _extendedTurretDataWorkingList);
        }

        // Return the associate extended data for a given Pawn, creating a new association
        // if required.
        public ExtendedPawnData GetExtendedDataFor(Pawn pawn)
        {

            var id = pawn.thingIDNumber;
            if (_store.TryGetValue(id, out ExtendedPawnData data))
            {
                return data;
            }

            var newExtendedData = new ExtendedPawnData();

            _store[id] = newExtendedData;
            return newExtendedData;
        }

        public bool CanTrackPawn(Pawn pawn)
        {
            return pawn?.Faction != null && pawn.Faction == Faction.OfPlayer;
        }

        public bool ShouldPawnAvoidFriendlyFire(Pawn pawn)
        {
            if (!CanTrackPawn(pawn))
                return false;

            if (!GetExtendedDataFor(pawn).AvoidFriendlyFire)
                return false;

            if (!FireConeOverlay.HasValidWeapon(pawn))
                return false;

            return true;
        }

        public ExtendedTurretData GetExtendedDataFor(Thing turret)
        {
            var id = turret.thingIDNumber;
            if (_turretStore.TryGetValue(id, out ExtendedTurretData data))
            {
                return data;
            }

            var newExtendedData = new ExtendedTurretData();
            _turretStore[id] = newExtendedData;
            return newExtendedData;
        }

        public bool CanTrackTurret(Thing turret)
        {
            return turret?.Faction == Faction.OfPlayer && GetTurretVerb(turret) != null;
        }

        public bool ShouldTurretAvoidFriendlyFire(Thing turret)
        {
            if (!Main.Instance.ShouldEnableTurretControl())
                return false;

            if (!CanTrackTurret(turret))
                return false;

            if (!GetExtendedDataFor(turret).AvoidFriendlyFire)
                return false;

            if (!FireConeOverlay.HasValidWeapon(GetTurretVerb(turret)))
                return false;

            return true;
        }

        public void DeleteExtendedDataFor(Pawn pawn)
        {
            _store.Remove(pawn.thingIDNumber);
        }

        public static Verb GetTurretVerb(Thing turret)
        {
            if (turret is Building_TurretGun buildingTurret)
                return buildingTurret.AttackVerb;

            return turret?.TryGetComp<CompTurretGun>()?.AttackVerb;
        }

        public ExtendedDataStorage(World world) : base(world)
        {
        }
    }
}
