param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedExe = (Resolve-Path $ExePath).Path

Write-Host "== Signature Check =="
$sig = Get-AuthenticodeSignature $resolvedExe
$sig | Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate

if ($sig.Status -ne "Valid") {
    throw "Signature is not valid: $resolvedExe"
}

Write-Host ""
Write-Host "== Mark-of-the-Web (MOTW) Check =="
$streams = Get-Item -Path $resolvedExe -Stream * -ErrorAction SilentlyContinue
$motw = $streams | Where-Object { $_.Stream -eq "Zone.Identifier" }

if ($motw) {
    Write-Warning "Zone.Identifier stream exists. If this file was downloaded, run Unblock-File before local execution."
}
else {
    Write-Host "No Zone.Identifier stream found."
}

Write-Host ""
Write-Host "Verification completed: $resolvedExe"
