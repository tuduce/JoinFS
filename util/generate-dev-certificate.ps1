<#
.SYNOPSIS
    Generates a Self-Signed Code Signing Certificate and exports it to PFX.
    Safe for open-source: No hardcoded passwords.

.DESCRIPTION
    1. Prompts the user securely for a password.
    2. Generates a certificate in the current user's store.
    3. Exports the certificate to a .pfx file in the specified directory.
    
.PARAMETER CertName
    The name (CN) of the certificate. Default is "MyProjectDevCert".
    
.PARAMETER OutputDir
    The folder where the PFX will be saved. Default is the current directory.
#>

[CmdletBinding()]
param (
    [string]$CertName = "JoinFS-MSFS2024",
    [string]$OutputDir = ".."
)

Process {
    # 1. Input Validation & Setup
    if (-not (Test-Path -Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir | Out-Null
        Write-Host "Created directory: $OutputDir" -ForegroundColor Cyan
    }

    $OutputPath = Join-Path -Path $OutputDir -ChildPath "$CertName.pfx"

    # 2. Securely Prompt for Password
    # This ensures the password is never saved in command history or the script file
    Write-Host "Please create a password for the PFX file." -ForegroundColor Yellow
    $SecurePassword = Read-Host -Prompt "Enter Password (input will be hidden)" -AsSecureString

    if (-not $SecurePassword) {
        Write-Error "Password cannot be empty."
        return
    }

    try {
        # 3. Generate the Certificate in the Windows Cert Store
        Write-Host "Generating Self-Signed Certificate '$CertName'..." -ForegroundColor Cyan
        
        $cert = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject "CN=$CertName" `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -Provider "Microsoft Enhanced RSA and AES Cryptographic Provider" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -NotAfter (Get-Date).AddYears(5)

        # 4. Export to PFX
        Write-Host "Exporting to $OutputPath..." -ForegroundColor Cyan
        
        Export-PfxCertificate `
            -Cert $cert `
            -FilePath $OutputPath `
            -Password $SecurePassword

        Write-Host "---------------------------------------------------" -ForegroundColor Green
        Write-Host "SUCCESS!" -ForegroundColor Green
        Write-Host "Certificate saved to: $OutputPath"
        Write-Host "Don't forget to add *.pfx to your .gitignore file!" -ForegroundColor Yellow
        Write-Host "---------------------------------------------------" -ForegroundColor Green

    }
    catch {
        Write-Error "An error occurred: $_"
    }
}