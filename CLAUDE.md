# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

Windows desktop tool that toggles the built-in **Loudness Equalization** audio enhancement on any playback device. GUI (WinForms) with headless CLI mode. Requires administrator privileges to write registry.

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
- Device combo box populated from `DeviceManager.ListAllDevices()`, filtered by `IsDigitalOnly()` to hide HDMI/S/PDIF/NVIDIA digital outputs
- Device matching: exact name match first, then substring fallback
- Status labels showing ON/OFF/Unknown state with color coding
- Toggle button, Sound Settings button (opens `mmsys.cpl`), 3-second refresh timer (extends to 5s when no device)
- On `UnauthorizedAccessException`, re-launches itself via `Process.Start` with `Verb = "runas"` to trigger UAC, waits up to 30s for the child to complete

### [DeviceManager.cs](LoudnessEqualizer/DeviceManager.cs) — Core logic
- `ListAllDevices()` — registry-first: reads `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render` subkeys, extracts friendly names from `Properties\{a45c254e-df1c-4efd-8020-67d146a850e0},2`
- `IsDigitalOnly()` — filters HDMI, S/PDIF, NVIDIA, DisplayPort, TOSLINK, 数字音频 from GUI dropdown
- `GetState()` — primary: reads `{fc52a749-4be9-4510-896e-966ba6525980},3` binary value; validates 8-byte header (`0B 00 00 00 01 00 00 00`) then checks bytes [8..9]: `0xFF 0xFF` = ON, `0x00 0x00` = OFF. Fallback: checks if `{d04e05a6...},1`, `,2`, or `,5` contains the LEQ APO CLSID (`{62dc1a93...}`)
- `SetEnabled()` — writes LEQ APO CLSIDs to `{d04e05a6...},1`/`,2`/`,3` (PreMix/PostMix/UI slots), loudness binary state, and release time. Uses `SeRestorePrivilege` + `RegOpenKeyEx(REG_OPTION_BACKUP_RESTORE)` to bypass SYSTEM-owned ACL without modifying the security descriptor. Always targets FxProperties root.
- `RestartAudioService()` — spawns `powershell -Command "Restart-Service audiosrv -Force"`, waits up to 30s
- `DeviceInfo` sealed class: `DeviceId` (GUID with braces), `FriendlyName`, `RegistryPath`
- `SeBackupPrivilege` enable failure is non-fatal; logged via `Debug.WriteLine`
- Section comment numbering: 1=List, 2=Find, 3=GetState, 4=SetEnabled, 5=RestartAudio

### [Strings.cs](LoudnessEqualizer/Strings.cs) — Localization
- `Lang` enum: `En`, `Zh`
- All user-visible strings as static methods taking `Lang` parameter
- Includes `SoundSettingsBtn` for the Sound Settings button

### Docs
- [README.md](README.md) — Chinese documentation (default)
- [README-en.md](README-en.md) — English documentation
- [CLAUDE.md](CLAUDE.md) — Claude Code project instructions (this file)

## Key technical details

### LEQ APO CLSIDs and SFX slot layout

Under `PKEY_FX_StreamEffectClsid` (`{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}`), numbered slots register APOs:

| Slot | Purpose | LEQ CLSID |
|---|---|---|
| `,1` | PreMix SFX | `{62dc1a93-ae24-464c-a43e-452f824c4250}` |
| `,2` | PostMix SFX | `{637c490d-eee3-4c0a-973f-371958802da2}` |
| `,3` | Property page UI | `{5860E1C5-F95C-4a7a-8EC8-8AEF24F379A1}` |
| `,5`+ | Driver-specific slots | **never touched** |

- ON: write LEQ CLSIDs to `,1`/`,2`/`,3` + set `{fc52a749...},3` binary ON
- OFF: nullify `,1`/`,2` + set `{fc52a749...},3` binary OFF. Leave `,3` and `,5`+ untouched

The `,0` slot is KSNODETYPE, NOT an enable/disable switch — do not write to it.

### Registry ACL bypass (the critical trick)
`FxProperties` keys are owned by SYSTEM with restrictive ACL. The code does NOT modify ACLs. Instead:
1. `OpenProcessToken` + `AdjustTokenPrivileges` to enable `SeRestorePrivilege` and `SeBackupPrivilege`
2. `RegOpenKeyEx` / `RegCreateKeyEx` with `REG_OPTION_BACKUP_RESTORE` (0x04) — this flag bypasses ACL checks entirely
3. `RegSetValueEx` writes the value directly

### Magic byte patterns
The loudness equalization state is stored as a 12-byte binary value under `{fc52a749-4be9-4510-896e-966ba6525980},3`:
- Bytes 8-9: `FF FF` = enabled, `00 00` = disabled
- Bytes 0-7 header: `0B 00 00 00 01 00 00 00`
- When writing, existing bytes are read first and patched at offset 8-9; if no existing value, default templates are used

### Audio service restart is required
The Windows audio engine only reads FxProperties on startup. After writing registry values, `audiosrv` must be restarted for changes to take effect. This causes a brief audio dropout.

## CI/CD

GitHub Actions (`.github/workflows/build.yml`): builds on `windows-latest`, publishes self-contained win-x64 single-file exe, uploads as artifact. On version tags (`v*`), creates a GitHub Release.
