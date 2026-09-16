# Khemistry live debug tool

This development tool connects Codex to the KSP test instance at
`C:\Users\name\Desktop\v1 test`. It consists of two deliberately separate programs:

- `Khemistry.DebugBridge.dll` runs inside KSP and accesses KSP, Unity, Khemistry, and
  Parallax on Unity's main thread.
- `KhemistryDebugMcp.exe` is a local MCP server used by the `khemistry-debug` Codex
  plugin. It never loads KSP assemblies itself.

The programs communicate through a per-process Windows named pipe. On startup, the
bridge writes a discovery file containing a random 256-bit session token to
`%TEMP%\khemistry-debug-session.json`. The pipe accepts only the current session's
token. The discovery file is removed when the bridge shuts down normally and stale
files are safely rejected when no matching pipe exists.

The bridge is a separate development DLL under `Khemistry\Debug\Plugins`. That folder
is ignored by the repository's normal `Debug/` ignore rule, so the bridge is present in
the symlinked test GameData but is not included in ordinary source commits or release
packages. Remove that folder to disable all in-game debug access.

## Build and update

Run from PowerShell:

```powershell
& .\Tools\Install-KhemistryDebugTool.ps1
```

The script builds the regular Khemistry DLL, builds and installs the bridge into the
symlinked mod, builds the MCP server, and updates the already-scaffolded personal plugin
at `%USERPROFILE%\plugins\khemistry-debug`.

After changing the MCP tool definitions or plugin files, reinstall the plugin and start
a new Codex task so the new server definition is loaded. Changes confined to the KSP
bridge require restarting KSP because Unity does not unload assemblies.

## Available operations

- Check/launch the configured KSP instance and read its current scene.
- List loaded parts and stable part flight IDs/module indexes.
- Inspect stock resources plus AdvancedStorage, fluid cells, material storage, kerbal
  suit cells, and Khemistry converters.
- Select a recipe and turn on/start/stop a converter.
- Run a bounded converter observation that captures state before and after plus recent
  Khemistry log messages.
- Enumerate nearest active Parallax collider instances, including exact scatter name,
  generated scale, position index, quad ID, distance, and collider state.
- Read recent KSP logs and request screenshots.

All KSP object access is queued and executed by `Update()` on Unity's main thread. A
command times out after 30 seconds if the game is frozen or the main thread is blocked.
Individual converter tests are capped at three minutes.

## Recommended workflow

1. Use `status`; use `launch_ksp` if necessary.
2. Load a dedicated test save and flight. Main-menu inspection correctly reports that
   no active vessel exists.
3. Use `list_parts`, then pass its `flightId` and module `index` to inspection or action
   tools.
4. Inspect before invoking a state-changing tool.
5. Use `run_converter_test` for repeatable converter checks, then inspect storage and
   logs. Capture a screenshot when visual state matters.

The bridge does not currently click stock UI, load a save from the main menu, teleport
vessels, edit arbitrary fields, execute arbitrary C#, or shut down KSP. Those omissions
keep the first version bounded and prevent accidental damage to normal saves. Full
flight setup can be added later as named, allowlisted test scenarios rather than as a
general remote console.

## Troubleshooting

- If `status` reports no bridge, confirm
  `GameData\Khemistry\Debug\Plugins\Khemistry.DebugBridge.dll` exists and restart KSP.
- If KSP exits while loading, inspect `KSP.log` for `[Khemistry Debug]` or assembly-loader
  errors and verify that the bridge was built against the same test instance.
- If Codex does not show the tools, reinstall `khemistry-debug@personal` and start a new
  task. MCP tools are discovered when the task starts.
- A stale `%TEMP%\khemistry-debug-session.json` can be deleted while KSP is closed; the
  next launch recreates it.
