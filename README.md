# 响度均衡工具

[English](README-en.md)

一键切换 Windows 内置**响度均衡**音效。

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![License](https://img.shields.io/badge/license-MIT-green)

## 下载

前往 [Releases](https://github.com/Ey4n9/Loudness_Equalization/releases) 下载：

- `LoudnessEqualizer-zh.exe` — 中文界面
- `LoudnessEqualizer.exe` — 英文界面

> **必须右键 → 以管理员身份运行**，否则无法修改音频设置。

## 原理

通过修改注册表 `FxProperties` 下的响度均衡 APO 配置，使用 `SeRestorePrivilege` + `REG_OPTION_BACKUP_RESTORE` 绕过 SYSTEM ACL 写入，最后重启 `audiosrv` 服务生效。

## 注意

本项目完全由 [Claude Code](https://claude.ai/code) 制作，个人能力有限，无法保证在所有电脑上正常运行。

如果遇到问题，可以在 GitHub 搜索其他切换响度均衡的工具。或者下载本项目的源码，交给 [Claude Code](https://claude.ai/code) 让它帮你修改。

## 许可证

MIT — 详见 [LICENSE](LICENSE)
