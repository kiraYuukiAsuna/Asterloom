[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateRange(1024, 65535)]
    [int]$Port = 5187
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $true
}
$runningOnWindows = if (Get-Variable -Name IsWindows -ErrorAction SilentlyContinue) {
    $IsWindows
}
else {
    $env:OS -eq "Windows_NT"
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$serverProject = Join-Path $repoRoot "Backend/Asterloom.Server/Asterloom.Server.csproj"
$openApiPath = Join-Path $repoRoot "Docs/Protocol/openapi/asterloom-v1.json"
$generatedClientPath = Join-Path $repoRoot "Frontend/lib/api/generated"
$baseUrl = "http://127.0.0.1:$Port"
$stdoutLog = New-TemporaryFile
$stderrLog = New-TemporaryFile
$serverProcess = $null
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT

try {
    & dotnet tool restore
    & dotnet build $serverProject --configuration $Configuration

    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $startParameters = @{
        FilePath = "dotnet"
        ArgumentList = @(
            "run",
            "--project", $serverProject,
            "--configuration", $Configuration,
            "--no-build",
            "--no-launch-profile",
            "--urls", $baseUrl
        )
        WorkingDirectory = $repoRoot
        PassThru = $true
        RedirectStandardOutput = $stdoutLog.FullName
        RedirectStandardError = $stderrLog.FullName
    }

    if ($runningOnWindows) {
        $startParameters.WindowStyle = "Hidden"
    }

    $serverProcess = Start-Process @startParameters
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    $ready = $false

    while ([DateTimeOffset]::UtcNow -lt $deadline -and -not $serverProcess.HasExited) {
        try {
            $healthResponse = Invoke-WebRequest "$baseUrl/health/ready" -TimeoutSec 2 -UseBasicParsing
            if ($healthResponse.StatusCode -eq 200) {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }

    if (-not $ready) {
        $serverOutput = Get-Content -LiteralPath $stdoutLog.FullName -Raw -ErrorAction SilentlyContinue
        $serverError = Get-Content -LiteralPath $stderrLog.FullName -Raw -ErrorAction SilentlyContinue
        throw "Asterloom.Server did not become ready.`n$serverOutput`n$serverError"
    }

    $swaggerResponse = Invoke-WebRequest "$baseUrl/swagger/v1/swagger.json" -TimeoutSec 15 -UseBasicParsing
    # Windows PowerShell 5.1 does not expose ConvertFrom-Json -Depth. Unlike
    # ConvertTo-Json, its parser still walks the complete input document.
    $openApiObject = $swaggerResponse.Content | ConvertFrom-Json
    $canonicalOpenApi = $openApiObject | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText(
        $openApiPath,
        $canonicalOpenApi + "`n",
        [System.Text.UTF8Encoding]::new($false))

    & dotnet kiota generate `
        --openapi $openApiPath `
        --language TypeScript `
        --output $generatedClientPath `
        --namespace-name "Asterloom.Api.Generated" `
        --class-name "AsterloomApiClient" `
        --clean-output `
        --exclude-backward-compatible `
        --log-level Warning

    Write-Host "Synchronized OpenAPI and Kiota client artifacts."
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment

    if ($null -ne $serverProcess -and -not $serverProcess.HasExited) {
        Stop-Process -Id $serverProcess.Id
        Wait-Process -Id $serverProcess.Id -ErrorAction SilentlyContinue
    }

    Remove-Item -LiteralPath $stdoutLog.FullName -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stderrLog.FullName -Force -ErrorAction SilentlyContinue
}
