# Trusted Release Guide

This project can be built as a signed Windows release to reduce SmartScreen warnings.

## 1) Required

- Windows code-signing certificate (PFX)
- Password for the certificate
- Windows SDK (includes `signtool.exe`)
- .NET 8 SDK

## 2) Create Signed Release

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish_signed_release.ps1 `
  -Version 0.4.3 `
  -CertificatePath "C:\cert\your_codesign.pfx" `
  -CertificatePassword "YOUR_PASSWORD"
```

Output:

- `signed_releases\v0.4.3\publish\LSA.exe` (signed)
- `signed_releases\v0.4.3\LSA_v0.4.3_signed.zip`
- `signed_releases\v0.4.3\SHA256SUMS.txt`
- `signed_releases\v0.4.3\release_info.json`

## 3) Verify

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify_signed_release.ps1 `
  -ExePath ".\signed_releases\v0.4.3\publish\LSA.exe"
```

## 4) Real-World Notes

- The strongest path for SmartScreen trust is an **EV code-signing certificate**.
- OV certificates can still show warnings early until reputation builds up.
- Managed environments (PC cafes, enterprise PCs) may enforce additional app control policies.
