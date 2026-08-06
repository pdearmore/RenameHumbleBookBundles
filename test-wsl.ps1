[CmdletBinding()]
param(
    [string]$Distribution = "Ubuntu-24.04",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) {
    throw "WSL is not installed. Install a Linux distribution, then run this script again."
}

$installed = @(wsl.exe --list --quiet | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Distribution -notin $installed) {
    throw "WSL distribution '$Distribution' was not found. Available distributions: $($installed -join ', ')"
}

$project = Join-Path $PSScriptRoot "src\HumbleRename\HumbleRename.csproj"
$publish = Join-Path $PSScriptRoot "dist\wsl-linux-x64"
$binary = Join-Path $publish "HumbleRenamer"

if (-not $SkipPublish) {
    Write-Host "Publishing the self-contained Linux binary..."
    dotnet publish $project -c Release -r linux-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none `
        -o $publish --nologo
}

if (-not (Test-Path -LiteralPath $binary)) {
    throw "Linux binary not found at $binary. Run without -SkipPublish first."
}

# WSL's command-line bridge interprets backslashes before `wslpath` receives them, so
# convert the absolute Windows path directly to the standard /mnt/<drive>/ form.
$drive = $binary.Substring(0, 1).ToLowerInvariant()
$linuxBinary = "/mnt/$drive$($binary.Substring(2).Replace('\', '/'))"
$smokeTest = @'
set -euo pipefail
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
cp '__BINARY__' "$stage/HumbleRenamer"
chmod +x "$stage/HumbleRenamer"
"$stage/HumbleRenamer" --version
"$stage/HumbleRenamer" --help >/dev/null
echo "Linux smoke test passed."
'@
$smokeTest = $smokeTest.Replace('__BINARY__', $linuxBinary)

Write-Host "Running Humble Renamer inside WSL ($Distribution)..."
# Send the script over standard input so PowerShell and WSL do not reinterpret Bash's `$` variables.
$smokeTest | & wsl.exe -d $Distribution -- bash -s
if ($LASTEXITCODE -ne 0) {
    throw "The Linux smoke test failed."
}
