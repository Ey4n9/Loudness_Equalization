# 响度均衡工具

[English](README-en.md)

一键切换任意播放设备的 Windows 内置**响度均衡**音效——支持图形界面和命令行。

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![License](https://img.shields.io/badge/license-MIT-green)

> **注意：** 本项目完全由 [Claude Code](https://claude.ai/code)（AI 编程助手）制作。功能在常规场景下可用，但未经所有硬件配置的充分测试，可能存在未知问题。欢迎提交 Bug 报告和 Pull Request。

## 功能

Windows 隐藏了一个名为**响度均衡（Loudness Equalization）**的每设备音频增强功能，可以自动统一音量大小（小声音变大、大声音变小）。通常你需要打开 控制面板 → 声音 → 设备属性 → 增强 才能开关它，非常繁琐。

本工具可快速找到你的播放设备并一键切换该设置，提供简洁的图形界面和适合脚本调用的命令行模式。

## 系统要求

- Windows 10 或 11（64 位）
- [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)（使用自包含版本则无需安装）

## 下载

前往 [Releases](https://github.com/Ey4n9/Loudness_Equalization/releases) 页面下载 `LoudnessEqualizer.exe`。

> **需要管理员权限。** 请右键选择"以管理员身份运行"，或在弹出的 UAC 窗口中点击"是"。

## 用法

### 图形界面

```
LoudnessEqualizer.exe
```

启动一个小窗口，显示设备状态和切换按钮。

### 中文界面

```
LoudnessEqualizer-zh.exe
```

或带 `--lang zh` 参数：

```
LoudnessEqualizer.exe --lang zh
```

文件名包含 `-zh` 或 `_zh` 会自动识别为中文界面。

### 指定设备

```
LoudnessEqualizer.exe --device "设备名称"
```

优先精确匹配设备名，精确匹配不到再模糊匹配（不区分大小写）。不指定 `--device` 则匹配第一个可用的播放设备。

可在 **控制面板 → 声音 → 播放** 选项卡中查看设备名称。

### 命令行模式（无界面）

```
LoudnessEqualizer.exe --apply on  --device "扬声器"
LoudnessEqualizer.exe --apply off --device "耳机"
```

开启或关闭响度均衡，不弹出窗口。成功返回 0，失败返回 1。适合脚本、快捷键或自动化场景。

```
LoudnessEqualizer.exe --help
```

查看完整参数说明。

## 从源码构建

```bash
git clone https://github.com/Ey4n9/Loudness_Equalization.git
cd LoudnessEqualizer
dotnet build -c Release
```

生成自包含单文件程序：

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -o publish
```

## 工作原理

1. **查找设备** — 扫描 `HKLM\SOFTWARE\...\MMDevices\Audio\Render` 注册表键，必要时回退 COM 枚举方式定位目标播放端点
2. **读取状态** — 检查 `FxProperties\{clsid}\User` 下的响度均衡 APO 值
3. **写入状态** — 使用 `SeRestorePrivilege` + `RegOpenKeyEx(REG_OPTION_BACKUP_RESTORE)` 绕过 SYSTEM 所有的 ACL，通过 `RegSetValueEx` 直接写入新值。全程不改动安全描述符
4. **重启音频服务** — 重启 `audiosrv` Windows Audio 服务，使音频引擎重新读取更新后的注册表配置

## 技术栈

| 层次 | 技术 |
|---|---|
| UI | WinForms（.NET 8） |
| 设备枚举 | COM（`IMMDeviceEnumerator`、`IPropertyStore`） |
| 注册表访问 | 原生 `advapi32.dll` P/Invoke（`RegOpenKeyEx`、`RegCreateKeyEx`、`RegSetValueEx`） |
| 服务重启 | PowerShell 子进程（`Restart-Service audiosrv -Force`） |

## 许可证

MIT — 详见 [LICENSE](LICENSE)
