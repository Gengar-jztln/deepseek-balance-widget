# Build script: uses the C# compiler shipped with .NET Framework (no extra dependencies).
# NOTE: keep this file ASCII-only, because Windows PowerShell reads .ps1 as ANSI.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root 'DeepSeekPet.exe'

$candidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
    throw 'csc.exe not found. Please install .NET Framework 4.x.'
}

$sources = Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName }
if (-not $sources) { throw 'No *.cs files found under src.' }

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    '/platform:anycpu',
    '/codepage:65001',
    ('/out:' + $out),
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll'
) + $sources

& $csc $arguments
if ($LASTEXITCODE -ne 0) { throw ('Compilation failed, exit code ' + $LASTEXITCODE) }

Write-Host ('Build succeeded: ' + $out) -ForegroundColor Green
