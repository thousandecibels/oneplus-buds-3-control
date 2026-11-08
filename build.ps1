$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework 4.x compiler is required.'
}

$output = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File
$exe = Join-Path $output 'Buds3Control.exe'
$layout = Join-Path $PSScriptRoot 'src\ControlWindow.xaml'
$wpf = Join-Path (Split-Path -Parent $compiler) 'WPF'
$references = @('PresentationCore', 'PresentationFramework', 'WindowsBase', 'WindowsFormsIntegration') |
    ForEach-Object { "/reference:$(Join-Path $wpf "$_.dll")" }

& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ "/out:$exe" `
    "/resource:$layout,ControlWindow.xaml" /reference:System.Xaml.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll $references $sources.FullName
if ($LASTEXITCODE -ne 0) {
    throw 'Build failed.'
}

Write-Output $exe
