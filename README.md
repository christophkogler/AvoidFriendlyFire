# AvoidFriendlyFire

 A RimWorld mod to drastically reduce accidental friendly fire, by preventing pawns from firing if they might hit friendlies.

## Features

- When a pawn is selected, you are able to visualize their accuracy / miss radius.
- Pawns will not shoot while friendly pawns are within their miss radius.
- Turrets and mechs can optionally avoid friendly fire too.
- Highlight which pawns are obstructing shots, and which are being obstructed.
- Supports RimWorld v1.6; depends on Harmony.

## Installation

### Option 1: Steam Workshop

1. Subscribe to AvoidFriendlyFire on Steam Workshop:  
   https://steamcommunity.com/sharedfiles/filedetails/?id=3532871145

### Option 2: Install from Releases

1. Download the latest release `.zip` from GitHub Releases.
2. Extract the mod folder from the `.zip` into your `RimWorld/Mods` directory.
3. Launch RimWorld and enable the mod in the Mods menu.

### Option 3: Build from Source

1. Clone this repository.  
2. Extract or copy the `AvoidFriendlyFire` folder into your `RimWorld/Mods` directory.  
3. Build the mod.
4. Launch RimWorld and enable the mod in the Mods menu.

## Building from Source

### Prerequisites

- Visual Studio 2022  
- .NET Framework (as required by RimWorld)  
- NuGet for package dependencies (RimWorld references, Harmony)

### Steps

1. (Optional) Clone this repository into your `RimWorld/Mods` directory.  
2. Open `src/AvoidFriendlyFire/AvoidFriendlyFire.sln` in Visual Studio 2022.  
3. Build `1.6.csproj` to produce `AvoidFriendlyFire.dll` and `AvoidFriendlyFire.pdb`.
4. Copy the output into `1.6/Assemblies` within the mod folder.

## License

This project is licensed under the [GPLv3](LICENSE.txt).
