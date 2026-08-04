<#
.SYNOPSIS
    Builds hbrename.exe as a single self-contained Windows executable.

.DESCRIPTION
    Produces one .exe with the .NET runtime, the word corpus and the lexicon all
    packed inside, so it runs on a machine with no .NET installed. The result is
    written to .\publish\hbrename.exe.

.PARAMETER Runtime
    Target runtime identifier. Defaults to win-x64; use win-arm64 for ARM devices.

.PARAMETER SkipTests
    Skip the test run.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Runtime win-arm64 -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\HumbleRename\HumbleRename.csproj'
$output = Join-Path $root 'publish'

Write-Host 'Restoring...' -ForegroundColor Cyan
dotnet restore (Join-Path $root 'HumbleRename.slnx')
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }

if (-not $SkipTests) {
    Write-Host 'Testing...' -ForegroundColor Cyan
    dotnet test (Join-Path $root 'tests\HumbleRename.Tests\HumbleRename.Tests.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

Write-Host "Publishing single-file exe ($Runtime)..." -ForegroundColor Cyan
dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $output `
    --nologo

if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$exe = Join-Path $output 'hbrename.exe'
if (Test-Path $exe) {
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host ''
    Write-Host "Built $exe ($size MB)" -ForegroundColor Green
    Write-Host 'Try:  .\publish\hbrename.exe "D:\Comics\Humble Bundle" --dry-run' -ForegroundColor DarkGray
}
else {
    throw "Expected $exe but it was not produced."
}
