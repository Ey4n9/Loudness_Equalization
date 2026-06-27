using System.Diagnostics;

namespace LoudnessEqualizer;

public sealed class MainForm : Form
{
    // ── State ──
    private readonly DeviceManager _deviceManager;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private List<DeviceManager.DeviceInfo> _allDevices = new();
    private DeviceManager.DeviceInfo? _deviceInfo;
    private DeviceManager.LoudnessState _currentState;
    private bool _isBusy;
    private bool _suppressComboEvent;

    // ── Controls ──
    private readonly ComboBox _deviceCombo;
    private readonly Label _statusLabel;
    private readonly Label _detailLabel;
    private readonly Button _toggleButton;

    // ── Layout constants ──
    private const int FormW = 480;
    private const int FormH = 180;
    private const int PadX  = 30;

    public MainForm(string? deviceName = null)
    {
        _deviceManager = new DeviceManager(deviceName);

        // ── Window ──
        Text            = "Loudness Equalizer";
        ClientSize      = new Size(FormW, FormH);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = Color.White;
        Font            = new Font("Segoe UI", 9f);

        // ── Status label ──
        _statusLabel = new Label
        {
            Font      = new Font("Segoe UI", 14f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(50, 50, 50),
            Size      = new Size(FormW - PadX * 2, 42),
            Location  = new Point(PadX, 16),
            Text      = "Detecting device..."
        };

        // ── Detail label ──
        _detailLabel = new Label
        {
            Font      = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(140, 140, 140),
            TextAlign = ContentAlignment.MiddleCenter,
            Size      = new Size(FormW - PadX * 2, 22),
            Location  = new Point(PadX, 58),
            Text      = ""
        };

        // ── Control bar panel (combo + button side by side) ──
        var barPanel = new Panel
        {
            BackColor = Color.White,
            Size      = new Size(380, 42),
            Location  = new Point((FormW - 380) / 2, 100)
        };

        _deviceCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font          = new Font("Segoe UI", 9f),
            Size          = new Size(220, 28),
            Location      = new Point(0, 7)  // vertically centered in 42px panel
        };
        _deviceCombo.SelectedIndexChanged += DeviceCombo_SelectedIndexChanged;

        _toggleButton = new Button
        {
            Size      = new Size(130, 38),
            Text      = "Toggle",
            Enabled   = false,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 10f, FontStyle.Bold),
            Location  = new Point(250, 2)  // 220 + 30 gap, v-centered in 42px panel
        };
        _toggleButton.Click += ToggleButton_Click;
        _toggleButton.FlatAppearance.BorderSize = 0;

        barPanel.Controls.Add(_deviceCombo);
        barPanel.Controls.Add(_toggleButton);

        // ── Add to form ──
        Controls.Add(_statusLabel);
        Controls.Add(_detailLabel);
        Controls.Add(barPanel);

        // ── Timer ──
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _refreshTimer.Tick += (_, _) => { if (!_isBusy) RefreshState(); };
        _refreshTimer.Start();

        // ── Prevent closing during toggle ──
        FormClosing += (_, e) =>
        {
            if (_isBusy)
            {
                e.Cancel = true;
                MessageBox.Show(this, "An operation is in progress. Please wait...",
                    "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

        PopulateDeviceList(deviceName);
        RefreshState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _deviceManager.Dispose();
        }
        base.Dispose(disposing);
    }

    // ──────────────────────────────────────────
    // Device list
    // ──────────────────────────────────────────
    private void PopulateDeviceList(string? preferredDeviceName)
    {
        _suppressComboEvent = true;

        _allDevices = _deviceManager.ListAllDevices();
        _deviceCombo.Items.Clear();

        int selectIndex = -1;

        for (int i = 0; i < _allDevices.Count; i++)
        {
            var dev = _allDevices[i];
            _deviceCombo.Items.Add(dev.FriendlyName);

            if (selectIndex < 0 && preferredDeviceName is not null
                && dev.FriendlyName.Contains(preferredDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                selectIndex = i;
            }
        }

        if (_allDevices.Count == 0)
        {
            _deviceCombo.Items.Add("(no devices found)");
            _deviceCombo.SelectedIndex = 0;
            _deviceCombo.Enabled = false;
            _deviceInfo = null;
        }
        else
        {
            if (selectIndex < 0) selectIndex = 0;
            _deviceCombo.SelectedIndex = selectIndex;
            _deviceInfo = _allDevices[selectIndex];
            _deviceCombo.Enabled = true;
        }

        _suppressComboEvent = false;
    }

    private void DeviceCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressComboEvent) return;

        int idx = _deviceCombo.SelectedIndex;
        if (idx >= 0 && idx < _allDevices.Count)
            _deviceInfo = _allDevices[idx];

        if (!_isBusy) RefreshState();
    }

