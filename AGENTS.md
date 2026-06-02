# Repository Notes

## Coding Preferences

- 每次修改代码前要向用户提问：“现在修改吗？”，获得同意后才动手修改代码。
- 本项目主要在 Visual Studio 环境中编码和阅读源码。源码中的中文字符串、中文注释、中文显示文本要直接写成中文，方便在 VS 中检查；不要为了 ASCII 兼容性改写成 `\uXXXX` 等转义形式。

## Build Environment

- This solution contains .NET Framework projects with COM references, so `dotnet build parrot.sln` is not a valid build command here. It fails with `MSB4803` because .NET Core MSBuild does not support `ResolveComReference`.
- Use the .NET Framework version of MSBuild from Visual Studio when building this solution.
- In the current Codex shell environment, `git` is not available on `PATH`, but Visual Studio's bundled Git is available at:
  - `D:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\mingw64\bin\git.exe`
- When using that Git for HTTPS remotes, set the Git root environment first so remote helpers are found:
  - `$gitRoot='D:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\mingw64'; $env:PATH="$gitRoot\bin;" + $env:PATH; $env:GIT_EXEC_PATH="$gitRoot\bin"; & "$gitRoot\bin\git.exe" status`
- `msbuild.exe` was not found on `PATH` or under the usual Visual Studio install directories checked at:
  - `%ProgramFiles%\Microsoft Visual Studio`
  - `%ProgramFiles(x86)%\Microsoft Visual Studio`
- Do not spend time re-running the same discovery unless the user says the local build tools changed.
