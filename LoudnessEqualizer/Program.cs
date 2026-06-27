using System.Runtime.InteropServices;

namespace LoudnessEqualizer;

// ──────────────────────────────────────────────
// COM types — only used for device enumeration
// ──────────────────────────────────────────────
internal static class Clsid
{
    public static readonly Guid MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
}

internal enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

[StructLayout(LayoutKind.Explicit)]
internal struct PROPVARIANT
{
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public IntPtr ptrVal;
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints([In] EDataFlow dataFlow, [In] uint dwStateMask, [Out] out IMMDeviceCollection ppDevices);
    [PreserveSig] int GetDefaultAudioEndpoint([In] EDataFlow dataFlow, [In] uint role, [Out] out IMMDevice ppEndpoint);
    [PreserveSig] int GetDevice([In, MarshalAs(UnmanagedType.LPWStr)] string pwstrId, [Out] out IMMDevice ppDevice);
    [PreserveSig] int RegisterEndpointNotificationCallback([In, MarshalAs(UnmanagedType.IUnknown)] object pClient);
    [PreserveSig] int UnregisterEndpointNotificationCallback([In, MarshalAs(UnmanagedType.IUnknown)] object pClient);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount([Out] out uint pcDevices);
    [PreserveSig] int Item([In] uint nDevice, [Out] out IMMDevice ppDevice);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate([In, MarshalAs(UnmanagedType.LPStruct)] Guid refId, [In] uint dwClsCtx, [In] IntPtr pActivationParams, [Out, MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    [PreserveSig] int OpenPropertyStore([In] uint stgmAccess, [Out] out IPropertyStore ppProperties);
    [PreserveSig] int GetId([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
    [PreserveSig] int GetState([Out] out uint pdwState);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount([Out] out uint cProps);
    [PreserveSig] int GetAt([In] uint iProp, [Out] out PROPERTYKEY pKey);
    [PreserveSig] int GetValue([In] ref PROPERTYKEY key, [Out] out PROPVARIANT pv);
    [PreserveSig] int SetValue([In] ref PROPERTYKEY key, [In] ref PROPVARIANT pv);
    [PreserveSig] int Commit();
}

internal static class PropertyKeys
{
    internal static readonly PROPERTYKEY PKEY_Device_FriendlyName = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14
    };
}

internal static class NativeMethods
{
    [DllImport("ole32.dll", PreserveSig = true)]
    internal static extern int PropVariantClear(ref PROPVARIANT pvar);
}

// ──────────────────────────────────────────────
// Entry point
// ──────────────────────────────────────────────
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            // Parse --device, --lang arguments
            string? deviceName = null;
            var lang = Lang.En;

            // Auto-detect: if exe name contains "-zh", default to Chinese
            string? exeName = Environment.GetCommandLineArgs().FirstOrDefault();
            if (exeName is not null && (exeName.Contains("-zh", StringComparison.OrdinalIgnoreCase)
                || exeName.Contains("_zh", StringComparison.OrdinalIgnoreCase)))
            {
                lang = Lang.Zh;
            }

            var remaining = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--device" && i + 1 < args.Length)
                    deviceName = args[++i];
                else if (args[i] == "--lang" && i + 1 < args.Length)
                    lang = args[++i].StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? Lang.Zh : Lang.En;
                else
                    remaining.Add(args[i]);
            }

            string appName = Strings.WindowTitle(lang);

            // Headless mode: apply loudness setting and restart audio
            if (remaining.Count >= 2 && remaining[0] == "--apply")
            {
                if (!remaining[1].Equals("on", StringComparison.OrdinalIgnoreCase)
                    && !remaining[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(Strings.InvalidArg(lang, remaining[1]),
                        appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }

                bool enable = remaining[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                using var mgr = new DeviceManager(deviceName);
                var dev = mgr.FindTargetDevice();
                if (dev is null)
                {
                    MessageBox.Show(
                        deviceName is not null ? Strings.DeviceNotFound(lang, deviceName) : Strings.NoPlaybackDevice(lang),
                        appName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }

                mgr.SetEnabled(dev, enable);
                mgr.RestartAudioService();
                return 0;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(deviceName, lang));
            return 0;
        }
        catch (Exception ex)
        {
            string detail = $"{ex.GetType().FullName}: {ex.Message}";
            if (ex.StackTrace is string st)
                detail += $"\n\n{st}";
            MessageBox.Show(detail, "Loudness Equalizer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
