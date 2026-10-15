param(
    [string]$KspRoot = 'C:\Users\name\Desktop\v1 test',
    [string]$PluginRoot = "$env:USERPROFILE\plugins\chitaktech-debug"
)

$ErrorActionPreference = 'Stop'
$RepositoryRoot = Split-Path -Parent $PSScriptRoot

dotnet build "$RepositoryRoot\ChitakTechDLLs\ChitakTech\ChitakTech.csproj" `
    -c Release "-p:KspRoot=$KspRoot"
dotnet build "$RepositoryRoot\ChitakTechDLLs\ChitakTechDebugBridge\ChitakTechDebugBridge.csproj" `
    -c Release "-p:KspRoot=$KspRoot"
dotnet build "$RepositoryRoot\Tools\ChitakTechDebugMcp\ChitakTechDebugMcp.csproj" `
    -c Release

$ServerSource = "$RepositoryRoot\Tools\ChitakTechDebugMcp\bin\Release\net10.0"
$ServerDestination = "$PluginRoot\server"
if (-not (Test-Path -LiteralPath "$PluginRoot\.codex-plugin\plugin.json")) {
    throw "The chitaktech-debug Codex plugin is not installed at $PluginRoot."
}
New-Item -ItemType Directory -Force -Path $ServerDestination | Out-Null
Copy-Item -Force -LiteralPath "$ServerSource\ChitakTechDebugMcp.exe" -Destination $ServerDestination
Copy-Item -Force -LiteralPath "$ServerSource\ChitakTechDebugMcp.dll" -Destination $ServerDestination
Copy-Item -Force -LiteralPath "$ServerSource\ChitakTechDebugMcp.deps.json" -Destination $ServerDestination
Copy-Item -Force -LiteralPath "$ServerSource\ChitakTechDebugMcp.runtimeconfig.json" -Destination $ServerDestination

Write-Host "ChitakTech, its debug bridge, and the Codex MCP server were rebuilt."
Write-Host "KSP bridge: $RepositoryRoot\ChitakTech\Debug\Plugins\ChitakTech.DebugBridge.dll"
Write-Host "Codex server: $ServerDestination\ChitakTechDebugMcp.exe"
