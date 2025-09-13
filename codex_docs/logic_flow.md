Here’s a high-level “logic flow” of how the AvoidFriendlyFire mod works, broken down by feature area and showing how the pieces fit together:

Mod Initialization
• Main (HugsLib.ModBase)
  • DefsLoaded()
    – Read all user settings (enable/disable mod, overlay, pets/animals, shields, undrafted behavior, accurate miss radius)
    – Try to detect CombatExtended and patch its CanHitTargetFrom if present
  • WorldLoaded()
    – Grab (or create) the WorldComponent ExtendedDataStorage
    – Instantiate a new FireManager
  • MapLoaded()
    – Instantiate a fresh FireManager and FireConeOverlay for the current map
    – Reset the PawnStatusTracker
  • Tick(int)
    – If mod enabled:
      * FireManager.RemoveExpiredCones(currentTick)
      * PawnStatusTracker.RemoveExpired()

Per-Pawn Settings & Storage
• ExtendedDataStorage (WorldComponent)
  – Holds a map pawnID → ExtendedPawnData (bool AvoidFriendlyFire)
  – ExposeData() for save/load
  – CanTrackPawn(pawn) = pawn ∈ player faction
  – ShouldPawnAvoidFriendlyFire(pawn) = tracked AND AvoidFriendlyFire ON AND has valid ranged weapon
  – DeleteExtendedDataFor on pawn death

• ExtendedPawnData
  – single field: AvoidFriendlyFire (default true)

Gizmo (Button) in Draft Panel
• Pawn_DraftController.GetGizmos postfix
  – If mod enabled, map present, pawn ∈ player, has ranged projectile weapon → append a Command_Toggle “Avoid Friendly Fire”
  – Reads/writes ExtendedPawnData.AvoidFriendlyFire

Auto-enable on Undraft
• Pawn_DraftController.set_Drafted postfix
  – When pawn is undrafted (if “enableWhenUndrafted”), automatically turn AvoidFriendlyFire back on

Cleaning Up on Death
• Pawn.Kill postfix
  – Remove any PawnStatus entries for that pawn (shooter or blocker)
  – Delete its ExtendedPawnData entry

Blocking & Status Tracking
• PawnStatusTracker
  – Holds a short‐lived list of “blocked shot” events (shooter, blocker, expires in 20 ticks)
  – AddBlockedShooter(shooter, blocker) or Refresh existing
  – RemoveExpired runs every tick from Main.Tick
  – KillOff(pawn) clears any entries involving pawn
  – IsAShooter / IsABlocker query for UI coloring

• PawnStatus
  – Pair (Shooter, Blocker), expires 20 ticks after last block

Shot-Validation (Core Logic)
• Harmony patches on:
  – AttackTargetFinder.BestAttackTarget (sets custom validator)
  – Verb.CanHitTargetFrom (core RimWorld)
  – CombatExtended.Verb_LaunchProjectileCE.CanHitTargetFrom (if CE detected)

• FireManager.CanHitTargetSafely(FireProperties)

    1. If SkipNextCheck flag (set by TooltipUtility), consume it and return true (bypass check)
    2. Remove any old “blocked” status for this caster in PawnStatusTracker
    3. Build or fetch a CachedFireCone for (origin,target)
    4. For each pawn on map:
       – Skip dead, no‐faction, hostile or prisoner humans; skip animals if settings say so
       – Skip origin/target cells, skip cells not in the fire‐cone; skip pawns with active shields if “ignoreShieldedPawns”
       – On first friendly in cone: record PawnStatusTracker.AddBlockedShooter(caster, blocker) and return false
    5. If no blockers found → return true

• CachedFireCone
  – Wraps a HashSet<int> of cell-indices in the cone, with a 2000-tick expiry and “Prolong” on reuse
  – FireManager cleans expired cones every ~400 ticks

Fire-Cone Computation
• FireCalculations.GetFireCone(FireProperties) → HashSet<int> of map-cell indices

    1. Validate origin/target visibility & bounds; call AdjustForLeaning() to handle corner peeks
    2. Get MissAreaDescriptor (either forced miss radius, accurate radius from ShotReport curves, or simple 8-cell adjacent)
    3. For each offset in the miss-area:
       – Compute a “splashTarget” = target + offset
       – Trace a line of sight from origin to splashTarget via GenSight.PointsOnLineOfSight
        * Stop if blocked by impassable cover

        * Skip points “too close” to the shooter

        * Collect every reachable cell index
             – If splashTarget isn’t adjacent, include splashTarget itself

• FireProperties
  – Bundles caster pawn, origin, target, verb, map
  – ArePointsVisibleAndValid(), AdjustForLeaning(), GetAimOnTargetChance(), GetMissAreaDescriptor()

UI Overlays & Coloring
• FireConeOverlay (ICellBoolGiver)
  – When targeting with a valid non-explosive ranged weapon:
    * On each mouse move, rebuild current fire-cone for (selectedPawn, mouseCell)
    * CellBoolDrawer draws red overlay over those cells

• TooltipUtility.ShotCalculationTipString prefix
  – When hovering a shot tooltip on a selected pawn, set FireManager.SkipNextCheck = true so the tooltip calculation itself isn’t blocked

• PawnNameColorUtility.PawnNameColorOf postfix
  – If pawn ∈ PawnStatusTracker.IsAShooter → color cyan
  – If pawn ∈ PawnStatusTracker.IsABlocker → color green

• PawnUIOverlay.DrawPawnGUIOverlay prefix
  – For animals (non-humanlike) that are currently blockers → draw their label and skip the default overlay

Resources
• FriendlyFireIcon loaded once via ContentFinder<Texture2D>.Get(“AvoidFF”)

—
Keep this schema handy as you dive into any part of the mod. It shows where data lives, who talks to whom, and the main hot-paths for avoiding friendly-fire.