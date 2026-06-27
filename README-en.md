# Loudness Equalizer

[中文版](README.md)

Toggle Windows built-in **Loudness Equalization** audio effect with one click or a single command.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![License](https://img.shields.io/badge/license-MIT-green)

> **Note:** This project was built entirely with [Claude Code](https://claude.ai/code). It has not been extensively tested across all hardware. Bug reports and PRs welcome.

## Download

Get `LoudnessEqualizer.exe` from the [Releases](https://github.com/Ey4n9/Loudness_Equalization/releases) page.

> **Administrator privileges required.**

## Usage

```bash
# GUI mode
LoudnessEqualizer.exe

# Specify device + language override
LoudnessEqualizer.exe --device "Speakers" --lang zh

# CLI toggle (for scripts / hotkeys)
LoudnessEqualizer.exe --apply on  --device "Speakers"
LoudnessEqualizer.exe --apply off --device "Headset"

# Show help
LoudnessEqualizer.exe --help
```

Rename the exe with `-zh` in the filename for auto Chinese UI, or use `--lang zh`.

## Build from Source

```bash
git clone https://github.com/Ey4n9/Loudness_Equalization.git
cd LoudnessEqualizer
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## How it Works

Writes to the Loudness Equalization APO config under registry `FxProperties`, bypassing the SYSTEM ACL via `SeRestorePrivilege` + `REG_OPTION_BACKUP_RESTORE`, then restarts the `audiosrv` service to apply.

## License

MIT — see [LICENSE](LICENSE)
