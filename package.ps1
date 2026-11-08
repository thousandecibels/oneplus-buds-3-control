param(
    [string] $InnoSetupCompiler,
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'

if (-not $InnoSetupCompiler) {
    $command = Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($command) {
        $InnoSetupCompiler = $command.Source
    } else {
        $programDirectories = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, (Join-Path $env:LOCALAPPDATA 'Programs'))
        $InnoSetupCompiler = $programDirectories | Where-Object { $_ } | ForEach-Object {
            Join-Path $_ 'Inno Setup 7 (x64)\ISCC.exe'
            Join-Path $_ 'Inno Setup 7\ISCC.exe'
        } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if (-not $InnoSetupCompiler -or -not (Test-Path -LiteralPath $InnoSetupCompiler -PathType Leaf)) {
    throw 'Install Inno Setup 7 or pass the path to ISCC.exe with -InnoSetupCompiler.'
}
$InnoSetupCompiler = (Resolve-Path -LiteralPath $InnoSetupCompiler).Path

$output = Join-Path $PSScriptRoot 'dist\release'
$portable = & (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $output
$setup = Join-Path $PSScriptRoot 'installer\Buds3Control.iss'

& $InnoSetupCompiler /Qp "/DAppVersion=$Version" $setup
if ($LASTEXITCODE -ne 0) {
    throw 'Installer build failed.'
}

Write-Output $portable
Write-Output (Join-Path $output 'Buds3Control-Setup.exe')
