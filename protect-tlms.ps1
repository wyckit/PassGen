#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Encrypt or decrypt the compiled TLM data (.tlmz) at rest with a passphrase.

.DESCRIPTION
    Authenticated encryption: PBKDF2-SHA256 (200k iterations) derives a 256-bit AES
    key and a separate HMAC key from your passphrase + a random per-file salt. Each
    file becomes  MAGIC | salt | iv | HMAC-SHA256 | AES-256-CBC ciphertext  (.tlmz.enc),
    and the plaintext .tlmz is removed. Decrypt verifies the HMAC (rejects a wrong
    passphrase or any tampering) before restoring the .tlmz.

    The sage app needs the plaintext .tlmz to run, so: decrypt -> run -> (optionally)
    encrypt again. Works in Windows PowerShell 5.1 and PowerShell 7+.

.PARAMETER Mode
    encrypt  (.tlmz -> .tlmz.enc, removes plaintext)
    decrypt  (.tlmz.enc -> .tlmz, removes ciphertext)

.PARAMETER Path
    Folder of TLM files. Default: <script dir>\dataset\compiled

.PARAMETER Password
    Passphrase (omit to be prompted securely).

.EXAMPLE
    .\protect-tlms.ps1 -Mode encrypt
    .\protect-tlms.ps1 -Mode decrypt
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true)][ValidateSet('encrypt', 'decrypt')][string]$Mode,
    [string]$Path,
    [string]$Password
)

$ErrorActionPreference = 'Stop'
if (-not $Path) { $Path = Join-Path $PSScriptRoot 'dataset\compiled' }
if (-not (Test-Path $Path)) { throw "Path not found: $Path" }

# ---- passphrase ----
if (-not $Password) {
    $sec = Read-Host -AsSecureString "Passphrase"
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    try { $Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}
if ([string]::IsNullOrEmpty($Password)) { throw "Empty passphrase." }
$pwBytes = [Text.Encoding]::UTF8.GetBytes($Password)

$MAGIC = [byte[]](0x54, 0x4C, 0x45, 0x4E)   # "TLEN"
$ITER = 200000

function New-RandomBytes([int]$n) {
    $b = New-Object byte[] $n
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($b) } finally { $rng.Dispose() }
    , $b
}
function Get-Keys([byte[]]$pw, [byte[]]$salt) {
    $kdf = [Security.Cryptography.Rfc2898DeriveBytes]::new($pw, $salt, $ITER, [Security.Cryptography.HashAlgorithmName]::SHA256)
    try { $k = $kdf.GetBytes(64) } finally { $kdf.Dispose() }
    @{ Aes = [byte[]]$k[0..31]; Mac = [byte[]]$k[32..63] }
}
function Test-CtEqual([byte[]]$a, [byte[]]$b) {
    if ($a.Length -ne $b.Length) { return $false }
    $d = 0; for ($i = 0; $i -lt $a.Length; $i++) { $d = $d -bor ($a[$i] -bxor $b[$i]) }
    return ($d -eq 0)
}

$encrypting = ($Mode -eq 'encrypt')
$pattern = if ($encrypting) { '*.tlmz' } else { '*.tlmz.enc' }
$files = Get-ChildItem -Path $Path -Filter $pattern -File
if (-not $files) { Write-Host "No $pattern files in $Path"; return }

foreach ($f in $files) {
    $data = [IO.File]::ReadAllBytes($f.FullName)
    if ($encrypting) {
        $salt = New-RandomBytes 16
        $iv = New-RandomBytes 16
        $keys = Get-Keys $pwBytes $salt
        $aes = [Security.Cryptography.Aes]::Create()
        $aes.KeySize = 256; $aes.Key = $keys.Aes; $aes.IV = $iv
        $aes.Mode = [Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
        try { $ct = $aes.CreateEncryptor().TransformFinalBlock($data, 0, $data.Length) } finally { $aes.Dispose() }
        $hmac = [Security.Cryptography.HMACSHA256]::new($keys.Mac)
        try { $mac = $hmac.ComputeHash([byte[]]($salt + $iv + $ct)) } finally { $hmac.Dispose() }
        [IO.File]::WriteAllBytes("$($f.FullName).enc", [byte[]]($MAGIC + $salt + $iv + $mac + $ct))
        Remove-Item $f.FullName -Force
        Write-Host "encrypted  $($f.Name) -> $($f.Name).enc"
    }
    else {
        if ($data.Length -lt 68 -or -not (Test-CtEqual ([byte[]]$data[0..3]) $MAGIC)) {
            Write-Warning "skip $($f.Name): not a TLEN file"; continue
        }
        $salt = [byte[]]$data[4..19]
        $iv = [byte[]]$data[20..35]
        $mac = [byte[]]$data[36..67]
        $ct = [byte[]]$data[68..($data.Length - 1)]
        $keys = Get-Keys $pwBytes $salt
        $hmac = [Security.Cryptography.HMACSHA256]::new($keys.Mac)
        try { $calc = $hmac.ComputeHash([byte[]]($salt + $iv + $ct)) } finally { $hmac.Dispose() }
        if (-not (Test-CtEqual $calc $mac)) {
            Write-Warning "skip $($f.Name): wrong passphrase or tampered (HMAC mismatch)"; continue
        }
        $aes = [Security.Cryptography.Aes]::Create()
        $aes.KeySize = 256; $aes.Key = $keys.Aes; $aes.IV = $iv
        $aes.Mode = [Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
        try { $pt = $aes.CreateDecryptor().TransformFinalBlock($ct, 0, $ct.Length) } finally { $aes.Dispose() }
        $outName = $f.FullName -replace '\.enc$', ''
        [IO.File]::WriteAllBytes($outName, $pt)
        Remove-Item $f.FullName -Force
        Write-Host "decrypted  $($f.Name) -> $(Split-Path $outName -Leaf)"
    }
}

# scrub the passphrase bytes from memory
for ($i = 0; $i -lt $pwBytes.Length; $i++) { $pwBytes[$i] = 0 }
$Password = $null
Write-Host "done."
