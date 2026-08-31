[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$PackageId = "Asterloom.ReferenceApp",
    [string]$Channel = "stable",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$BaselineVersion = "1.0.0",
    [string]$TargetVersion = "1.1.0"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "../.."))
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "OutputDirectory must not already exist: $outputRoot"
}

$baselinePublish = Join-Path $outputRoot "publish-$BaselineVersion"
$targetPublish = Join-Path $outputRoot "publish-$TargetVersion"
$releases = Join-Path $outputRoot "releases"
New-Item -ItemType Directory -Path $baselinePublish, $targetPublish, $releases | Out-Null

$project = Join-Path $repositoryRoot `
    "Backend/Samples/Asterloom.ReferenceApp.Client/Asterloom.ReferenceApp.Client.csproj"
$mainExecutable = "Asterloom.ReferenceApp.Client.exe"
$setupName = "$PackageId-$Channel-Setup.exe"
$baselineSetupName = "$PackageId-$Channel-Setup-$BaselineVersion.exe"

Push-Location $repositoryRoot
try {
    dotnet tool restore
    dotnet publish $project `
        --configuration Release `
        --runtime $RuntimeIdentifier `
        --self-contained false `
        -p:Version=$BaselineVersion `
        -p:InformationalVersion=$BaselineVersion `
        --output $baselinePublish
    dotnet vpk pack `
        --packId $PackageId `
        --packVersion $BaselineVersion `
        --packDir $baselinePublish `
        --mainExe $mainExecutable `
        --channel $Channel `
        --runtime $RuntimeIdentifier `
        --outputDir $releases `
        --shortcuts None `
        --framework net10-x64 `
        --yes
    Copy-Item `
        -LiteralPath (Join-Path $releases $setupName) `
        -Destination (Join-Path $releases $baselineSetupName)

    dotnet publish $project `
        --configuration Release `
        --runtime $RuntimeIdentifier `
        --self-contained false `
        -p:Version=$TargetVersion `
        -p:InformationalVersion=$TargetVersion `
        --output $targetPublish
    dotnet vpk pack `
        --packId $PackageId `
        --packVersion $TargetVersion `
        --packDir $targetPublish `
        --mainExe $mainExecutable `
        --channel $Channel `
        --runtime $RuntimeIdentifier `
        --outputDir $releases `
        --shortcuts None `
        --framework net10-x64 `
        --yes
}
finally {
    Pop-Location
}

$baselineFull = Get-Item -LiteralPath (
    Join-Path $releases "$PackageId-$BaselineVersion-$Channel-full.nupkg")
$targetFull = Get-Item -LiteralPath (
    Join-Path $releases "$PackageId-$TargetVersion-$Channel-full.nupkg")
$targetDelta = Get-Item -LiteralPath (
    Join-Path $releases "$PackageId-$TargetVersion-$Channel-delta.nupkg")
$baselineSetup = Get-Item -LiteralPath (Join-Path $releases $baselineSetupName)
$reconstructed = Join-Path $outputRoot "reconstructed-$TargetVersion.nupkg"
Push-Location $repositoryRoot
try {
    dotnet vpk delta patch `
        --base $baselineFull.FullName `
        --patch $targetDelta.FullName `
        --output $reconstructed
}
finally {
    Pop-Location
}
$targetHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $targetFull.FullName).Hash
$reconstructedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $reconstructed).Hash
if ($targetHash -ne $reconstructedHash) {
    throw "The generated Delta did not reconstruct the target Full package byte-for-byte."
}
Remove-Item -LiteralPath $reconstructed

[ordered]@{
    packageId = $PackageId
    channel = $Channel
    runtimeIdentifier = $RuntimeIdentifier
    baselineVersion = $BaselineVersion
    targetVersion = $TargetVersion
    baselineFull = $baselineFull.FullName
    targetFull = $targetFull.FullName
    targetDelta = $targetDelta.FullName
    baselineSetup = $baselineSetup.FullName
    fullBytes = $targetFull.Length
    deltaBytes = $targetDelta.Length
    deltaRatio = [Math]::Round($targetDelta.Length / $targetFull.Length, 6)
    reconstructedSha256 = $reconstructedHash
} | ConvertTo-Json
