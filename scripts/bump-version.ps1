[CmdletBinding()]
param(
    [ValidateSet("patch", "minor", "major")]
    [string]$Type = "patch",
    [string]$SetVersion = ""
)

$manifestPath = Join-Path $PSScriptRoot "..\EurekaSuite.json"
$csprojPath = Join-Path $PSScriptRoot "..\EurekaSuite.csproj"

if (-not (Test-Path $manifestPath)) {
    Write-Error "Could not find EurekaSuite.json at $manifestPath"
    exit 1
}

# 1. Read current version from EurekaSuite.json
$jsonContent = Get-Content $manifestPath -Raw | ConvertFrom-Json
$currentVersion = $jsonContent.AssemblyVersion

if ([string]::IsNullOrWhiteSpace($currentVersion)) {
    $currentVersion = "1.0.0.0"
}

# Parse version (Major.Minor.Patch.Revision)
$parts = $currentVersion.Split('.')
[int]$major = if ($parts.Length -ge 1) { [int]$parts[0] } else { 1 }
[int]$minor = if ($parts.Length -ge 2) { [int]$parts[1] } else { 0 }
[int]$patch = if ($parts.Length -ge 3) { [int]$parts[2] } else { 0 }
[int]$rev   = if ($parts.Length -ge 4) { [int]$parts[3] } else { 0 }

# 2. Determine new version
if (-not [string]::IsNullOrWhiteSpace($SetVersion)) {
    $newVersion = $SetVersion
    if ($newVersion.Split('.').Length -eq 3) {
        $newVersion = "$newVersion.0"
    }
} else {
    switch ($Type) {
        "major" {
            $major++
            $minor = 0
            $patch = 0
            $rev = 0
        }
        "minor" {
            $minor++
            $patch = 0
            $rev = 0
        }
        "patch" {
            $patch++
            $rev = 0
        }
    }
    $newVersion = "$major.$minor.$patch.$rev"
}

Write-Host "Bumping version from $currentVersion to $newVersion..." -ForegroundColor Cyan

# 3. Update EurekaSuite.json
$jsonRaw = Get-Content $manifestPath -Raw
$jsonUpdated = $jsonRaw -replace '("AssemblyVersion"\s*:\s*")[^"]+(")', "`${1}$newVersion`${2}"
Set-Content -Path $manifestPath -Value $jsonUpdated -Encoding utf8

# 4. Update EurekaSuite.csproj
if (Test-Path $csprojPath) {
    $csprojContent = Get-Content $csprojPath -Raw
    if ($csprojContent -match '<AssemblyVersion>[^<]+</AssemblyVersion>') {
        $csprojContent = $csprojContent -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$newVersion</AssemblyVersion>"
        $csprojContent = $csprojContent -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$newVersion</FileVersion>"
        $csprojContent = $csprojContent -replace '<Version>[^<]+</Version>', "<Version>$newVersion</Version>"
    } else {
        # Inject inside first PropertyGroup
        $propGroupInsert = "`n    <Version>$newVersion</Version>`n    <AssemblyVersion>$newVersion</AssemblyVersion>`n    <FileVersion>$newVersion</FileVersion>"
        $csprojContent = $csprojContent -replace '(<PropertyGroup>)', "`$1$propGroupInsert"
    }
    Set-Content -Path $csprojPath -Value $csprojContent -Encoding utf8
}

Write-Host "Successfully bumped version to $newVersion!" -ForegroundColor Green
Write-Host "Tag: v$($newVersion.Substring(0, $newVersion.LastIndexOf('.'))) (or v$newVersion)"
