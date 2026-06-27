# 响度均衡工具

[English](README-en.md)

一键切换 Windows 内置**响度均衡**音效，支持图形界面和命令行。

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![License](https://img.shields.io/badge/license-MIT-green)

> **注意：** 本项目完全由 [Claude Code](https://claude.ai/code) 制作，未经所有硬件配置充分测试，欢迎提交 Bug 报告和 Pull Request。

## 下载

前往 [Releases](https://github.com/Ey4n9/Loudness_Equalization/releases) 下载 `LoudnessEqualizer.exe`。

> **需要管理员权限。**

## 用法

```bash
# 图形界面
LoudnessEqualizer.exe

# 指定设备 + 中文界面
LoudnessEqualizer-zh.exe --device "扬声器"

# 命令行开关（适合脚本/快捷键）
LoudnessEqualizer.exe --apply on  --device "扬声器"
LoudnessEqualizer.exe --apply off --device "耳机"

# 查看帮助
LoudnessEqualizer.exe --help
```

文件名含 `-zh` 自动切换中文，也可用 `--lang zh` 手动指定。

## 从源码构建

```bash
git clone https://github.com/Ey4n9/Loudness_Equalization.git
cd LoudnessEqualizer
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## 原理

通过修改注册表 `FxProperties` 下的响度均衡 APO 配置，使用 `SeRestorePrivilege` + `REG_OPTION_BACKUP_RESTORE` 绕过 SYSTEM ACL 写入，最后重启 `audiosrv` 服务生效。

## 许可证

MIT — 详见 [LICENSE](LICENSE)
