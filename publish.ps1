# Builds one GitHub release:
#   dist\uemm\UEModManager.exe                          the app, for every game
#   dist\release\uemm_<version>.zip                     its zip, ready to upload
#   dist\release\uemm_phantom-blade-zero_<version>.zip  the same zip under the name apps up to 1.7.2 look for
# The exe is a single file, framework dependent (needs the .NET 8 Desktop Runtime). The publish
# checks it (CheckPublishedExe in UEManager.UI.csproj).
param(
    [string]$Configuration = "Release",
    # Leaves out the copy under the old name. Use it once GitHub's download count of that copy stays near zero:
    # then no app up to 1.7.2 needs it any more. Cloudflare's copy keeps the old name either way (publish-app-copy.py).
    [switch]$NoOldName
)

$ErrorActionPreference = "Stop"
$dist = Join-Path $PSScriptRoot "dist"
$project = Join-Path $PSScriptRoot "src\UEManager.UI\UEManager.UI.csproj"
$version = ([xml](Get-Content (Join-Path $PSScriptRoot "src\Directory.Build.props"))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1

dotnet test (Join-Path $PSScriptRoot "src\UEManager.slnx") -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$release = Join-Path $dist "release"
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory -Force $release | Out-Null

$out = Join-Path $dist "uemm"
# The app keeps its data in a Data folder next to the exe when it is run from here: never delete that.
if (Test-Path $out) { Get-ChildItem $out -Exclude "Data" | Remove-Item -Recurse -Force }
dotnet publish $project -c $Configuration -o $out "-p:DebugType=none"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The zip name is part of the update check: the app takes the zip of a release by this name (AppInfo.ReleaseFileName).
$zip = Join-Path $release "uemm_$version.zip"
Compress-Archive -Path (Join-Path $out "UEModManager.exe") -DestinationPath $zip
# Apps up to 1.7.2 look for a name that starts with uemm_phantom-blade-zero_: the same bytes under that name.
if (-not $NoOldName) { Copy-Item $zip (Join-Path $release "uemm_phantom-blade-zero_$version.zip") }

Write-Host "Published to $dist; the exe passed its check."
Get-ChildItem $release | ForEach-Object { Write-Host "  $($_.Name)" }
