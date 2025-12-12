using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace AvoidFriendlyFire
{
    public class AvoidFriendlyFireSettings : ModSettings
    {
        public bool ModEnabled = true;
        public bool ShowOverlay = true;
        public bool ProtectPets = true;
        public bool ProtectColonyAnimals;
        public bool IgnoreShieldedPawns = true;
        public bool EnableWhenUndrafted;
        public bool EnableAccurateMissRadius = true;
        public bool UseFarSideFilter;
        public int MinCheckedDiskWidth = 2;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ModEnabled, "enabled", true);
            Scribe_Values.Look(ref ShowOverlay, "showOverlay", true);
            Scribe_Values.Look(ref ProtectPets, "protectPets", true);
            Scribe_Values.Look(ref ProtectColonyAnimals, "protectColonyAnimals");
            Scribe_Values.Look(ref IgnoreShieldedPawns, "ignoreShieldedPawns", true);
            Scribe_Values.Look(ref EnableWhenUndrafted, "enableWhenUndrafted");
            Scribe_Values.Look(ref EnableAccurateMissRadius, "enableAccurateMissRadius", true);
            Scribe_Values.Look(ref UseFarSideFilter, "useFarSideFilter");
            Scribe_Values.Look(ref MinCheckedDiskWidth, "minCheckedDiskWidth", 2);
        }
    }

    public class Main : Mod
    {
        internal static Main Instance { get; private set; }

        public PawnStatusTracker PawnStatusTracker { get; } = new PawnStatusTracker();

        private readonly Harmony _harmony;
        private readonly AvoidFriendlyFireSettings _settings;

        private ExtendedDataStorage _extendedDataStorage;
        private FireManager _fireManager;
        private FireConeOverlay _fireConeOverlay;
        private Map _lastSeenMap;

        public Main(ModContentPack content) : base(content)
        {
            Instance = this;
            _settings = GetSettings<AvoidFriendlyFireSettings>();
            _fireManager = new FireManager();

            _harmony = new Harmony("falconne.AvoidFriendlyFire");
            _harmony.PatchAll();
            TryPatchCombatExtended();
        }

        public override string SettingsCategory()
        {
            return "Avoid Friendly Fire";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled("FALCFF.EnableMod".Translate(), ref _settings.ModEnabled,
                "FALCFF.EnableModDesc".Translate());
            listing.CheckboxLabeled("FALCFF.ShowTargetingOverlay".Translate(), ref _settings.ShowOverlay,
                "FALCFF.ShowTargetingOverlayDesc".Translate());
            listing.CheckboxLabeled("FALCFF.ProtectPets".Translate(), ref _settings.ProtectPets,
                "FALCFF.ProtectPetsDesc".Translate());
            listing.CheckboxLabeled("FALCFF.ProtectColonyAnimals".Translate(), ref _settings.ProtectColonyAnimals,
                "FALCFF.ProtectColonyAnimalsDesc".Translate());
            listing.CheckboxLabeled("FALCFF.IgnoreShieldedPawns".Translate(), ref _settings.IgnoreShieldedPawns,
                "FALCFF.IgnoreShieldedPawnsDesc".Translate());
            listing.CheckboxLabeled("FALCFF.EnableWhenUndrafted".Translate(), ref _settings.EnableWhenUndrafted,
                "FALCFF.EnableWhenUndraftedDesc".Translate());
            listing.CheckboxLabeled("FALCFF.EnableAccurateMissRadius".Translate(),
                ref _settings.EnableAccurateMissRadius, "FALCFF.EnableAccurateMissRadiusDesc".Translate());
            listing.CheckboxLabeled("FALCFF.UseFarSideFilter".Translate(), ref _settings.UseFarSideFilter,
                "FALCFF.UseFarSideFilterDesc".Translate());

            listing.Label("FALCFF.MinCheckedDiskWidth".Translate() + $": {GetMinCheckedDiskWidth()}");
            listing.Label("FALCFF.MinCheckedDiskWidthDesc".Translate());
            listing.GapLine();
            var sliderRect = listing.GetRect(Text.LineHeight);
            _settings.MinCheckedDiskWidth = Mathf.RoundToInt(
                Widgets.HorizontalSlider(sliderRect, _settings.MinCheckedDiskWidth, 1, 20, false,
                    null, "1", "20"));

            listing.End();
        }

        public void OnWorldTick(int currentTick)
        {
            if (!IsModEnabled())
                return;

            GetFireManager().RemoveExpiredCones(currentTick);
            PawnStatusTracker.RemoveExpired();
            TrackMapChange();
        }

        private void TrackMapChange()
        {
            var map = Find.CurrentMap;
            if (map == _lastSeenMap)
                return;

            _lastSeenMap = map;
            if (map != null)
            {
                _fireManager = new FireManager();
                _fireConeOverlay = new FireConeOverlay();
                PawnStatusTracker.Reset();
            }
        }

        private void TryPatchCombatExtended()
        {
            try
            {
                var ceVerb = GenTypes.GetTypeInAnyAssembly("CombatExtended.Verb_LaunchProjectileCE");
                if (ceVerb == null)
                    return;

                Log.Message("[AvoidFriendlyFire] Patching CombatExtended methods");
                var vecType = GenTypes.GetTypeInAnyAssembly("Verse.IntVec3");
                var ltiType = GenTypes.GetTypeInAnyAssembly("Verse.LocalTargetInfo");

                var original = ceVerb.GetMethod("CanHitTargetFrom", new[] { vecType, ltiType });
                var postfix = typeof(Verb_CanHitTargetFrom_Patch).GetMethod("Postfix");
                _harmony.Patch(original, null, new HarmonyMethod(postfix));
            }
            catch (Exception e)
            {
                Log.Error($"[AvoidFriendlyFire] Exception while trying to detect CombatExtended: {e}");
            }
        }

        public void UpdateFireConeOverlay(bool enabled)
        {
            if (!IsModEnabled() || !_settings.ShowOverlay)
                return;

            if (Find.CurrentMap == null)
                return;

            if (_fireConeOverlay == null)
            {
                _fireConeOverlay = new FireConeOverlay();
            }
            _fireConeOverlay.Update(enabled);
        }

        public bool ShouldProtectPets()
        {
            return _settings.ProtectPets;
        }

        public bool ShouldProtectAllColonyAnimals()
        {
            return _settings.ProtectColonyAnimals;
        }

        public bool ShouldIgnoreShieldedPawns()
        {
            return _settings.IgnoreShieldedPawns;
        }

        public bool ShouldEnableWhenUndrafted()
        {
            return _settings.EnableWhenUndrafted;
        }

        public bool ShouldEnableAccurateMissRadius()
        {
            return _settings.EnableAccurateMissRadius;
        }

        public bool ShouldUseFarSideFilter()
        {
            return _settings.UseFarSideFilter;
        }

        public int GetMinCheckedDiskWidth()
        {
            int width = _settings.MinCheckedDiskWidth;
            if (width < 1) width = 1;
            if (width > 20) width = 20;
            return width;
        }

        public static Pawn GetSelectedPawn()
        {
            var selectedObjects = Find.Selector.SelectedObjects;
            if (selectedObjects == null || selectedObjects.Count != 1)
                return null;

            return selectedObjects.First() as Pawn;
        }

        public ExtendedDataStorage GetExtendedDataStorage()
        {
            if (_extendedDataStorage != null)
                return _extendedDataStorage;

            if (Find.World == null)
                return null;

            _extendedDataStorage = Find.World.GetComponent<ExtendedDataStorage>();
            return _extendedDataStorage;
        }

        public FireManager GetFireManager()
        {
            if (_fireManager == null)
            {
                _fireManager = new FireManager();
            }
            return _fireManager;
        }

        public bool IsModEnabled()
        {
            return _settings.ModEnabled;
        }
    }
}
