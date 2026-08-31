[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPath,

    [string]$OutputPath = (Join-Path (Get-Location) "signing-metadata.json")
)

$ErrorActionPreference = "Stop"

$resolvedPackages = [Collections.Generic.List[System.IO.FileInfo]]::new()
foreach ($candidate in $PackagePath) {
    $matches = @(Resolve-Path -Path $candidate -ErrorAction Stop)
    foreach ($match in $matches) {
        $item = Get-Item -LiteralPath $match.Path
        if ($item.PSIsContainer) {
            foreach ($package in Get-ChildItem -LiteralPath $item.FullName -File) {
                if ($package.Name -match "-(full|delta)\.nupkg$") {
                    $resolvedPackages.Add($package)
                }
            }
        }
        elseif ($item.Name -match "-(full|delta)\.nupkg$") {
            $resolvedPackages.Add($item)
        }
        else {
            throw "Package must end in -full.nupkg or -delta.nupkg: $($item.FullName)"
        }
    }
}

$packages = @($resolvedPackages | Sort-Object FullName -Unique)
if ($packages.Count -eq 0) {
    throw "No Velopack *-full.nupkg or *-delta.nupkg files were found."
}
$duplicateNames = @(
    $packages |
        Group-Object Name |
        Where-Object Count -gt 1 |
        Select-Object -ExpandProperty Name
)
if ($duplicateNames.Count -gt 0) {
    throw "Package file names must be unique: $($duplicateNames -join ', ')"
}

$privateKeyFile = Get-Item -LiteralPath $PrivateKeyPath
if ($privateKeyFile.PSIsContainer) {
    throw "PrivateKeyPath must identify a PEM file."
}
$privateKey = Get-Content -LiteralPath $privateKeyFile.FullName -Raw
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem($privateKey)
    if ($rsa.KeySize -lt 2048) {
        throw "The release signing RSA key must be at least 2048 bits."
    }

    $subjectPublicKeyInfo = $rsa.ExportSubjectPublicKeyInfo()
    $fingerprint = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($subjectPublicKeyInfo)
    ).ToLowerInvariant()
    $artifacts = [ordered]@{}
    foreach ($package in $packages) {
        $sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $package.FullName).Hash.ToLowerInvariant()
        $signatureBytes = $rsa.SignData(
            [Text.Encoding]::UTF8.GetBytes($sha256),
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pss)
        $artifacts[$package.Name] = [ordered]@{
            sha256 = $sha256
            signature = [Convert]::ToBase64String($signatureBytes)
        }
    }
}
finally {
    $rsa.Dispose()
}

$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [IO.Path]::GetDirectoryName($outputFullPath)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}
$document = [ordered]@{
    schemaVersion = 1
    algorithm = "RSA-PSS-SHA256"
    fingerprint = $fingerprint
    artifacts = $artifacts
}
$json = $document | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText(
    $outputFullPath,
    $json,
    [Text.UTF8Encoding]::new($false))

[ordered]@{
    outputPath = $outputFullPath
    fingerprint = $fingerprint
    packageCount = $packages.Count
    packageNames = @($packages.Name)
} | ConvertTo-Json -Depth 3
