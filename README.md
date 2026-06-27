# Loudness Equalizer

[中文版](README-zh.md)

Toggle Windows built-in **Loudness Equalization** audio effect for any playback device — with a single click or command line.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![License](https://img.shields.io/badge/license-MIT-green)

> **Note:** This project was developed entirely using [Claude Code](https://claude.ai/code) — an AI-powered coding agent. While it works for the intended use case, it has not been extensively tested across all hardware configurations. Expect rough edges. Bug reports and pull requests are welcome.

## What it does

Windows has a hidden per-device audio enhancement called **Loudness Equalization** that normalizes volume levels (quieter sounds get louder, louder sounds get quieter). Normally you have to dig through Control Panel → Sound → Device Properties → Enhancements to toggle it.

This tool finds your playback device and toggles the setting instantly, with a simple GUI or a headless CLI mode for scripting.

## System Requirements

- Windows 10 or 11 (64-bit)
- [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (or use the self-contained build)

## Download

Go to the [Releases](https://github.com/Ey4n9/Loudness_Equalization/releases) page and download `LoudnessEqualizer.exe`.

> **Administrator privileges are required.** Right-click and select "Run as administrator", or accept the UAC prompt when toggling.

## Usage

### GUI mode

```
LoudnessEqualizer.exe
```

Launches a small window showing the device status and a toggle button.

### Language

```
LoudnessEqualizer.exe --lang zh
```

Switches the UI to Chinese. Or rename the exe with `-zh` or `_zh` in the filename for auto-detection.

### Specify a device

```
LoudnessEqualizer.exe --device "Your Device Name"
```

Matches by exact device name first, then falls back to substring match (case-insensitive). Without `--device`, picks the first available playback device.

Find your device name in **Control Panel → Sound → Playback** tab.

### Headless mode (CLI)

```
LoudnessEqualizer.exe --apply on  --device "Speakers"
LoudnessEqualizer.exe --apply off --device "Headset"
```

Enables or disables loudness equalization without showing the GUI. Exits with code 0 on success, 1 on failure. Useful for scripts, hotkeys, or automation.

```
LoudnessEqualizer.exe --help
```

Shows all available options.

## Build from Source

```bash
git clone https://github.com/Ey4n9/Loudness_Equalization.git
cd Loudness_Equalization/LoudnessEqualizer
dotnet build -c Release
```

To produce a self-contained single-file executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -o publish
```

## How it works

1. **Find the device** — scans `HKLM\SOFTWARE\...\MMDevices\Audio\Render` registry keys and optionally COM enumeration to locate the target playback endpoint
2. **Read state** — checks `FxProperties\{clsid}\User` for the Loudness Equalization APO value
3. **Write state** — uses `SeRestorePrivilege` + `RegOpenKeyEx(REG_OPTION_BACKUP_RESTORE)` to bypass the SYSTEM-owned ACL and write new values directly via `RegSetValueEx`. No ACL modification — the security descriptor is never touched
4. **Restart audio** — restarts the `audiosrv` Windows Audio service so the engine re-reads the updated registry config

## Tech stack

| Layer | Technology |
|---|---|
| UI | WinForms (.NET 8) |
| Device enumeration | COM (`IMMDeviceEnumerator`, `IPropertyStore`) |
| Registry access | Native `advapi32.dll` P/Invoke (`RegOpenKeyEx`, `RegCreateKeyEx`, `RegSetValueEx`) |
| Service restart | PowerShell subprocess (`Restart-Service audiosrv -Force`) |

## License

MIT — see [LICENSE](LICENSE)
