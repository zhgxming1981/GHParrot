# Repository Notes

## Coding Preferences

- 以后写代码、脚本、Lisp、配置或修改文件前，要先向用户提问：“现在可以写了吗？”，获得同意后才动手。
- 对于 AutoCAD Lisp 中“标注手改文字换算样式”的需求：如果手改文字不是纯数字（例如 `10%%c`、`L=10`、`10 个`），应直接报错并停止，不要继续计算比例。

- 每次修改代码前要向用户提问：“现在修改吗？”，获得同意后才动手修改代码。
- 本项目主要在 Visual Studio 环境中编码和阅读源码。源码中的中文字符串、中文注释、中文显示文本要直接写成中文，方便在 VS 中检查；不要为了 ASCII 兼容性改写成 `\uXXXX` 等转义形式。

- 回复用户时避免复述没有新增信息量的内容；例如用户已经确认过的参数顺序、行为规则、编译细节，除非用户主动要求说明，否则最终回复只保留改动结果、输出位置、错误或必要注意事项。
- 如果本轮只是按用户已确认的方案修改，最终回复不要再次罗列端口名称、端口顺序、边名判断规则、矩形判断规则、构建命令等已知信息；只说明已完成、是否通过构建、输出位置或需要用户注意的问题。

## Build Environment

- This solution contains .NET Framework projects with COM references, so `dotnet build parrot.sln` is not a valid build command here. It fails with `MSB4803` because .NET Core MSBuild does not support `ResolveComReference`.
- Use the .NET Framework version of MSBuild from Visual Studio when building this solution.
- In the current Codex shell environment, `git` is not available on `PATH`. Prefer the repository-bundled Git at:
  - `E:\程序\GH1 20260424\GH1\.tools\mingit\cmd\git.exe`
  - `E:\程序\GH1 20260424\GH1\.tools\mingit\mingw64\bin\git.exe`
- Visual Studio's bundled Git is also available at:
  - `D:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe`
  - `D:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\mingw64\bin\git.exe`
- When using Visual Studio's bundled Git for HTTPS remotes, set the Git root environment first so remote helpers are found:
  - `$gitRoot='D:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\mingw64'; $env:PATH="$gitRoot\bin;" + $env:PATH; $env:GIT_EXEC_PATH="$gitRoot\bin"; & "$gitRoot\bin\git.exe" status`
- Visual Studio MSBuild is available at:
  - `D:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe`
  - `D:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`
- Prefer the `amd64` Visual Studio MSBuild path above for this solution. The .NET SDK MSBuild at `C:\Program Files\dotnet\sdk\10.0.300\MSBuild.exe` exists, but it is not the preferred build tool for this COM-reference .NET Framework solution.
- 以后固定使用 VS MSBuild 的 `Debug|Any CPU` 构建输出：`E:\程序\GH1 20260424\GH1\输出\x64\Debug\net48\parrot.gha`。不要临时改到 `.codex-build`、`.buildcheck` 或其它输出目录；需要给 Grasshopper 加载时，以这个 `.gha` 为准。
