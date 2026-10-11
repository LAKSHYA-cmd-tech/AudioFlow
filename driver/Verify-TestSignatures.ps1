param([ValidateSet('x64', 'ARM64')][string[]] $Platforms = @('x64', 'ARM64'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$publicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
    (Join-Path $PSScriptRoot 'package\AudioFlowOutput-Test.cer'))
try {
    foreach ($platform in $Platforms) {
        $catalog = Join-Path $PSScriptRoot "package\$platform\AudioFlowOutput\audioflowoutput.cat"
        $signed = [System.Security.Cryptography.Pkcs.SignedCms]::new()
        $signed.Decode([IO.File]::ReadAllBytes($catalog))
        # Validate the signature mathematically. The test root is trusted inside the VM,
        # not installed into this workstation's Root store by the build workflow.
        $signed.CheckSignature($true)
        if ($signed.SignerInfos.Count -ne 1 -or $signed.SignerInfos[0].Certificate.Thumbprint -ne $publicCertificate.Thumbprint) {
            throw "$platform catalog does not match the public VM certificate."
        }
        Write-Host "$platform catalog signature is valid and matches the public VM certificate."
    }
} finally { $publicCertificate.Dispose() }
