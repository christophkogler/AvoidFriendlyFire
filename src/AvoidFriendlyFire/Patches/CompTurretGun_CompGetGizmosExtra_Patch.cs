using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AvoidFriendlyFire
{
    [HarmonyPatch(typeof(CompTurretGun), "CompGetGizmosExtra")]
    public class CompTurretGun_CompGetGizmosExtra_Patch
    {
        public static void Postfix(ref IEnumerable<Gizmo> __result, CompTurretGun __instance)
        {
            if (!Main.Instance.IsModEnabled() || !Main.Instance.ShouldEnableTurretControl())
                return;

            if (__result == null || !__result.Any())
                return;

            var turretThing = __instance.parent;
            var extendedDataStore = Main.Instance.GetExtendedDataStorage();
            if (!extendedDataStore.CanTrackTurret(turretThing))
                return;

            if (!FireConeOverlay.HasValidWeapon(__instance.AttackVerb))
                return;

            var turretData = extendedDataStore.GetExtendedDataFor(turretThing);
            var gizmoList = __result.ToList();
            gizmoList.Add(new Command_Toggle
            {
                defaultLabel = "FALCFF.AvoidFriendlyFireTurret".Translate(),
                defaultDesc = "FALCFF.AvoidFriendlyFireTurretDesc".Translate(),
                icon = Resources.FriendlyFireIcon,
                isActive = () => turretData.AvoidFriendlyFire,
                toggleAction = () => turretData.AvoidFriendlyFire = !turretData.AvoidFriendlyFire
            });
            __result = gizmoList;
        }
    }
}
