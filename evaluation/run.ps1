#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"

function ExecSafe([scriptblock] $ScriptBlock) {
  & $ScriptBlock
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$root = Split-Path -Parent $PSScriptRoot

ExecSafe { dotnet publish "$root/src/BicepGeneratorMcp" }

ExecSafe { dotnet run --project "$root/src/BicepGeneratorEval" -- `
    --mcp-server-path "$root/src/BicepGeneratorMcp/bin/Release/net10.0/publish/BicepGeneratorMcp" `
    --subscription-id "57c853ca-9f92-4c7b-87e8-6656490dac62" `
    --resource-group "mcp-ai-test" `
    --prompts-path "$root/evaluation/prompts.json" `
    --output "$root/evaluation/results.md" }