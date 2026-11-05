using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskbarMediaControls {
    public class TrayAppContext : ApplicationContext {
        private readonly NotifyIcon[] _trayIcons = new NotifyIcon[3];

        private readonly int[] _mediaKeys = { 0xB1, 0xB3, 0xB0 };
        private readonly string[] _tooltips = { "Previous Track", "Play / Pause", "Next Track" };


        private const string RegistryRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private const string RegistryApprovedKey =
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        private const string AppName = "TaskbarMediaControls";

        private bool _isPlaying;
        private bool _launchOnStartup;
        private bool _hidden;

        private readonly ContextMenuStrip _trayMenu;

        private NotifyIcon CreateNotifyIcon(IconType iconType, string tooltip, int index) {
            var icon = new NotifyIcon {
                Icon = IconManager.LoadIcon(iconType),
                Text = tooltip,
                Visible = true,
                ContextMenuStrip = _trayMenu
            };

            icon.MouseUp += (_, e) => {
                if (e.Button == MouseButtons.Left) {
                    SendMediaKey(_mediaKeys[index]);

                    if (index == 1)
                        TogglePlayPause();
                }
            };

            return icon;
        }

        public TrayAppContext() {
            string exePath = Path.GetFullPath(Application.ExecutablePath);
            _launchOnStartup = !StartupEntryExists() || IsStartupEnabled();
            
            _hidden = NotifyIconSettings.IsHidden(exePath);

            _trayMenu = BuildContextMenu();

            _trayIcons[2] = CreateNotifyIcon(IconType.Next, _tooltips[2], 2);
            _trayIcons[1] = CreateNotifyIcon(IconType.Play, _tooltips[1], 1);
            _trayIcons[0] = CreateNotifyIcon(IconType.Previous, _tooltips[0], 0);

            NotifyIconSettings.EnsureEntries(exePath, _tooltips, _hidden);
            Application.ApplicationExit += OnApplicationExit;

            if (_launchOnStartup)
                SetStartup(true);
            SystemEvents.UserPreferenceChanged += SystemEventsOnUserPreferenceChanged;
        }

        private void SystemEventsOnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) {
            if (e.Category == UserPreferenceCategory.General)
                RefreshIcons();
        }

        private void TogglePlayPause() {
            _isPlaying = !_isPlaying;

            _trayIcons[1].Icon?.Dispose();
            _trayIcons[1].Icon = IconManager.LoadIcon(_isPlaying ? IconType.Pause : IconType.Play);
            _trayIcons[1].Text = _isPlaying ? "Pause" : "Play";
        }

        private ContextMenuStrip BuildContextMenu() {
            var menu = new ContextMenuStrip();

            var startupItem = new ToolStripMenuItem("Launch on Startup") {
                Checked = _launchOnStartup,
                CheckOnClick = true
            };
            startupItem.CheckedChanged += (_, _) => {
                _launchOnStartup = startupItem.Checked;
                SetStartup(_launchOnStartup);
            };
            menu.Items.Add(startupItem);

            var hiddenItem = new ToolStripMenuItem("Hide from Taskbar") {
                Checked = _hidden,
                CheckOnClick = true
            };
            hiddenItem.CheckedChanged += (_, _) => {
                _hidden = hiddenItem.Checked;
                CheckAndUpdateRegistryValue(@"Control Panel\NotifyIconSettings");
            };
            menu.Items.Add(hiddenItem);

            var vlcHotkeysItem = new ToolStripMenuItem("Configure VLC Hotkeys") {
                Visible = VlcHotkeyManager.NeedsConfiguration
            };
            vlcHotkeysItem.Click += OnConfigureVlcHotkeys;
            menu.Items.Add(vlcHotkeysItem);
            menu.Opening += (_, _) => vlcHotkeysItem.Visible = VlcHotkeyManager.NeedsConfiguration;

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) => Application.Exit();
            menu.Items.Add(exitItem);

            return menu;
        }

        private void OnConfigureVlcHotkeys(object? sender, EventArgs e) {
            var item = (ToolStripMenuItem)sender!;

            if (!VlcHotkeyManager.IsVlcInstalled) {
                MessageBox.Show(
                    $"VLC's configuration file was not found:{Environment.NewLine}{VlcHotkeyManager.VlcrcPath}",
                    "Taskbar Media Controls", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string message =
                "This will set the global hotkeys VLC needs to respond to the tray buttons:" +
                Environment.NewLine + Environment.NewLine +
                "  global-key-play-pause = Media Play Pause" + Environment.NewLine +
                "  global-key-next       = Media Next Track" + Environment.NewLine +
                "  global-key-prev       = Media Prev Track" + Environment.NewLine + Environment.NewLine +
                "A backup is written to vlcrc.bak.";

            if (VlcHotkeyManager.IsVlcRunning())
                message += Environment.NewLine + "VLC is running and will be closed.";

            message += Environment.NewLine + Environment.NewLine + "Continue?";

            if (MessageBox.Show(message, "Configure VLC Hotkeys",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            try {
                VlcHotkeyManager.ApplyHotkeys();
            } catch (Exception ex) {
                MessageBox.Show(
                    $"Failed to update VLC's configuration:{Environment.NewLine}{ex.Message}",
                    "Taskbar Media Controls", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            bool configured = VlcHotkeyManager.AreHotkeysConfigured();
            item.Visible = !configured;

            MessageBox.Show(
                configured
                    ? "VLC hotkeys configured."
                    : "Attempted to update VLC's configuration, but the hotkeys still do not match.",
                "Taskbar Media Controls", MessageBoxButtons.OK,
                configured ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void OnApplicationExit(object? sender, EventArgs e) {
            foreach (var icon in _trayIcons) {
                icon.Visible = false;
                icon.Dispose();
            }
            
            SystemEvents.UserPreferenceChanged -= SystemEventsOnUserPreferenceChanged;
        }

        private void SetStartup(bool enable) {
            string exePath = Path.GetFullPath(Application.ExecutablePath);
            string value = $"\"{exePath}\"";

            try {
                // 1) Run key
                using var runKey = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true)
                                   ?? Registry.CurrentUser.CreateSubKey(RegistryRunKey);
                if (enable)
                    runKey.SetValue(AppName, value, RegistryValueKind.String);
                else
                    runKey.DeleteValue(AppName, false);

                // 2) Force StartupApproved to enabled
                using var approvedKey = Registry.CurrentUser.OpenSubKey(RegistryApprovedKey, true)
                                        ?? Registry.CurrentUser.CreateSubKey(RegistryApprovedKey);
                if (enable) {
                    byte[] enabledValue = new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                    approvedKey.SetValue(AppName, enabledValue, RegistryValueKind.Binary);
                } else {
                    approvedKey.DeleteValue(AppName, false);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Failed to set startup: {ex.Message}");
            }
        }

        private bool IsStartupEnabled() {
            try {
                using var runKey = Registry.CurrentUser.OpenSubKey(RegistryRunKey);
                if (runKey == null)
                    return false;

                var value = runKey.GetValue(AppName)?.ToString();
                if (string.IsNullOrWhiteSpace(value))
                    return false;

                value = value.Trim('"');
                string exePath = Path.GetFullPath(Application.ExecutablePath);

                return string.Equals(value, exePath, StringComparison.OrdinalIgnoreCase);
            } catch {
                return false;
            }
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

        private void SendMediaKey(int key) {
            keybd_event((byte)key, 0, 0, 0);
            keybd_event((byte)key, 0, 2, 0);
        }

        private void RefreshIcons() {
            _trayIcons[0].Icon = IconManager.LoadIcon(IconType.Previous);
            _trayIcons[1].Icon = IconManager.LoadIcon(_isPlaying ? IconType.Pause : IconType.Play);
            _trayIcons[2].Icon = IconManager.LoadIcon(IconType.Next);
        }
        private bool StartupEntryExists() {
            try {
                using var runKey = Registry.CurrentUser.OpenSubKey(RegistryRunKey);
                return runKey?.GetValue(AppName) != null;
            } catch { 
                return false;
            }
        }
        private void CheckAndUpdateRegistryValue(string registryPath) {
            const string propertyPromo = "IsPromoted";
            const string propertyExe = "ExecutablePath";
            string[] exeArr = ["TaskbarMediaControls.exe"];
            using (var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true)) {
                if (key != null) {
                    var propertyValue = key.GetValue(propertyPromo);
                    var exePath = key.GetValue(propertyExe);
                    if (exePath != null) {
                        exePath = exePath.ToString().Split('\\').Last().Trim();
                        if (exeArr.Contains(exePath)) {
                            if ((propertyValue == null || propertyValue.ToString() == "0") && !_hidden) {
                                // If the key doesn't exist, create it and set its value to 1
                                key.SetValue(propertyPromo, 1, RegistryValueKind.DWord);
                            }
                            if ((propertyValue == null || propertyValue.ToString() == "1") && _hidden) {
                                // If the key doesn't exist, create it and set its value to 0
                                key.SetValue(propertyPromo, 0, RegistryValueKind.DWord);
                            }
                        }
                    }

                    // Recursively check all subkeys
                    foreach (string subkeyName in key.GetSubKeyNames()) {
                        CheckAndUpdateRegistryValue(registryPath + "\\" + subkeyName);
                    }
                }
            }
        }
    }
}