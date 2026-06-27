using System.Diagnostics;

namespace LoudnessEqualizer;

public sealed class MainForm : Form
{
    // ── State ──
    private readonly DeviceManager _deviceManager;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private DeviceManager.DeviceInfo? _deviceInfo;
    private DeviceManager.LoudnessState _currentState;
    private bool _isBusy;

    // ── Controls ──
    private readonly Label _statusLabel;
    private readonly Label _detailLabel;
    private readonly Button _toggleButton;

    public MainForm(string? deviceName = null)
    {
        _deviceManager = new DeviceManager(deviceName);

        // ── Window ──
        Text            = "Loudness Equalizer";
        ClientSize      = new Size(440, 190);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        Font            = new Font("Segoe UI", 9f);

        // ── Status label ──
        _statusLabel = new Label
        {
            Font      = new Font("Segoe UI", 15f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock      = DockStyle.Top,
            Height    = 50,
            Text      = "Detecting device..."
        };

        // ── Detail label ──
        _detailLabel = new Label
        {
            Font      = new Font("Segoe UI", 9f),
            ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock      = DockStyle.Top,
            Height    = 28,
            Text      = ""
        };

        // ── Button panel ──
        var buttonPanel = new Panel { Dock = DockStyle.Fill, Height = 50 };

        _toggleButton = new Button
        {
            Size      = new Size(130, 36),
            Text      = "Toggle",
            Enabled   = false,
            FlatStyle = FlatStyle.System
        };
        _toggleButton.Click += ToggleButton_Click;

        buttonPanel.Resize += (_, _) =>
        {
            int x = (buttonPanel.ClientSize.Width - _toggleButton.Width) / 2;
            int y = (buttonPanel.ClientSize.Height - _toggleButton.Height) / 2;
            _toggleButton.Location = new Point(x, y);
        };
        buttonPanel.Controls.Add(_toggleButton);

        // ── Add to form ──
        Controls.Add(buttonPanel);
        Controls.Add(_detailLabel);
        Controls.Add(_statusLabel);

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
    // Toggle (async — no DoEvents, no Thread.Sleep)
    // ──────────────────────────────────────────
    private async void ToggleButton_Click(object? sender, EventArgs e)
    {
        if (_deviceInfo is null) { RefreshState(); return; }
        if (_isBusy) return;

        bool targetOn = _currentState != DeviceManager.LoudnessState.On;
        string verb   = targetOn ? "on" : "off";
        _isBusy = true;
        SetBusy(true, targetOn ? "Enabling Loudness Equalization..." : "Disabling Loudness Equalization...");

        try
        {
            // ── Path A: try direct write (works when process is already elevated) ──
            _deviceManager.SetEnabled(_deviceInfo, targetOn);
            _detailLabel.Text = "Restarting audio service to apply changes...";
            _deviceManager.RestartAudioService();
            await Task.Delay(500);
            RefreshState();
        }
        catch (UnauthorizedAccessException)
        {
            // ── Path B: self-elevate via UAC, retry in a child process ──
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

                // Child process shows its own error MessageBox on failure.
                await Task.Delay(1000);
                RefreshState();
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException)
            {
                ShowErrorAndRecover("Administrator privileges are required to modify audio settings.\n\n" +
                    "Right-click the application and select 'Run as administrator', or click 'Yes' in the UAC prompt.");
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
        try
        {
            _deviceInfo = _deviceManager.FindTargetDevice();

            if (_deviceInfo is null)
            {
                SetUI("Device not detected", Color.FromArgb(180, 85, 20),
                      "Please connect your audio device", "Refresh", enabled: true);
                _refreshTimer.Interval = 5000;
                return;
            }

            _currentState = _deviceManager.GetState(_deviceInfo);

            switch (_currentState)
            {
                case DeviceManager.LoudnessState.On:
                    SetUI("● Loudness Equalization: ON", Color.FromArgb(22, 130, 80),
                          $"Device: {_deviceInfo.FriendlyName}", "Disable", enabled: true);
                    break;
                case DeviceManager.LoudnessState.Off:
                    SetUI("○ Loudness Equalization: OFF", Color.DimGray,
                          $"Device: {_deviceInfo.FriendlyName}", "Enable", enabled: true);
                    break;
                default:
                    SetUI("State unknown", Color.FromArgb(180, 80, 20),
                          $"Device: {_deviceInfo.FriendlyName} (no FxProperties found)", "Retry", enabled: true);
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
    }

    private void SetBusy(bool busy, string? detailText)
    {
        _toggleButton.Enabled = !busy;
        if (detailText is not null)
            _detailLabel.Text = detailText;
    }
}
