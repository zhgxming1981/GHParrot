# Repository Notes

## Build Environment

- This solution contains .NET Framework projects with COM references, so `dotnet build parrot.sln` is not a valid build command here. It fails with `MSB4803` because .NET Core MSBuild does not support `ResolveComReference`.
- Use the .NET Framework version of MSBuild from Visual Studio when building this solution.
- In the current Codex shell environment, `git` is not available on `PATH`, and `msbuild.exe` was not found on `PATH` or under the usual Visual Studio install directories checked at:
  - `%ProgramFiles%\Microsoft Visual Studio`
  - `%ProgramFiles(x86)%\Microsoft Visual Studio`
- Do not spend time re-running the same discovery unless the user says the local build tools changed.
