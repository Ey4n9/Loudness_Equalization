# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

Windows desktop tool that toggles the built-in **Loudness Equalization** audio enhancement on any playback device. GUI (WinForms) or headless CLI mode. Requires administrator privileges to write registry.

## Build & run

```bash
# Build
cd LoudnessEqualizer
dotnet build -c Release

# Publish self-contained single-file .exe (with Chinese variant)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -o publish
cp publish/LoudnessEqualizer.exe publish/LoudnessEqualizer-zh.exe

# Headless toggle (must run as admin)
dotnet run -c Release --no-build -- --apply on --device "Speakers"
dotnet run -c Release --no-build -- --apply off --device "Headset"

# Show help
dotnet run -c Release --no-build -- --help
```

No test suite exists. Git remote uses SSH (`git@github.com:Ey4n9/Loudness_Equalization.git`).

## Architecture

Four source files in `LoudnessEqualizer/` namespace `LoudnessEqualizer`, plus docs:

### [Program.cs](LoudnessEqualizer/Program.cs) — Entry point + COM interop types
- `Main()` parses `--device`, `--lang`, `--help`/`-h`, and `--apply on|off` from args
- Language auto-detection: if exe filename contains `-zh` or `_zh`, defaults to Chinese; `--lang zh` override
- `--help` / `-h` prints bilingual usage text and exits
- Headless mode: applies setting + restarts audio service, exits with 0 or 1
- GUI mode: `Application.Run(new MainForm(...))`
- Defines all COM interop types: `IMMDeviceEnumerator`, `IMMDevice`, `IPropertyStore`, `PROPVARIANT`, `PROPERTYKEY`, P/Invoke for `PropVariantClear`

### [MainForm.cs](LoudnessEqualizer/MainForm.cs) — WinForms GUI
- Device combo box populated from `DeviceManager.ListAllDevices()`
- Device matching: exact name match first, then substring fallback
- Status labels showing ON/OFF/Unknown state with color coding
- Toggle button, 3-second refresh timer (extends to 5s when no device)
- On `UnauthorizedAccessException`, re-launches itself via `Process.Start` with `Verb = "runas"` to trigger UAC, waits up to 30s for the child to complete

### [DeviceManager.cs](LoudnessEqualizer/DeviceManager.cs) — Core logic
- `ListAllDevices()` — registry-first: reads `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render` subkeys, extracts friendly names from `Properties\{a45c254e-df1c-4efd-8020-67d146a850e0},2`. Falls back to COM `IMMDeviceEnumerator` enumeration.
- `GetState()` — reads `FxProperties\{clsid}\User\{fc52a749-4be9-4510-896e-966ba6525980},3` binary value; validates 8-byte header (`0B 00 00 00 01 00 00 00`) then checks bytes [8..9]: `0xFF 0xFF` = ON, `0x00 0x00` = OFF; unknown header → Unknown
- `SetEnabled()` — writes the Enhancement CLSID, enabled/disabled flag, and default release time. Uses `SeRestorePrivilege` + `RegOpenKeyEx(REG_OPTION_BACKUP_RESTORE)` to bypass SYSTEM-owned ACL without modifying the security descriptor.
- `RestartAudioService()` — spawns `powershell -Command "Restart-Service audiosrv -Force"`, waits up to 30s
- `DeviceInfo` sealed class: `DeviceId` (GUID with braces), `FriendlyName`, `RegistryPath`
- `SeBackupPrivilege` enable failure is non-fatal; logged via `Debug.WriteLine`
- Section comment numbering: 1=List, 2=Find, 3=GetState, 4=SetEnabled, 5=RestartAudio

### [Strings.cs](LoudnessEqualizer/Strings.cs) — Localization
- `Lang` enum: `En`, `Zh`
- All user-visible strings as static methods taking `Lang` parameter
- `Error` used uniformly for error titles and labels (removed duplicate `ErrorTitle`)

### Docs
- [README.md](README.md) — English documentation
- [README-zh.md](README-zh.md) — Chinese documentation
- [CLAUDE.md](CLAUDE.md) — Claude Code project instructions (this file)

## Key technical details

### Registry ACL bypass (the critical trick)
`FxProperties` keys are owned by SYSTEM with restrictive ACL. The code does NOT modify ACLs. Instead:
1. `OpenProcessToken` + `AdjustTokenPrivileges` to enable `SeRestorePrivilege` and `SeBackupPrivilege`
2. `RegOpenKeyEx` / `RegCreateKeyEx` with `REG_OPTION_BACKUP_RESTORE` (0x04) — this flag bypasses ACL checks entirely
3. `RegSetValueEx` writes the value directly

### FxProperties path discovery
`GetFxCandidatePaths()` searches under the device's GUID key for `FxProperties`, `FxProperties\{clsid}\User`, `FxProperties\{clsid}\Default`, `FxProperties\{clsid}\Volatile`. Prefers `User` paths; deduplicates. Paths are hardcoded with `REG_OPTION_BACKUP_RESTORE`.

### Magic byte patterns
The loudness equalization state is stored as a 12-byte binary value under `{fc52a749-4be9-4510-896e-966ba6525980},3`:
- Bytes 8-9: `FF FF` = enabled, `00 00` = disabled
- Bytes 0-7 header: `0B 00 00 00 01 00 00 00`
- When writing, existing bytes are read first and patched at offset 8-9; if no existing value, default templates are used

### Audio service restart is required
The Windows audio engine only reads FxProperties on startup. After writing registry values, `audiosrv` must be restarted for changes to take effect. This causes a brief audio dropout.

## CI/CD

GitHub Actions (`.github/workflows/build.yml`): builds on `windows-latest`, publishes self-contained win-x64 single-file exe, uploads as artifact. On version tags (`v*`), creates a GitHub Release.
