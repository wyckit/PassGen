#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Assemble a distributable publish/ folder: the self-contained passgen app, ONLY the
    compiled .tlmz data, the encrypt/decrypt script, instructions, and the architecture doc.

.PARAMETER Rid
    Runtime identifier for the self-contained build (default win-x64).
#>
[CmdletBinding()]
param([string]$Rid = 'win-x64')

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$pub = Join-Path $root 'publish'

Write-Host "==> Cleaning $pub" -ForegroundColor Cyan
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
New-Item -ItemType Directory -Force $pub | Out-Null

Write-Host "==> Publishing passgen app (self-contained $Rid)" -ForegroundColor Cyan
dotnet publish (Join-Path $root 'PassGen.App\PassGen.App.csproj') `
    -c Release -r $Rid --self-contained true `
    -o (Join-Path $pub 'app') -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "==> Copying compiled TLMs (only .tlmz)" -ForegroundColor Cyan
$cdir = Join-Path $pub 'dataset\compiled'
New-Item -ItemType Directory -Force $cdir | Out-Null
Copy-Item (Join-Path $root 'dataset\compiled\rs-*.tlmz') $cdir
$count = (Get-ChildItem $cdir -Filter *.tlmz).Count
Write-Host "    $count .tlmz copied"

Write-Host "==> Adding crypto script, launcher, docs" -ForegroundColor Cyan
Copy-Item (Join-Path $root 'protect-tlms.ps1') (Join-Path $pub 'protect-tlms.ps1')
$arch = Join-Path $root 'docs\ARCHITECTURE.md'
if (Test-Path $arch) { Copy-Item $arch (Join-Path $pub 'ARCHITECTURE.md') }

# Windows launchers
Set-Content -Encoding ascii -Path (Join-Path $pub 'passgen.cmd') -Value @(
    '@echo off',
    '"%~dp0app\passgen.exe" %*'
)
# decompile launcher: dumps the compiled .tlmz back to readable JSON in dataset/decompiled
Set-Content -Encoding ascii -Path (Join-Path $pub 'decompile.cmd') -Value @(
    '@echo off',
    '"%~dp0app\passgen.exe" --decompile %*'
)

# README (ASCII only so Windows PowerShell never mis-reads it)
$exe = if ($Rid -like 'win*') { 'app\passgen.exe' } else { 'app/passgen' }
$readme = @"
# PassGen - password assistant (distributable)

Self-contained build. No .NET install required ($Rid runtime is bundled).

## Contents
  app/                  the passgen application (self-contained)
  dataset/compiled/     the knowledge: ONLY the compiled .tlmz bundle (7 TLMs)
  passgen.cmd              launcher (Windows)
  decompile.cmd         dump the .tlmz knowledge to readable JSON (dataset/decompiled/)
  protect-tlms.ps1      encrypt/decrypt the .tlmz data at rest
  README.md             this file
  ARCHITECTURE.md       how it all works + how to reuse it for other programs

## Run
  Double-click passgen.cmd, or from a terminal:

    .\passgen.cmd                         interactive chat (waits for each line)
    .\passgen.cmd give me a 16 char password with 2 uppercase and no ambiguous
    .\passgen.cmd what makes a password strong

  Or run the app directly:  $exe

  In the chat, type a request or a question. Commands:
    /recall <topic>   search the knowledge graph
    /decompile [n]    dump the .tlmz knowledge to readable JSON (name or 'all')
    /copy             copy the last password to the clipboard
    /mask [on|off]    hide passwords on screen (copy them instead)
    /clear            wipe the screen
    /seed [n]         deterministic seed for TESTING only (insecure); omit to clear
    /help  /exit

## Inspect / decompile the knowledge
  Turn the compiled .tlmz back into readable JSON (written to dataset/decompiled/):

    .\decompile.cmd            decompile all TLMs
    .\decompile.cmd rs-entropy decompile one

  (Or inside the chat: /decompile all  /  /decompile rs-entropy)
  If the data is encrypted, decrypt it first (see below).

## Security
  - Default generation uses the OS cryptographic RNG (unbiased) - safe for real passwords.
  - /seed makes output reproducible and is flagged [INSECURE]; use only for tests.
  - Entropy under 80 bits is flagged [WEAK] with advice.

## Protect the data at rest (optional)
  The app needs plaintext .tlmz to run. To encrypt the bundle when not in use:

    powershell -ExecutionPolicy Bypass -File .\protect-tlms.ps1 -Mode encrypt

  This turns each dataset/compiled/*.tlmz into *.tlmz.enc (AES-256 + HMAC, passphrase
  derived via PBKDF2) and deletes the plaintext. To use the app again:

    powershell -ExecutionPolicy Bypass -File .\protect-tlms.ps1 -Mode decrypt

  A wrong passphrase or any tampering is rejected (HMAC check). If you launch passgen
  while the data is still encrypted, it will report it cannot find the .tlmz bundle -
  decrypt first.
"@
Set-Content -Encoding ascii -Path (Join-Path $pub 'README.md') -Value $readme

Write-Host ""
Write-Host "Publish ready: $pub" -ForegroundColor Green
Get-ChildItem $pub | ForEach-Object { Write-Host "  $($_.Name)" }
