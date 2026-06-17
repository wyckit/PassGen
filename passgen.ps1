#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run the self-contained passgen random-string assistant (no LLM, pure 7-TLM data).

.DESCRIPTION
    The "passgen" host layer: deterministic rule-based English -> ConstraintSpec, dispatched
    to RandomStringTool (the same function-call contract an LLM would use), plus knowledge
    Q&A from the random-string TLM bundle. No Ollama, no network.

    Run with NO arguments for an interactive chat that waits for each line.
    Pass a request as arguments for a one-shot answer.

.PARAMETER Build
    Force a rebuild of the host before running.

.PARAMETER Query
    A one-shot request (generate a password, or a question). Omit for interactive chat.

.EXAMPLE
    .\passgen.ps1
    .\passgen.ps1 give me a 16 character password with 2 uppercase and no ambiguous
    .\passgen.ps1 what makes a password strong
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [switch]$Build,
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)][string[]]$Query
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'PassGen.App/PassGen.App.csproj'
$dll = Join-Path $root 'PassGen.App/bin/Debug/net10.0/passgen.dll'

if ($Build -or -not (Test-Path $dll)) {
    Write-Host "==> Building PassGen.App" -ForegroundColor Cyan
    dotnet build $proj -c Debug -v quiet | Out-Null
    if (-not (Test-Path $dll)) { throw "Build did not produce $dll" }
}

# NOTE: no ValueFromPipeline parameter — that made PowerShell hand the child a
# closed stdin, so the interactive REPL saw EOF and exited without waiting.
# Invoking dotnet plainly lets the app inherit the real console for Console.ReadLine().
if ($Query -and $Query.Count -gt 0) {
    & dotnet $dll @Query                 # one-shot
}
else {
    & dotnet $dll                        # interactive chat — waits for each line
}
