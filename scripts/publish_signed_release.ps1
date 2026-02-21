param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$CertificatePath,

    [string]$CertificatePassword = $env:LSA_CERT_PASSWORD,
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$ProjectPath = "src/LSA.App/LSA.App.csproj",
    [string]$OutputRoot = "signed_releases"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Find-SignTool {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $kitsRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    if (Test-Path $kitsRoot) {
        $candidate = Get-ChildItem $kitsRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*\x64\signtool.exe" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($candidate) {
            return $candidate.FullName
        }
    }

    throw "signtool.exe not found. Install Windows SDK and retry."
}

function Invoke-SignFile {
    param(
        [Parameter(Mandatory = $true)] [string]$SignToolPath,
        [Parameter(Mandatory = $true)] [string]$CertPath,
        [Parameter(Mandatory = $true)] [string]$StampUrl,
        [Parameter(Mandatory = $true)] [string]$FilePath,
        [string]$CertPassword
    )

    $args = @(
        "sign",
        "/fd", "SHA256",
        "/td", "SHA256",
        "/tr", $StampUrl,
        "/f", $CertPath
    )

    if (-not [string]::IsNullOrWhiteSpace($CertPassword)) {
        $args += @("/p", $CertPassword)
    }

    $args += $FilePath

    & $SignToolPath @args | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed for: $FilePath"
    }
}

function Assert-ValidSignature {
    param(
        [Parameter(Mandatory = $true)] [string]$FilePath
    )

    $sig = Get-AuthenticodeSignature $FilePath
    if ($sig.Status -ne "Valid") {
        throw "Invalid signature: $FilePath ($($sig.Status) - $($sig.StatusMessage))"
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectFullPath = (Resolve-Path (Join-Path $repoRoot $ProjectPath)).Path
$certFullPath = (Resolve-Path $CertificatePath).Path
$signToolPath = Find-SignTool

$versionTag = if ($Version.StartsWith("v")) { $Version } else { "v$Version" }
$versionRoot = Join-Path (Join-Path $repoRoot $OutputRoot) $versionTag
$publishDir = Join-Path $versionRoot "publish"

if (Test-Path $versionRoot) {
    Remove-Item $versionRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "== Publish =="
$publishArgs = @(
    "publish", $projectFullPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", $publishDir
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$exePath = Join-Path $publishDir "LSA.exe"
if (-not (Test-Path $exePath)) {
    throw "Published LSA.exe not found: $exePath"
}

Write-Host "== Sign =="
Invoke-SignFile -SignToolPath $signToolPath -CertPath $certFullPath -CertPassword $CertificatePassword -StampUrl $TimestampUrl -FilePath $exePath
Assert-ValidSignature -FilePath $exePath

Write-Host "== Hashes =="
$hashFile = Join-Path $versionRoot "SHA256SUMS.txt"
Get-ChildItem $publishDir -File |
    Get-FileHash -Algorithm SHA256 |
    Sort-Object Path |
    ForEach-Object {
        "{0} *{1}" -f $_.Hash, (Split-Path $_.Path -Leaf)
    } |
    Set-Content -Path $hashFile -Encoding Ascii

Write-Host "== Archive =="
$zipPath = Join-Path $versionRoot ("LSA_{0}_signed.zip" -f $versionTag)
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

$sigInfo = Get-AuthenticodeSignature $exePath
$releaseInfo = [ordered]@{
    version = $versionTag
    builtAt = (Get-Date).ToString("O")
    runtime = $Runtime
    publishPath = $publishDir
    signedExe = $exePath
    zip = $zipPath
    signer = $sigInfo.SignerCertificate.Subject
    timestampServer = $TimestampUrl
}
$releaseInfo | ConvertTo-Json -Depth 3 | Set-Content -Path (Join-Path $versionRoot "release_info.json") -Encoding UTF8

Write-Host ""
Write-Host "Signed release completed."
Write-Host " - EXE : $exePath"
Write-Host " - ZIP : $zipPath"
Write-Host " - HASH: $hashFile"
