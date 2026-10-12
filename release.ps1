<#
.SYNOPSIS
    Builds, packs, signs and publishes a release to this fork's GitHub releases.

.DESCRIPTION
    1. Reads the version from AionDpsMeter.UI\AionDpsMeter.UI.csproj (bump AssemblyVersion first;
       the app compares major.minor.build, so bump at least the third number).
    2. Publishes a self-contained build with the updater pointed at UpdateRepository
       (AionDpsMeter.Services\AionDpsMeter.Services.csproj).
    3. Packs a clean zip (no settings, history or logs) with packaging\README.txt.
    4. Signs it with your private key. The key is never committed; default location
       %USERPROFILE%\.aion-dps-meter\release-signing-key.pem (override with -KeyPath or
       the AION_DPS_SIGNING_KEY environment variable).
    5. Creates GitHub release v<version> with the zip and its .sig via the gh CLI.

    Signing happens locally on purpose: a compromised GitHub account can't produce a
    release the app will accept without this key.

.EXAMPLE
    .\release.ps1 -DryRun
    .\release.ps1 -Notes "Adds boss time-to-kill estimate"
#>
param(
    [switch]$DryRun,
    [string]$Notes = '',
    [switch]$AllowDirty,
    [string]$KeyPath = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Fail([string]$message) {
    Write-Host "ERROR: $message" -ForegroundColor Red
    exit 1
}

function Assert-ExitCode([string]$what) {
    if ($LASTEXITCODE -ne 0) { Fail "$what failed (exit code $LASTEXITCODE)" }
}

# Native commands whose stderr/exit code we inspect ourselves.
function Invoke-Quiet([scriptblock]$command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $command 2>$null | Out-Null } finally { $ErrorActionPreference = $previous }
    return $LASTEXITCODE
}

if (-not $KeyPath) {
    $KeyPath = if ($env:AION_DPS_SIGNING_KEY) { $env:AION_DPS_SIGNING_KEY }
               else { Join-Path $env:USERPROFILE '.aion-dps-meter\release-signing-key.pem' }
}

# --- Read version and update repository -----------------------------------------------

$uiProject  = Join-Path $root 'AionDpsMeter.UI\AionDpsMeter.UI.csproj'
$svcProject = Join-Path $root 'AionDpsMeter.Services\AionDpsMeter.Services.csproj'

$version = ([xml](Get-Content $uiProject -Raw)).SelectSingleNode('//AssemblyVersion').InnerText
$repo    = ([xml](Get-Content $svcProject -Raw)).SelectSingleNode('//UpdateRepository').InnerText
if (-not $version) { Fail "AssemblyVersion not found in $uiProject" }
if (-not $repo -or $repo -like '*SHIFT_GITHUB_LOGIN*') { Fail "Set UpdateRepository in $svcProject to your GitHub owner/repo" }

$tag = "v$version"
Write-Host "Release $tag -> github.com/$repo" -ForegroundColor Cyan

# --- Pre-flight checks ------------------------------------------------------------------

if (-not (Test-Path $KeyPath)) { Fail "Signing key not found: $KeyPath" }

if (-not $AllowDirty) {
    $dirty = git -C $root status --porcelain
    if ($dirty) { Fail "Uncommitted changes. Commit them first, or pass -AllowDirty." }
}

if (-not $DryRun) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Fail "gh CLI not found (winget install GitHub.cli, then gh auth login)" }
    if ((Invoke-Quiet { gh release view $tag --repo $repo }) -eq 0) { Fail "Release $tag already exists. Bump AssemblyVersion in $uiProject." }
    if (-not $AllowDirty) {
        if ((Invoke-Quiet { git -C $root rev-parse '@{u}' }) -ne 0) { Fail "Current branch has no upstream. Push it first." }
        $ahead = git -C $root rev-list --count '@{u}..HEAD'
        if ([int]$ahead -gt 0) { Fail "$ahead local commit(s) not pushed. Push first so the release tag matches the code." }
    }
}

# --- Build ------------------------------------------------------------------------------

$out     = Join-Path $root "artifacts\release\$tag"
$publish = Join-Path $out 'publish'
$stage   = Join-Path $out 'Aion2DpsMeter'
$zip     = Join-Path $out "Aion2DpsMeter-$tag-win-x64.zip"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }

dotnet publish (Join-Path $root 'AionDpsMeter.UI\AionDpsMeter.UI.csproj') `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    "-p:UpdateRepository=$repo" -o $publish -nologo -v q -clp:ErrorsOnly
Assert-ExitCode 'dotnet publish'

if (-not (Test-Path (Join-Path $publish 'AionDpsMeter.Updater.exe'))) { Fail 'AionDpsMeter.Updater.exe missing from publish output' }

# --- Pack a clean folder ------------------------------------------------------------------

$skipFiles = @('appsettings.user.json', 'combat-history.db', 'combat-history.db-shm', 'combat-history.db-wal')
$skipTop   = @('Logs', 'IconCache', 'PacketLogs')

foreach ($file in Get-ChildItem $publish -Recurse -File) {
    $rel = $file.FullName.Substring($publish.Length + 1)
    if ($skipFiles -contains $file.Name) { continue }
    if ($skipTop -contains $rel.Split('\')[0]) { continue }
    if ($file.Extension -eq '.pdb' -or $file.Extension -eq '.xml') { continue }

    $dest = Join-Path $stage $rel
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    Copy-Item $file.FullName $dest
}
Copy-Item (Join-Path $root 'packaging\README.txt') (Join-Path $stage 'README.txt')

$signer = Join-Path $root 'tools\ReleaseSigner\ReleaseSigner.csproj'
dotnet run --project $signer -c Release -v q -- pack $stage $zip
Assert-ExitCode 'pack'

# --- Sign -----------------------------------------------------------------------------------

dotnet run --project $signer -c Release -v q -- sign $KeyPath $zip $tag
Assert-ExitCode 'sign'

$sizeMb = '{0:N1}' -f ((Get-Item $zip).Length / 1MB)
Write-Host "Built $zip ($sizeMb MB) and $zip.sig" -ForegroundColor Green

if ($DryRun) {
    Write-Host 'Dry run: not uploading.' -ForegroundColor Yellow
    exit 0
}

# --- Publish ------------------------------------------------------------------------------

if (-not $Notes) { $Notes = "Release $tag" }
gh release create $tag $zip "$zip.sig" --repo $repo --title $tag --notes $Notes
Assert-ExitCode 'gh release create'

Write-Host "Published https://github.com/$repo/releases/tag/$tag" -ForegroundColor Green
