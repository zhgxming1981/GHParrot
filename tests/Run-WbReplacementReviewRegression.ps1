$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo '输出/x64/Debug/net48'
[xml]$config = Get-Content -Encoding UTF8 -Raw (Join-Path $repo 'Directory.Build.props.user')
$rhino = [string]$config.Project.PropertyGroup.Rhino8Dir
$resolve = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    foreach ($folder in @($output, (Join-Path $rhino 'System'), (Join-Path $rhino 'Plug-ins/Grasshopper'))) {
        $candidate = Join-Path $folder $name
        if (Test-Path -LiteralPath $candidate) { return [Reflection.Assembly]::LoadFrom($candidate) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolve)
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('wb-replacement-review-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    $sqlite = Join-Path $output 'System.Data.SQLite.dll'
    [Reflection.Assembly]::LoadFrom($sqlite) | Out-Null
    Add-Type -TypeDefinition (Get-Content -Encoding UTF8 -Raw (Join-Path $PSScriptRoot 'WbReplacementReviewRegression.cs')) -ReferencedAssemblies @('System.Core', 'System.Data', $sqlite)
    [WbReplacementReviewRegression]::Run((Join-Path $output 'parrot.gha'), $testDirectory)
}
finally {
    [System.Data.SQLite.SQLiteConnection]::ClearAllPools()
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolve)
    # 仅清理本次创建目录内的测试库，不递归删除目录。
    foreach ($name in @('replacement.db', 'abnormal.db')) {
        $testFile = Join-Path $testDirectory $name
        if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile }
    }
    [IO.Directory]::Delete($testDirectory, $false)
}