    // ──────────────────────────────────────────
    // Toggle (async)
    // ──────────────────────────────────────────
    private async void ToggleButton_Click(object? sender, EventArgs e)
    {
        if (_deviceInfo is null) return;
        if (_isBusy) return;

        bool targetOn = _currentState != DeviceManager.LoudnessState.On;
        string verb   = targetOn ? "on" : "off";
        _isBusy = true;
        SetBusy(true, targetOn ? "Enabling Loudness Equalization..." : "Disabling Loudness Equalization...");

        try
        {
            _deviceManager.SetEnabled(_deviceInfo, targetOn);
            _detailLabel.Text = "Restarting audio service to apply changes...";
            _deviceManager.RestartAudioService();
            await Task.Delay(500);
            RefreshState();
        }
        catch (UnauthorizedAccessException)
        {
            _detailLabel.Text = "Requesting administrator privileges...";

            var psi = new ProcessStartInfo
            {
                FileName        = Application.ExecutablePath,
                Arguments       = $"--apply {verb}",
                UseShellExecute = true,
                Verb            = "runas"
            };

            try
            {
                using var process = Process.Start(psi);
                if (process is null)
                {
                    ShowErrorAndRecover("Failed to launch elevated process.");
                    return;
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    ShowErrorAndRecover("Elevated process did not complete within 30 seconds.");
                    return;
                }

                await Task.Delay(1000);
                RefreshState();
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException)
            {
                ShowErrorAndRecover("Administrator privileges are required.\n\n" +
                    "Right-click and Run as administrator, or accept the UAC prompt.");
            }
        }
        catch (Exception ex)
        {
            ShowErrorAndRecover(ex.Message);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void ShowErrorAndRecover(string message)
    {
        MessageBox.Show(this, message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        SetBusy(false, null);
    }

    // ──────────────────────────────────────────
    // Refresh state
    // ──────────────────────────────────────────
    private void RefreshState()
    {
        if (_deviceInfo is null)
        {
            SetUI("No device selected", Color.FromArgb(180, 85, 20),
                  "Select a device from the list", "Refresh", enabled: true);
            _refreshTimer.Interval = 5000;
            return;
        }

        try
        {
            _currentState = _deviceManager.GetState(_deviceInfo);

            switch (_currentState)
            {
                case DeviceManager.LoudnessState.On:
                    SetUI("● Loudness Equalization: ON", Color.FromArgb(34, 139, 34),
                          $"{_deviceInfo.FriendlyName}", "Disable", enabled: true);
                    break;
                case DeviceManager.LoudnessState.Off:
                    SetUI("○ Loudness Equalization: OFF", Color.FromArgb(120, 120, 120),
                          $"{_deviceInfo.FriendlyName}", "Enable", enabled: true);
                    break;
                default:
                    SetUI("State unknown", Color.FromArgb(200, 120, 30),
                          $"{_deviceInfo.FriendlyName} (no FxProperties found)", "Retry", enabled: true);
                    break;
            }

            _refreshTimer.Interval = 3000;
        }
        catch (Exception ex)
        {
            SetUI("Error", Color.Red, ex.Message, "Retry", enabled: true);
        }
    }

    // ──────────────────────────────────────────
    // UI helpers
    // ──────────────────────────────────────────
    private void SetUI(string status, Color color, string detail, string buttonText, bool enabled)
    {
        _statusLabel.Text      = status;
        _statusLabel.ForeColor = color;
        _detailLabel.Text      = detail;
        _toggleButton.Text     = buttonText;
        _toggleButton.Enabled  = enabled;

        // Button styling based on state
        if (buttonText == "Enable")
        {
            _toggleButton.BackColor  = Color.FromArgb(34, 139, 34);
            _toggleButton.ForeColor  = Color.White;
        }
        else if (buttonText == "Disable")
        {
            _toggleButton.BackColor  = Color.FromArgb(200, 60, 60);
            _toggleButton.ForeColor  = Color.White;
        }
        else
        {
            _toggleButton.BackColor  = Color.FromArgb(66, 133, 244);
            _toggleButton.ForeColor  = Color.White;
        }
    }

    private void SetBusy(bool busy, string? detailText)
    {
        _toggleButton.Enabled = !busy;
        if (detailText is not null)
            _detailLabel.Text = detailText;
    }
}
