param(
    [string]$KspRoot = 'C:\Users\name\Desktop\v1 test',
    [string]$PluginRoot = "$env:USERPROFILE\plugins\khemistry-debug"
)

$ErrorActionPreference = 'Stop'
$RepositoryRoot = Split-Path -Parent $PSScriptRoot

dotnet build "$RepositoryRoot\KhemistryDLLs\Khemistry\Khemistry.csproj" `
    -c Release "-p:KspRoot=$KspRoot"
dotnet build "$RepositoryRoot\KhemistryDLLs\KhemistryDebugBridge\KhemistryDebugBridge.csproj" `
    -c Release "-p:KspRoot=$KspRoot"
dotnet build "$RepositoryRoot\Tools\KhemistryDebugMcp\KhemistryDebugMcp.csproj" `
    -c Release

$ServerSource = "$RepositoryRoot\Tools\KhemistryDebugMcp\bin\Release\net10.0"
$ServerDestination = "$PluginRoot\server"
if (-not (Test-Path -LiteralPath "$PluginRoot\.codex-plugin\plugin.json")) {
    throw "The khemistry-debug Codex plugin is not installed at $PluginRoot."
}
New-Item -ItemType Directory -Force -Path $ServerDestination | Out-Null
Copy-Item -Force -LiteralPath "$ServerSource\KhemistryDebugMcp.exe" -Destination $ServerDestination
Copy-Item -Force -LiteralPath "$ServerSource\KhemistryDebugMcp.dll" -Destination $ServerDestination
Copy-Item -Force -LiteralPath "$ServerSource\KhemistryDebugMcp.deps.json" -Destination $ServerDestination
Copy-Item -Force -LiteralPath "$ServerSource\KhemistryDebugMcp.runtimeconfig.json" -Destination $ServerDestination

Write-Host "Khemistry, its debug bridge, and the Codex MCP server were rebuilt."
Write-Host "KSP bridge: $RepositoryRoot\Khemistry\Debug\Plugins\Khemistry.DebugBridge.dll"
Write-Host "Codex server: $ServerDestination\KhemistryDebugMcp.exe"
