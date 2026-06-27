namespace LoudnessEqualizer;

public enum Lang { En, Zh }

public static class Strings
{
    // ── Window ──
    public static string WindowTitle(Lang lang) => lang == Lang.Zh ? "响度均衡工具" : "Loudness Equalizer";

    // ── Status ──
    public static string Detecting(Lang lang) => lang == Lang.Zh ? "正在检测设备…" : "Detecting device...";
    public static string LoudnessOn(Lang lang) => lang == Lang.Zh ? "● 响度均衡：已开启" : "● Loudness Equalization: ON";
    public static string LoudnessOff(Lang lang) => lang == Lang.Zh ? "○ 响度均衡：已关闭" : "○ Loudness Equalization: OFF";
    public static string StateUnknown(Lang lang) => lang == Lang.Zh ? "状态未知" : "State unknown";
    public static string NoDevice(Lang lang) => lang == Lang.Zh ? "未检测到设备" : "No device selected";
    public static string NoDeviceDetail(Lang lang) => lang == Lang.Zh ? "从列表中选择播放设备" : "Select a device from the list";
    public static string NoFxProperties(Lang lang) => lang == Lang.Zh ? "未找到 FxProperties 配置" : "no FxProperties found";
    public static string Error(Lang lang) => lang == Lang.Zh ? "错误" : "Error";

    // ── Buttons ──
    public static string ToggleBtn(Lang lang) => lang == Lang.Zh ? "切换" : "Toggle";
    public static string EnableBtn(Lang lang) => lang == Lang.Zh ? "开启" : "Enable";
    public static string DisableBtn(Lang lang) => lang == Lang.Zh ? "关闭" : "Disable";
    public static string RefreshBtn(Lang lang) => lang == Lang.Zh ? "刷新" : "Refresh";
    public static string RetryBtn(Lang lang) => lang == Lang.Zh ? "重试" : "Retry";

    // ── Busy / progress ──
    public static string Enabling(Lang lang) => lang == Lang.Zh ? "正在开启响度均衡…" : "Enabling Loudness Equalization...";
    public static string Disabling(Lang lang) => lang == Lang.Zh ? "正在关闭响度均衡…" : "Disabling Loudness Equalization...";
    public static string RestartingAudio(Lang lang) => lang == Lang.Zh ? "正在重启音频服务以生效…" : "Restarting audio service to apply changes...";
    public static string RequestingAdmin(Lang lang) => lang == Lang.Zh ? "正在请求管理员权限…" : "Requesting administrator privileges...";

    // ── Errors ──
    public static string NoPlaybackDevice(Lang lang) => lang == Lang.Zh
        ? "未找到播放设备。使用 --device 指定设备名。"
        : "No playback device found. Use --device to specify one.";
    public static string InvalidArg(Lang lang, string arg) => lang == Lang.Zh
        ? $"无效的参数 \"{arg}\"。\n\n用法: LoudnessEqualizer --apply on|off [--device \"设备名\"]"
        : $"Invalid argument \"{arg}\".\n\nUsage: LoudnessEqualizer --apply on|off [--device \"Device Name\"]";
    public static string DeviceNotFound(Lang lang, string name) => lang == Lang.Zh
        ? $"未找到设备 \"{name}\"。"
        : $"Device \"{name}\" not found.";
    public static string FailedLaunch(Lang lang) => lang == Lang.Zh ? "无法启动提权进程。" : "Failed to launch elevated process.";
    public static string ProcessTimeout(Lang lang) => lang == Lang.Zh ? "提权进程未在 30 秒内完成。" : "Elevated process did not complete within 30 seconds.";
    public static string NeedAdmin(Lang lang) => lang == Lang.Zh
        ? "需要管理员权限才能修改音频设置。\n\n请右键以管理员身份运行本程序，或在 UAC 弹窗中点击「是」。"
        : "Administrator privileges are required.\n\nRight-click and Run as administrator, or accept the UAC prompt.";
    public static string BusyClose(Lang lang) => lang == Lang.Zh
        ? "正在执行操作，请稍候…"
        : "An operation is in progress. Please wait...";
    public static string Info(Lang lang) => lang == Lang.Zh ? "提示" : "Info";
    public static string ErrorTitle(Lang lang) => lang == Lang.Zh ? "错误" : "Error";
}
