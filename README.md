To build, build 1.6.csproj in VS 2022. The project is designed to be able to worked on and built inside the RimWorld Mods folder.

Before a release, update `AssemblyInfo.cs` in the project directory to increase the patch number (3rd version field) of `AssemblyVersion` and `AssemblyFileVersion` by one. 
The major & minor version fields of `AssemblyInfo.cs` and the `targetVersion` field of `mod-structure\About\About.xml` must be updated manually.

Rimworld references as well as Harmony and HugsLib dependencies are fetched via NuGet.