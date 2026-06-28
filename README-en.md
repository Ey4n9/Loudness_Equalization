# Loudness Equalizer

[中文版](README.md)

Toggle Windows built-in **Loudness Equalization** with one click.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![License](https://img.shields.io/badge/license-MIT-green)

## Download

Get the exe from [Releases](https://github.com/Ey4n9/Loudness_Equalization/releases):

- `LoudnessEqualizer.exe` — English UI
- `LoudnessEqualizer-zh.exe` — Chinese UI

> If toggling doesn't work, try right-click → Run as administrator.

## How it Works

Writes to the Loudness Equalization APO config under registry `FxProperties`, bypassing the SYSTEM ACL via `SeRestorePrivilege` + `REG_OPTION_BACKUP_RESTORE`, then restarts the `audiosrv` service to apply.

## Notes

After toggling loudness equalization, some voice chat apps (e.g. Discord, Teams, KOOK) may lose microphone/speaker detection. Restart the voice chat app to restore it.

This project was built entirely with [Claude Code](https://claude.ai/code). I'm not a professional developer and can't guarantee it works on every PC.

If it doesn't work for you, try searching GitHub for other loudness equalization tools. Or grab the source code and ask [Claude Code](https://claude.ai/code) to fix it for your machine.

## License

MIT — see [LICENSE](LICENSE)
