#!/usr/bin/env pwsh
# =============================================================================
# build-dataset.ps1 - build the standalone RSRM random-string TLM dataset.
#
# Steps:
#   1. dotnet build the self-contained PassGen.Tlm.Cli tool
#   2. author    dataset/source/*.source.json from the C# DatasetAuthor (the model)
#   3. compile   dataset/source/*.source.json  ->  dataset/compiled/*.tlmz
#   4. decompile dataset/compiled/*.tlmz       ->  dataset/decompiled/*.json
#   5. verify    full lossless round-trip integrity over the whole dataset
#
# Fully C# - no Python. Grow coverage by editing PassGen.Tlm.Cli/DatasetAuthor.cs.
# =============================================================================
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$cli = Join-Path $root 'PassGen.Tlm.Cli/PassGen.Tlm.Cli.csproj'
$dataset = Join-Path $root 'dataset'

Write-Host "==> Building PassGen.Tlm.Cli" -ForegroundColor Cyan
dotnet build $cli -c Release -v quiet | Out-Null
$dll = Join-Path $root 'PassGen.Tlm.Cli/bin/Release/net10.0/tlm.dll'

function Tlm { dotnet $dll @args --root $dataset }

Write-Host "`n==> Authoring sources (C#)" -ForegroundColor Cyan
Tlm author

Write-Host "`n==> Compiling sources -> .tlmz" -ForegroundColor Cyan
Tlm compile all

Write-Host "`n==> Decompiling .tlmz -> .json" -ForegroundColor Cyan
Tlm decompile all

Write-Host "`n==> Verifying round-trip integrity" -ForegroundColor Cyan
Tlm verify
$code = $LASTEXITCODE

if ($code -eq 0) {
    Write-Host "`nDataset build OK - all TLMs compiled, decompiled, and verified lossless." -ForegroundColor Green
} else {
    Write-Host "`nDataset build FAILED - round-trip verification did not pass." -ForegroundColor Red
}
exit $code
