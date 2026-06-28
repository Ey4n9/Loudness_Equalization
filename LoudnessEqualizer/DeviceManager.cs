using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Win32;
using System.Text;

namespace LoudnessEqualizer;

internal sealed class DeviceManager : IDisposable
{
    // ── Registry constants ──
    private const string RenderRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";

    // PKEY_FX_StreamEffectClsid — property key for SFX APO registration.
    // Numbered slots: ,1=PreMix, ,2=PostMix, ,3=UI page, ,5=Stream, ,6=Mode.
    // We only write ,1/,2/,3 to avoid overwriting driver-specific ,5/,6/,7.
    private const string FxStreamEffectKey = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}";

    // CLSIDs for the Windows built-in Loudness Equalization APO stack
    private const string LoudnessApoClsId    = "{62dc1a93-ae24-464c-a43e-452f824c4250}"; // PreMix SFX
    private const string LoudnessEffectClsId = "{637c490d-eee3-4c0a-973f-371958802da2}";  // PostMix SFX
    private const string LoudnessPageClsId   = "{5860E1C5-F95C-4a7a-8EC8-8AEF24F379A1}"; // Property page

    private const string LoudnessEnabledValue = "{fc52a749-4be9-4510-896e-966ba6525980},3";
    private const string ReleaseTimeValue     = "{9c00eeed-edce-4cd8-ae08-cb05e8ef57a0},3";

    // Slot value names under FxStreamEffectKey
    private static string FxSlot(int n) => FxStreamEffectKey + "," + n;

    private static readonly byte[] DefaultEnabledBytes = new byte[]
    {
        0x0b, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0xff, 0xff, 0x00, 0x00
    };

    private static readonly byte[] DefaultDisabledBytes = new byte[]
    {
        0x0b, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    private static readonly byte[] DefaultReleaseTimeBytes = new byte[]
    {
        0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00
    };

    /// <summary>
    /// Device name keywords that indicate a digital-only output where
    /// loudness equalization is not applicable.  Case-insensitive match.
    /// </summary>
    private static readonly string[] DigitalOnlyKeywords =
    {
        "HDMI", "S/PDIF", "SPDIF", "Digital", "TOSLINK", "DisplayPort",
        "NVIDIA Output", "NVIDIA HDMI",
        "数字音频"   // Chinese: "Digital Audio"
    };

    /// <summary>
    /// Returns true for digital-only outputs (HDMI, S/PDIF, etc.) that
    /// don't benefit from loudness equalization.
    /// </summary>
    public static bool IsDigitalOnly(string friendlyName)
    {
        foreach (string kw in DigitalOnlyKeywords)
            if (friendlyName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    // ── COM ──
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly string _targetDeviceName;
    private bool _disposed;

    public DeviceManager(string? targetDeviceName = null)
    {
        _targetDeviceName = targetDeviceName ?? "";
        var type = Type.GetTypeFromCLSID(Clsid.MMDeviceEnumerator)
                   ?? throw new InvalidOperationException("MMDeviceEnumerator COM class not registered.");
        _enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Marshal.ReleaseComObject(_enumerator);
    }

    // ──────────────────────────────────────────
    // Public types
    // ──────────────────────────────────────────
    public enum LoudnessState { Unknown, On, Off }

    public sealed class DeviceInfo
    {
        public required string DeviceId;
        public required string FriendlyName;
        public required string RegistryPath;   // HKLM\...\Render\{guid}
    }

    // ──────────────────────────────────────────
    // 1. List all playback devices
    // ──────────────────────────────────────────
    public List<DeviceInfo> ListAllDevices()
    {
        var devices = new List<DeviceInfo>();

        using RegistryKey? renderRoot = Registry.LocalMachine.OpenSubKey(RenderRoot, false);
        if (renderRoot is null) return devices;

        foreach (string id in renderRoot.GetSubKeyNames())
        {
            using RegistryKey? deviceKey = renderRoot.OpenSubKey(id, false);
            using RegistryKey? props = deviceKey?.OpenSubKey("Properties", false);
            if (deviceKey is null || props is null) continue;

            string? name = ReadRegistryString(props, "{a45c254e-df1c-4efd-8020-67d146a850e0},2");
            if (name is not null)
            {
                devices.Add(new DeviceInfo
                {
                    DeviceId     = id,
                    FriendlyName = name,
                    RegistryPath = RenderRoot + "\\" + id
                });
            }
        }

        return devices;
    }

    // ──────────────────────────────────────────
    // 2. Find device — COM enumeration for GUID
    // ──────────────────────────────────────────
    public DeviceInfo? FindTargetDevice()
    {
        // Try registry first — gives us the correct GUID with braces
        var regResult = FindViaRegistry();
        if (regResult is not null)
            return regResult;

        // Fallback: COM enumeration
        return FindViaCom();
    }

    private DeviceInfo? FindViaCom()
    {
        const uint DEVICE_STATE_ACTIVE = 0x00000001;
        const int S_OK = 0;

        int hr = _enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE_ACTIVE,
                    out IMMDeviceCollection? collection);
        if (hr != S_OK || collection is null) return null;

        try
        {
            hr = collection.GetCount(out uint count);
            if (hr != S_OK || count == 0) return null;

            for (uint i = 0; i < count; i++)
            {
                hr = collection.Item(i, out IMMDevice device);
                if (hr != S_OK || device is null) continue;

                string? name = null;
                string? devId = null;
                try
                {
                    // Read friendly name
                    hr = device.OpenPropertyStore(0 /* STGM_READ */, out IPropertyStore? store);
                    if (hr == S_OK && store is not null)
                    {
                        try
                        {
                            var key = PropertyKeys.PKEY_Device_FriendlyName;
                            hr = store.GetValue(ref key, out PROPVARIANT pv);
                            if (hr == S_OK && pv.vt == 31 /* VT_LPWSTR */)
                            {
                                name = Marshal.PtrToStringUni(pv.ptrVal);
                                NativeMethods.PropVariantClear(ref pv);
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(store);
                        }
                    }

                    // Check HRESULT — GetId failure means we can't build RegistryPath
                    hr = device.GetId(out devId);
                    if (hr != S_OK)
                        continue;

                    if (name is not null && name.Contains(_targetDeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        return new DeviceInfo
                        {
                            DeviceId     = devId ?? "",
                            FriendlyName = name,
                            RegistryPath = RenderRoot + "\\" + ExtractGuid(devId)
                        };
                    }
                }
                finally
                {
                    // Always release the device COM pointer — every Item() adds a ref
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }

        return null;
    }

    private DeviceInfo? FindViaRegistry()
    {
        using RegistryKey? renderRoot = Registry.LocalMachine.OpenSubKey(RenderRoot, false);
        if (renderRoot is null) return null;

        foreach (string id in renderRoot.GetSubKeyNames())
        {
            using RegistryKey? deviceKey = renderRoot.OpenSubKey(id, false);
            using RegistryKey? props = deviceKey?.OpenSubKey("Properties", false);
            if (deviceKey is null || props is null) continue;

            // Read friendly name: {a45c254e-df1c-4efd-8020-67d146a850e0},2
            string? name = ReadRegistryString(props, "{a45c254e-df1c-4efd-8020-67d146a850e0},2");
            if (name is not null && name.Contains(_targetDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                return new DeviceInfo
                {
                    DeviceId     = id,
                    FriendlyName = name,
                    RegistryPath = RenderRoot + "\\" + id
                };
            }
        }

        return null;
    }

    // ──────────────────────────────────────────
    // 3. Read state from FxProperties registry
    //    (no admin needed — HKLM read is public)
    // ──────────────────────────────────────────
    public LoudnessState GetState(DeviceInfo deviceInfo)
    {
        foreach (string path in GetFxCandidatePaths(deviceInfo))
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path, false);
            if (key is null) continue;

            // Primary: check the loudness enabled binary value
            byte[]? bytes = key.GetValue(LoudnessEnabledValue) as byte[];
            if (bytes is not null && bytes.Length >= 10
                && bytes[0] == 0x0b && bytes[1] == 0x00 && bytes[2] == 0x00 && bytes[3] == 0x00
                && bytes[4] == 0x01 && bytes[5] == 0x00 && bytes[6] == 0x00 && bytes[7] == 0x00)
                return (bytes[8] == 0xff && bytes[9] == 0xff) ? LoudnessState.On : LoudnessState.Off;

            // Fallback: check if any SFX slot has the LEQ APO CLSID registered.
            // A non-null LEQ CLSID in any slot means the APO is loaded → ON.
            foreach (int n in new[] { 1, 2, 5 })
            {
                string? slotClsid = ReadRegistryString(key, FxSlot(n));
                if (slotClsid is not null
                    && slotClsid.Equals(LoudnessApoClsId, StringComparison.OrdinalIgnoreCase))
                    return LoudnessState.On;
            }
        }

        return LoudnessState.Unknown;
    }

    // ──────────────────────────────────────────
    // 4. Write state to FxProperties registry
    //    Uses REG_OPTION_BACKUP_RESTORE to bypass
    //    the SYSTEM-owned ACL without changing it.
    //    All writes go through native RegSetValueEx
    //    on the privilege-backed handle — no ACL
    //    modification needed.
    // ──────────────────────────────────────────
    public void SetEnabled(DeviceInfo deviceInfo, bool enable)
    {
        // APO CLSIDs MUST be written to FxProperties root — the audio
        // engine only reads SFX slot assignments from the root key.
        string fxRoot = deviceInfo.RegistryPath + @"\FxProperties";
        List<string> targets = GetFxCandidatePaths(deviceInfo);

        // Always include the root; deduplicate
        if (!targets.Contains(fxRoot, StringComparer.OrdinalIgnoreCase))
            targets.Insert(0, fxRoot);

        if (targets.Count == 0)
        {
            // No FxProperties exists at all — create from scratch.
            targets.Add(fxRoot);
        }

        // Ensure backup/restore privileges are enabled
        EnableBackupPrivileges();

        // For each target, ensure the key chain exists (parent → child),
        // then write values via native API on the privilege-backed handle.
        int writeCount = 0;
        List<string> errors = new();

        foreach (string path in targets)
        {
            try
            {
                EnsureKeyChain(deviceInfo.RegistryPath, path);
                WriteLoudnessValues(path, enable);
                writeCount++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{path}: {ex.GetType().Name} — {ex.Message}");
            }
        }

        if (writeCount == 0)
        {
            string detail = errors.Count > 0
                ? $"  Details:\n  • {string.Join("\n  • ", errors)}"
                : "";
            throw new UnauthorizedAccessException(
                $"Unable to write audio enhancement settings.{detail}");
        }
    }

    // ──────────────────────────────────────────
    // Registry native-API helpers
    //   Instead of modifying ACLs (save→grant→
    //   write→restore), we open/create keys with
    //   REG_OPTION_BACKUP_RESTORE and write
    //   directly via RegSetValueEx.  The ACL is
    //   never touched.
    // ──────────────────────────────────────────

    private const int TOKEN_QUERY             = 0x0008;
    private const int TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const int SE_PRIVILEGE_ENABLED    = 0x0002;
    private const int ERROR_SUCCESS           = 0;
    private const int ERROR_NOT_ALL_ASSIGNED  = 0x0514;

    private const uint REG_OPTION_BACKUP_RESTORE = 0x0004;
    private const uint KEY_READ                  = 0x20019;
    private const uint KEY_SET_VALUE             = 0x0002;
    private const uint REG_SZ                    = 1;
    private const uint REG_BINARY                = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr hProcess, uint desiredAccess, out IntPtr hToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr hToken, bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState, uint len, IntPtr prevState, IntPtr returnLen);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, uint ulOptions,
        uint samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(IntPtr hKey);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegCreateKeyEx(IntPtr hKey, string lpSubKey,
        uint Reserved, IntPtr lpClass, uint dwOptions, uint samDesired,
        IntPtr lpSecurityAttributes, out IntPtr phkResult, out uint lpdwDisposition);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegSetValueEx(IntPtr hKey, string? lpValueName,
        uint Reserved, uint dwType, byte[] lpData, uint cbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegQueryValueEx(IntPtr hKey, string? lpValueName,
        IntPtr lpReserved, out uint lpType, IntPtr lpData, ref uint lpcbData);

    private static readonly IntPtr HKEY_LOCAL_MACHINE = new(unchecked((int)0x80000002));

    /// <summary>
    /// Enable SeRestorePrivilege + SeBackupPrivilege so that
    /// RegOpenKeyEx/RegCreateKeyEx with REG_OPTION_BACKUP_RESTORE
    /// can bypass the restrictive SYSTEM-owned ACL on FxProperties keys.
    /// </summary>
    private static void EnableBackupPrivileges()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_ADJUST_PRIVILEGES, out IntPtr hToken))
            throw new UnauthorizedAccessException(
                $"Failed to open process token (OpenProcessToken error {Marshal.GetLastWin32Error()}).");

        try
        {
            if (!LookupPrivilegeValue(null, "SeRestorePrivilege", out LUID luid))
                throw new UnauthorizedAccessException(
                    $"Failed to look up SeRestorePrivilege (error {Marshal.GetLastWin32Error()}).");

            uint sz = (uint)Marshal.SizeOf<TOKEN_PRIVILEGES>();

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid           = luid,
                Attributes     = SE_PRIVILEGE_ENABLED
            };

            if (!AdjustTokenPrivileges(hToken, false, ref tp, sz, IntPtr.Zero, IntPtr.Zero))
            {
                int err = Marshal.GetLastWin32Error();
                throw new UnauthorizedAccessException(
                    $"Failed to enable SeRestorePrivilege (AdjustTokenPrivileges error {err}).");
            }

            // AdjustTokenPrivileges returns TRUE even when the privilege wasn't
            // actually assigned.  Check GetLastError for ERROR_NOT_ALL_ASSIGNED.
            int lastErr = Marshal.GetLastWin32Error();
            if (lastErr == ERROR_NOT_ALL_ASSIGNED)
                throw new UnauthorizedAccessException(
                    "SeRestorePrivilege is not assigned to this process token. Please run as Administrator.");

            // Also enable SeBackupPrivilege — REG_OPTION_BACKUP_RESTORE needs both
            if (LookupPrivilegeValue(null, "SeBackupPrivilege", out LUID backupLuid))
            {
                tp.Luid = backupLuid;
                if (!AdjustTokenPrivileges(hToken, false, ref tp, sz, IntPtr.Zero, IntPtr.Zero))
                {
                    Debug.WriteLine($"AdjustTokenPrivileges for SeBackupPrivilege failed (error {Marshal.GetLastWin32Error()}). Continue with SeRestorePrivilege only.");
                }
            }
        }
        finally
        {
            CloseHandle(hToken);
        }
    }

    /// <summary>
    /// Ensure the full registry key path exists by creating intermediate
    /// keys from the device's Render key down to the target path.
    /// Each level is created with REG_OPTION_BACKUP_RESTORE to bypass
    /// the restrictive ACL.
    /// </summary>
    private static void EnsureKeyChain(string renderPath, string targetPath)
    {
        // targetPath is e.g. HKLM\SOFTWARE\...\Render\{guid}\FxProperties\{clsid}\User
        // renderPath   is e.g. HKLM\SOFTWARE\...\Render\{guid}
        // The renderPath already exists (created by Windows audio subsystem).
        // We need to ensure each level below it exists.

        if (!targetPath.StartsWith(renderPath, StringComparison.OrdinalIgnoreCase))
            return; // shouldn't happen — safety guard

        string relative = targetPath.Substring(renderPath.Length).TrimStart('\\');
        string[] parts = relative.Split('\\');
        string current = renderPath;

        foreach (string part in parts)
        {
            current += "\\" + part;
            using (OpenOrCreateKey(current)) { /* just ensuring existence */ }
        }
    }

    /// <summary>
    /// Open or create a registry key with REG_OPTION_BACKUP_RESTORE.
    /// Returns a handle that must be closed via RegCloseKey (wrapped in
    /// a disposable helper via the using pattern).
    /// </summary>
    private static SafeRegHandle OpenOrCreateKey(string fullPath)
    {
        int hr = RegOpenKeyEx(HKEY_LOCAL_MACHINE, fullPath,
            REG_OPTION_BACKUP_RESTORE,
            KEY_READ | KEY_SET_VALUE,
            out IntPtr hKey);

        if (hr == ERROR_SUCCESS)
            return new SafeRegHandle(hKey);

        if (hr == 2) // ERROR_FILE_NOT_FOUND
        {
            hr = RegCreateKeyEx(HKEY_LOCAL_MACHINE, fullPath,
                0, IntPtr.Zero, REG_OPTION_BACKUP_RESTORE,
                KEY_READ | KEY_SET_VALUE,
                IntPtr.Zero, out hKey, out _);

            if (hr == ERROR_SUCCESS)
                return new SafeRegHandle(hKey);
        }

        throw new IOException($"Failed to open or create registry key {fullPath} (error 0x{hr:x}).");
    }

    /// <summary>
    /// Minimal IDisposable wrapper around a native registry handle,
    /// for use with `using` blocks.
    /// </summary>
    private sealed class SafeRegHandle : IDisposable
    {
        private IntPtr _handle;
        public SafeRegHandle(IntPtr handle) => _handle = handle;
        public IntPtr Handle => _handle;
        public void Dispose() { if (_handle != IntPtr.Zero) { RegCloseKey(_handle); _handle = IntPtr.Zero; } }
    }

    /// <summary>
    /// Write loudness equalization APO CLSIDs and per-property values to
    /// a registry key.  This registers (or unregisters) the Windows
    /// built-in Loudness Equalization SFX APO on the target device.
    ///
    /// Slot layout under PKEY_FX_StreamEffectClsid ({d04e05a6...}):
    ///   ,1 = PreMix  → LEQ APO ({62dc1a93...})
    ///   ,2 = PostMix → LEQ effect ({637c490d...})
    ///   ,3 = UI page → LEQ property page ({5860E1C5...})
    ///   ,5/,6/,7  — driver-specific, we never touch these
    /// </summary>
    private static void WriteLoudnessValues(string fullPath, bool enable)
    {
        using SafeRegHandle hKey = OpenOrCreateKey(fullPath);
        using RegistryKey? readKey = Registry.LocalMachine.OpenSubKey(fullPath, false);

        string nullGuid = "{00000000-0000-0000-0000-000000000000}";

        // 1. Register (or unregister) the LEQ APO in PreMix / PostMix slots.
        //    These are the slots the Windows audio engine processes BEFORE
        //    driver-specific slots ,5+, so LEQ runs regardless of driver.
        WriteRegString(hKey, FxSlot(1), enable ? LoudnessApoClsId : nullGuid);
        WriteRegString(hKey, FxSlot(2), enable ? LoudnessEffectClsId : nullGuid);

        // 2. Property page CLSID — only write when enabling (don't destroy
        //    driver's property page on disable, user might want it back).
        if (enable)
            WriteRegString(hKey, FxSlot(3), LoudnessPageClsId);

        // 3. Loudness enabled binary value (always)
        byte[]? existing = readKey?.GetValue(LoudnessEnabledValue) as byte[];
        byte[] patched = PatchEnabledBytes(existing, enable);
        int hr = RegSetValueEx(hKey.Handle, LoudnessEnabledValue, 0, REG_BINARY,
            patched, (uint)patched.Length);
        if (hr != ERROR_SUCCESS)
            throw new IOException($"Failed to write {LoudnessEnabledValue} (error 0x{hr:x}).");

        // 4. Release Time (write once — never overwrite user preference)
        if (readKey?.GetValue(ReleaseTimeValue) is null)
        {
            int hr2 = RegSetValueEx(hKey.Handle, ReleaseTimeValue, 0, REG_BINARY,
                DefaultReleaseTimeBytes, (uint)DefaultReleaseTimeBytes.Length);
            if (hr2 != ERROR_SUCCESS)
                throw new IOException($"Failed to write {ReleaseTimeValue} (error 0x{hr2:x}).");
        }
    }

    /// <summary>Write a REG_SZ value via the native privilege-backed handle.</summary>
    private static void WriteRegString(SafeRegHandle hKey, string valueName, string data)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(data + "\0");
        int hr = RegSetValueEx(hKey.Handle, valueName, 0, REG_SZ, bytes, (uint)bytes.Length);
        if (hr != ERROR_SUCCESS)
            throw new IOException($"Failed to write {valueName} (error 0x{hr:x}).");
    }

    // ──────────────────────────────────────────
    // 5. Restart Windows Audio service to apply
    //    FxProperties changes.  The audio engine
    //    only re-reads the registry on startup.
    // ──────────────────────────────────────────
    public void RestartAudioService()
    {
        var psi = new ProcessStartInfo
        {
            FileName         = "powershell.exe",
            Arguments        = "-NoProfile -ExecutionPolicy Bypass -Command \"Restart-Service audiosrv -Force\"",
            UseShellExecute  = false,
            CreateNoWindow   = true,
            WindowStyle      = ProcessWindowStyle.Hidden
        };

        using var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Settings written, but failed to launch the audio service restart process.");

        bool exited = process.WaitForExit(30000);

        if (!exited)
        {
            // Timed out — kill the orphan and report failure
            try { process.Kill(); } catch { /* best-effort */ }
            throw new InvalidOperationException("Settings written, but the Windows Audio service restart timed out. Try reconnecting your audio device and try again.");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Settings written, but the Windows Audio service restart failed (exit code {process.ExitCode}). Try reconnecting your audio device and try again.");
    }

    // ──────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────
    private static List<string> GetFxCandidatePaths(DeviceInfo deviceInfo)
    {
        string fxRoot = deviceInfo.RegistryPath + @"\FxProperties";
        var targets = new List<string>();

        using RegistryKey? root = Registry.LocalMachine.OpenSubKey(fxRoot, false);
        if (root is null) return targets;

        // Check FxProperties itself
        if (HasRelevantValue(root))
            targets.Add(fxRoot);

        // Check FxProperties\{child}\{User, Default, Volatile}
        foreach (string childName in root.GetSubKeyNames())
        {
            string childPath = fxRoot + "\\" + childName;
            foreach (string leafName in new[] { "User", "Default", "Volatile" })
            {
                string leafPath = childPath + "\\" + leafName;
                using RegistryKey? leaf = Registry.LocalMachine.OpenSubKey(leafPath, false);
                if (leaf is not null && HasRelevantValue(leaf))
                    targets.Add(leafPath);
            }
        }

        // Fallback: add any FxProperties\{child}\User paths
        if (targets.Count == 0)
        {
            foreach (string childName in root.GetSubKeyNames())
            {
                string userPath = fxRoot + "\\" + childName + "\\User";
                using RegistryKey? user = Registry.LocalMachine.OpenSubKey(userPath, false);
                if (user is not null)
                    targets.Add(userPath);
            }
        }

        // Prefer "User" paths, avoid "Volatile"
        return targets
            .Where(x => x.EndsWith("\\User", StringComparison.OrdinalIgnoreCase)
                        || !x.EndsWith("\\Volatile", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool HasRelevantValue(RegistryKey key)
    {
        return key.GetValue(FxSlot(1)) is not null
            || key.GetValue(LoudnessEnabledValue) is not null
            || key.GetValue(ReleaseTimeValue) is not null;
    }

    private static byte[] PatchEnabledBytes(byte[]? existing, bool enable)
    {
        byte[] bytes = (existing is not null && existing.Length >= 10)
            ? (byte[])existing.Clone()
            : (byte[])(enable ? DefaultEnabledBytes : DefaultDisabledBytes).Clone();

        bytes[8] = enable ? (byte)0xff : (byte)0x00;
        bytes[9] = enable ? (byte)0xff : (byte)0x00;
        return bytes;
    }

    private static string? ReadRegistryString(RegistryKey key, string valueName)
    {
        object? value = key.GetValue(valueName);
        if (value is byte[] bytes)
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        if (value is string s)
            return s;
        return null;
    }

    private static string ExtractGuid(string? deviceId)
    {
        // deviceId format: "{0.0.0.00000000}.{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}"
        // Registry keys include braces: {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}
        if (string.IsNullOrEmpty(deviceId)) return "";
        int dot = deviceId.LastIndexOf('.');
        return dot >= 0 ? deviceId.Substring(dot + 1) : deviceId;
    }
}
