
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using NAudio.CoreAudioApi;

namespace AudioOutputController;

public partial class Form1 : Form
{
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly List<MMDevice> devices = new();
    private readonly List<DeviceConfiguration> configurations = new();

    private FlowLayoutPanel mainPanel = null!;
    private FlowLayoutPanel globalShortcutPanel = null!;
    private Label status = null!;
    private VolumeOverlay volumeOverlay = null!;
    private ComboBox displayScreenCombo = null!;
    private ComboBox displayPositionCombo = null!;
    private ComboBox displayOrientationCombo = null!;
    private Panel displayMapPreview = null!;
    private Label displayInfoPreview = null!;
    private readonly Dictionary<string, RectangleF> displayHitRects = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Button> displayPositionButtons = new();
    private readonly List<Button> displayOrientationButtons = new();
    private NotifyIcon? trayIcon;
    private bool exitFromTrayMenu;
    private CheckBox startWithWindowsCheck = null!;
    private CheckBox showInTrayCheck = null!;
    private CheckBox minimizeToTrayCheck = null!;
    private ComboBox overlayDurationCombo = null!;
    private TrackBar overlaySizeTrack = null!;
    private Label overlaySizeValueLabel = null!;
    private TrackBar overlayOpacityTrack = null!;
    private TabPage audioRoutingTab = null!;
    private TabPage audioSettingTab = null!;
    private readonly Dictionary<string, Dictionary<string, string>> audioMappings = new(StringComparer.OrdinalIgnoreCase);
    private string AudioMappingsFile => Path.Combine(settingsFolder, "audio-mappings.json");

    private sealed class AppSettings
    {
        public bool StartWithWindows { get; set; }
        public bool ShowInTray { get; set; } = true;
        public bool MinimizeToTrayOnClose { get; set; }
        // Overlay is always enabled; the former checkbox has been removed from the UI.
        public bool OverlayEnabled { get; set; } = true;
        public int OverlayDurationSeconds { get; set; } = 5;
        public int OverlaySizePercent { get; set; } = 30;
        // Retained only to read older settings files without losing compatibility.
        public string OverlaySize { get; set; } = "Medium";
        public int OverlayOpacity { get; set; } = 97;
        public int NightModePercent { get; set; } = 30;
    }

    private AppSettings appSettings = new();
    private string AppSettingsFile => Path.Combine(settingsFolder, "app-settings.json");
    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "Nutho313PinAudio";

    private sealed class OverlayDisplaySettings
    {
        public string ScreenDeviceName { get; set; } = "DISPLAY2";
        public string Position { get; set; } = "Top right";
        public int Orientation { get; set; } = 270;
    }

    private OverlayDisplaySettings overlaySettings = new();
    private string DisplaySettingsFile => Path.Combine(settingsFolder, "display-settings.json");

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_BASE = 500;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const int VOLUME_STEP = 2;

    private bool updatingSliders;
    private int nextHotkeyId = HOTKEY_BASE;

    private readonly Dictionary<int, HotkeyAction> hotkeyActions = new();
    private readonly List<int> registeredHotkeyIds = new();
    private readonly List<ShortcutAction> shortcutActions = new();
    private readonly List<SelectableVolumeTarget> selectableTargets = new();
    private int selectedTargetIndex = -1;
    private bool nightModeActive;
    private int nightModePercent = 30;
    private readonly Dictionary<string, float> nightModeOriginalVolumes = new(StringComparer.OrdinalIgnoreCase);
    private Button? nightModeButton;
    private TrackBar? nightModeTrack;
    private Label? nightModePercentLabel;

    private readonly Dictionary<string, ShortcutBinding> savedShortcuts = new();

    private System.Windows.Forms.Timer? joystickTimer;
    private Dictionary<string, uint> joyPrevButtons = new();
    private bool joystickCaptureActive;

    private readonly string settingsFolder =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioOutputController");

    private string SettingsFile =>
        Path.Combine(settingsFolder, "settings.json");

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(
        IntPtr hWnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private class ChannelControl
    {
        public int Index;
        public TrackBar Slider = null!;
        public Label Value = null!;
    }

    private class ShortcutBinding
    {
        public uint Key { get; set; }
        public uint Modifiers { get; set; }
        // Bouton de manette HID (Pinscape / OpenPinDev) : 1..32, 0 = non utilisé.
        public int JoyButton { get; set; }
        // Identifiant du périphérique "VID:PID" (ex. 1209:EAEA).
        public string JoyId { get; set; } = "";
    }

    private class AudioGroup
    {
        public string Name = "";
        public int[] Channels = Array.Empty<int>();
        public int KeyUp;
        public int KeyDown;
        public uint Modifiers;
        public bool Linked;
    }

    private class DeviceConfiguration
    {
        public MMDevice Device = null!;
        public int DeviceNumber;
        public bool IsSsf;
        public List<AudioGroup> Groups = new();
        public Dictionary<int, ChannelControl> Controls = new();
    }

    private class ShortcutAction
    {
        public string SettingKey = "";
        public string Label = "";
        public DeviceConfiguration Config = null!;
        public int[] Channels = Array.Empty<int>();
        public bool Increase;
        public ShortcutBinding DefaultBinding = new();
        public string ActionType = "Direct";
    }

    private class SelectableVolumeTarget
    {
        public string Name = "";
        public DeviceConfiguration Config = null!;
        public int[] Channels = Array.Empty<int>();
        public bool IsNightMode;
    }

    private class HotkeyAction
    {
        public int Id;
        public uint Key;
        public uint Modifiers;
        public DeviceConfiguration Config = null!;
        public int[] Channels = Array.Empty<int>();
        public bool Increase;
        public string Description = "";
        public string ActionType = "Direct";
        public int JoyButton;
        public string JoyId = "";
    }

    public Form1()
    {
        InitializeComponent();

        Text = "Nutho313 PinAudio";
        Size = new Size(1050, 900);
        MinimumSize = new Size(850, 600);
        StartPosition = FormStartPosition.CenterScreen;
        try
        {
            string appIconFile = Path.Combine(AppContext.BaseDirectory, "Nutho313PinAudio.ico");
            if (File.Exists(appIconFile)) Icon = new Icon(appIconFile);
        }
        catch { }

        LoadSettings();
        LoadDisplaySettings();
        LoadAppSettings();
        nightModePercent = Math.Clamp(appSettings.NightModePercent, 0, 100);
        LoadAudioMappings();
        BuildInterface();

        volumeOverlay = new VolumeOverlay();
        volumeOverlay.Configure(appSettings.OverlayDurationSeconds, appSettings.OverlayOpacity, appSettings.OverlaySizePercent, overlaySettings);
        ConfigureTrayIcon();
        ApplyStartupSetting();

        RefreshDevices();

        // Polling des boutons de manette (Pinscape / OpenPinDev) pour les raccourcis.
        joystickTimer = new System.Windows.Forms.Timer { Interval = 25 };
        joystickTimer.Tick += (_, _) => PollJoystickButtons();
        joystickTimer.Start();
    }

    // ---------------------------------------------------------
    // INTERFACE
    // ---------------------------------------------------------

    private void BuildInterface()
    {
        var header = new Label
        {
            Text = "NUTHO313 PINAUDIO",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 48,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82
        };

        globalShortcutPanel = new FlowLayoutPanel
        {
            Location = new Point(205, 5),
            Size = new Size(820, 68),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
            Padding = new Padding(2)
        };
        topPanel.Controls.Add(globalShortcutPanel);

        mainPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(8)
        };

        status = new Label
        {
            Text = "Ready",
            Dock = DockStyle.Bottom,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var tabs = new TabControl { Dock = DockStyle.Fill };
        audioRoutingTab = new TabPage("Audio Mapping");
        audioSettingTab = new TabPage("Audio Setting");
        var displayTab = new TabPage("Display");
        var settingsTab = new TabPage("General");

        // Keep mapping and shortcut configuration on separate pages.
        BuildAudioSettingTab();
        BuildDisplayConfigurationTab(displayTab);
        BuildSettingsTab(settingsTab);

        tabs.TabPages.Add(audioRoutingTab);
        tabs.TabPages.Add(audioSettingTab);
        tabs.TabPages.Add(displayTab);
        tabs.TabPages.Add(settingsTab);

        Controls.Add(tabs);
        status.Dock = DockStyle.Bottom;
        Controls.Add(status);
        Controls.Add(header);
    }

    private void LoadAppSettings()
    {
        try
        {
            if (File.Exists(AppSettingsFile))
                appSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppSettingsFile)) ?? new AppSettings();
        }
        catch { appSettings = new AppSettings(); }
    }

    private void SaveAppSettings()
    {
        try
        {
            Directory.CreateDirectory(settingsFolder);
            File.WriteAllText(AppSettingsFile, JsonSerializer.Serialize(appSettings, new JsonSerializerOptions { WriteIndented = true }));
            ApplyStartupSetting();
            if (trayIcon != null) trayIcon.Visible = appSettings.ShowInTray;
            if (volumeOverlay != null && !volumeOverlay.IsDisposed)
                volumeOverlay.Configure(appSettings.OverlayDurationSeconds, appSettings.OverlayOpacity, appSettings.OverlaySizePercent, overlaySettings);
            status.Text = "Application settings saved.";
        }
        catch (Exception ex) { status.Text = "Settings error: " + ex.Message; }
    }

    private void ApplyStartupSetting()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true)
                ?? Registry.CurrentUser.CreateSubKey(StartupRegistryKey, true);
            if (key == null) return;
            key.DeleteValue("AudioOutputController", false);
            if (appSettings.StartWithWindows)
            {
                string exe = Application.ExecutablePath;
                key.SetValue(StartupValueName, "\"" + exe + "\"");
            }
            else key.DeleteValue(StartupValueName, false);
        }
        catch (Exception ex) { status.Text = "Startup setting error: " + ex.Message; }
    }

    private void ConfigureTrayIcon()
    {
        trayIcon?.Dispose();
        Icon trayImage = SystemIcons.Application;
        try
        {
            string trayFile = Path.Combine(AppContext.BaseDirectory, "Nutho313PinAudio.Tray.ico");
            if (File.Exists(trayFile)) trayImage = new Icon(trayFile);
        }
        catch { }
        trayIcon = new NotifyIcon
        {
            Text = "Nutho313 PinAudio",
            Icon = trayImage,
            Visible = appSettings.ShowInTray
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Nutho313 PinAudio", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Show volume overlay", null, (_, _) => ShowVolumeOverlay());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => { exitFromTrayMenu = true; trayIcon!.Visible = false; Close(); });
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    private void BuildSettingsTab(TabPage page)
    {
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24) };
        var heading = new Label
        {
            Text = "GENERAL SETTINGS",
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 20)
        };
        panel.Controls.Add(heading);

        startWithWindowsCheck = new CheckBox { Text = "Start with Windows", AutoSize = true, Location = new Point(28, 78), Checked = appSettings.StartWithWindows };
        showInTrayCheck = new CheckBox { Text = "Display in the Tray Taskbar", AutoSize = true, Location = new Point(28, 112), Checked = appSettings.ShowInTray };
        minimizeToTrayCheck = new CheckBox { Text = "Minimize to Tray on Close", AutoSize = true, Location = new Point(28, 146), Checked = appSettings.MinimizeToTrayOnClose };
        panel.Controls.AddRange(new Control[] { startWithWindowsCheck, showInTrayCheck, minimizeToTrayCheck });

        var save = new Button { Text = "Save settings", Location = new Point(28, 194), Width = 150, Height = 34 };
        save.Click += (_, _) =>
        {
            appSettings.StartWithWindows = startWithWindowsCheck.Checked;
            appSettings.ShowInTray = showInTrayCheck.Checked;
            appSettings.MinimizeToTrayOnClose = minimizeToTrayCheck.Checked;
            SaveAppSettings();
        };
        panel.Controls.Add(save);

        var note = new Label
        {
            Text = "Tip: when Minimize to Tray on Close is enabled, use Exit from the tray menu to quit completely.",
            AutoSize = true,
            MaximumSize = new Size(850, 0),
            Location = new Point(28, 246),
            ForeColor = Color.DimGray
        };
        panel.Controls.Add(note);

        var brandLogo = new PictureBox
        {
            Image = LoadBrandLogo(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(28, 292),
            Size = new Size(112, 112),
            BackColor = Color.Transparent
        };
        panel.Controls.Add(brandLogo);

        panel.Controls.Add(CreateExternalLink(
            "GitHub – nutho313 / PinAudio",
            "https://github.com/nutho313/PinAudio",
            new Point(160, 316)));
        panel.Controls.Add(CreateExternalLink(
            "Support me on Ko-fi",
            "https://ko-fi.com/nutho313",
            new Point(160, 354)));

        page.Controls.Add(panel);
    }

    private static Bitmap? LoadBrandLogo()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "nutho313-logo.png");
        if (!File.Exists(path)) return null;

        using var stream = File.OpenRead(path);
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static LinkLabel CreateExternalLink(string text, string url, Point location)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            Location = location,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            LinkColor = Color.FromArgb(70, 145, 190),
            ActiveLinkColor = Color.FromArgb(253, 137, 13),
            VisitedLinkColor = Color.FromArgb(70, 145, 190)
        };
        link.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        };
        return link;
    }

    private void LoadDisplaySettings()
    {
        try
        {
            if (File.Exists(DisplaySettingsFile))
                overlaySettings = JsonSerializer.Deserialize<OverlayDisplaySettings>(File.ReadAllText(DisplaySettingsFile)) ?? new OverlayDisplaySettings();
        }
        catch { overlaySettings = new OverlayDisplaySettings(); }
    }

    private void SaveDisplaySettings()
    {
        try
        {
            Directory.CreateDirectory(settingsFolder);
            File.WriteAllText(DisplaySettingsFile, JsonSerializer.Serialize(overlaySettings, new JsonSerializerOptions { WriteIndented = true }));
            if (volumeOverlay != null && !volumeOverlay.IsDisposed && volumeOverlay.Visible)
                ShowVolumeOverlay();
            status.Text = "Display configuration saved.";
        }
        catch (Exception ex) { status.Text = "Display settings error: " + ex.Message; }
    }

    private void LoadAudioMappings()
    {
        try
        {
            if (File.Exists(AudioMappingsFile))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(AudioMappingsFile));
                if (loaded != null)
                    foreach (var pair in loaded) audioMappings[pair.Key] = pair.Value;
            }
        }
        catch { audioMappings.Clear(); }
    }

    private void SaveAudioMappings()
    {
        Directory.CreateDirectory(settingsFolder);
        File.WriteAllText(AudioMappingsFile, JsonSerializer.Serialize(audioMappings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private string DeviceMapKey(DeviceConfiguration config) => config.Device.ID;

    private Dictionary<string, string> GetOrCreateDeviceMap(DeviceConfiguration config)
    {
        string key = DeviceMapKey(config);
        if (!audioMappings.TryGetValue(key, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int count = config.Device.AudioEndpointVolume.Channels.Count;

            // Default 7.1 SSF mapping, expressed as individual speaker positions.
            if (config.IsSsf && count >= 8)
            {
                map["L/R L"] = "0"; map["L/R R"] = "1";
                map["Center"] = "2"; map["Sub"] = "3";
                map["SSR L"] = "4"; map["SSR R"] = "5";
                map["SSF L"] = "6"; map["SSF R"] = "7";
            }
            else if (count == 2)
            {
                map["BackGlass L"] = "0";
                map["BackGlass R"] = "1";
            }
            audioMappings[key] = map;
        }
        else
        {
            // Migrate mappings created by V2.2 (paired values such as "4,5")
            // into individual left/right assignments.
            MigrateLegacyPair(map, "BackGlass");
            MigrateLegacyPair(map, "SSR");
            MigrateLegacyPair(map, "L/R");
            MigrateLegacyPair(map, "SSF");
        }
        return map;
    }

    private static void MigrateLegacyPair(Dictionary<string, string> map, string zone)
    {
        if (!map.TryGetValue(zone, out string? legacy) || string.IsNullOrWhiteSpace(legacy))
            return;
        string[] parts = legacy.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && !map.ContainsKey(zone + " L")) map[zone + " L"] = parts[0];
        if (parts.Length > 1 && !map.ContainsKey(zone + " R")) map[zone + " R"] = parts[1];
        map.Remove(zone);
    }

    private int[] GetMappedChannels(DeviceConfiguration config, string zone)
    {
        var map = GetOrCreateDeviceMap(config);
        if (zone is "BackGlass" or "SSR" or "L/R" or "SSF")
        {
            var pair = new[] { zone + " L", zone + " R" }
                .Select(k => map.TryGetValue(k, out string? v) && int.TryParse(v, out int n) ? n : -1)
                .Where(n => n >= 0 && n < config.Device.AudioEndpointVolume.Channels.Count)
                .Distinct().ToArray();
            return pair;
        }

        if (!map.TryGetValue(zone, out string? value) || string.IsNullOrWhiteSpace(value))
            return Array.Empty<int>();

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => int.TryParse(v, out int n) ? n : -1)
            .Where(n => n >= 0 && n < config.Device.AudioEndpointVolume.Channels.Count).ToArray();
    }

    private string GetRoutingChannelLabel(DeviceConfiguration config, int index)
    {
        string[] standard = { "L", "R", "C", "Sub", "RL", "RR", "SL", "SR" };
        int count = config.Device.AudioEndpointVolume.Channels.Count;
        if (config.IsSsf && index < standard.Length) return standard[index];
        if (count == 2 && index < 2) return standard[index];
        if (count == 6 && index < 6) return standard[index];
        if (count == 8 && index < 8) return standard[index];
        return $"Channel {index + 1}";
    }

    private void BuildAudioRoutingTab()
    {
        if (audioRoutingTab == null) return;
        audioRoutingTab.Controls.Clear();
        var outer = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16) };

        var refresh = new Button
        {
            Text = "Refresh audio devices",
            Location = new Point(12, 8),
            Width = 190,
            Height = 30
        };
        refresh.Click += (_, _) => RefreshDevices();
        outer.Controls.Add(refresh);
        outer.Controls.Add(new Label
        {
            Text = "Assign Windows playback channels to physical pinball speaker positions.",
            AutoSize = true,
            Location = new Point(220, 15)
        });

        var rows = new (string Title, string[] Keys, string? Position)[ ]
        {
            ("BackGlass", new[] { "BackGlass L", "BackGlass R" }, null),
            ("SSR", new[] { "SSR L", "SSR R" }, "REAR"),
            ("L / R", new[] { "L/R L", "L/R R" }, null),
            ("Sub", new[] { "Sub" }, null),
            ("Center", new[] { "Center" }, null),
            ("SSF", new[] { "SSF L", "SSF R" }, "FRONT")
        };

        int y = 48;
        foreach (var config in configurations)
        {
            var map = GetOrCreateDeviceMap(config);
            int groupHeight = 48 + rows.Length * 43 + 12;
            var group = new GroupBox
            {
                Text = $"{GetCleanDeviceName(config.Device.FriendlyName)} ({config.Device.AudioEndpointVolume.Channels.Count} channels)",
                Location = new Point(12, y),
                Size = new Size(960, groupHeight)
            };

            group.Controls.Add(new Label
            {
                Text = "LEFT", Font = new Font("Segoe UI", 9, FontStyle.Bold),
                AutoSize = true, Location = new Point(285, 24)
            });
            group.Controls.Add(new Label
            {
                Text = "RIGHT", Font = new Font("Segoe UI", 9, FontStyle.Bold),
                AutoSize = true, Location = new Point(630, 24)
            });

            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                var row = rows[rowIndex];
                int top = 45 + rowIndex * 43;
                group.Controls.Add(new Label
                {
                    Text = row.Title,
                    Font = new Font("Segoe UI", 9, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(18, top + 5)
                });
                if (row.Position != null)
                    group.Controls.Add(new Label
                    {
                        Text = row.Position,
                        Font = new Font("Segoe UI", 8, FontStyle.Italic),
                        ForeColor = Color.DimGray,
                        AutoSize = true,
                        Location = new Point(88, top + 6)
                    });

                bool mono = row.Keys.Length == 1;
                for (int side = 0; side < row.Keys.Length; side++)
                {
                    string position = row.Keys[side];
                    var combo = new ComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        Location = new Point(mono ? 190 : (side == 0 ? 190 : 535), top),
                        Width = mono ? 645 : 300,
                        Tag = (config, position)
                    };
                    combo.Items.Add(new MappingChoice("Not assigned", ""));
                    int count = config.Device.AudioEndpointVolume.Channels.Count;
                    for (int channel = 0; channel < count; channel++)
                        combo.Items.Add(new MappingChoice(GetRoutingChannelLabel(config, channel), channel.ToString()));

                    string current = map.TryGetValue(position, out var value) ? value : "";
                    int selected = 0;
                    for (int item = 0; item < combo.Items.Count; item++)
                        if (((MappingChoice)combo.Items[item]!).Value == current) { selected = item; break; }
                    combo.SelectedIndex = selected;
                    combo.SelectedIndexChanged += (_, _) =>
                    {
                        var (deviceConfig, speakerPosition) = ((DeviceConfiguration, string))combo.Tag!;
                        var deviceMap = GetOrCreateDeviceMap(deviceConfig);
                        deviceMap[speakerPosition] = ((MappingChoice)combo.SelectedItem!).Value;
                        try
                        {
                            SaveAudioMappings();
                            status.Text = $"Audio routing saved for {GetCleanDeviceName(deviceConfig.Device.FriendlyName)}.";
                        }
                        catch (Exception ex) { status.Text = "Audio routing save error: " + ex.Message; }
                        RebuildSelectableTargets();
                        BuildAudioSettingTab();
                        ShowVolumeOverlay();
                    };
                    group.Controls.Add(combo);
                }
            }
            outer.Controls.Add(group);
            y += groupHeight + 12;
        }
        audioRoutingTab.Controls.Add(outer);
    }

    private void BuildAudioSettingTab()
    {
        if (audioSettingTab == null) return;

        var outer = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(12)
        };

        var overlayPanel = BuildCabinetMappingPanel();
        overlayPanel.Location = new Point(12, 16);
        outer.Controls.Add(overlayPanel);

        var shortcutGroup = new GroupBox
        {
            Text = "GLOBAL SHORTCUTS",
            Location = new Point(12, 460),
            Size = new Size(960, 145),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        shortcutGroup.Controls.Add(new Label
        {
            Text = "Click and select the shortcut",
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = Color.DimGray,
            AutoSize = true,
            Location = new Point(16, 24)
        });

        if (globalShortcutPanel != null)
        {
            globalShortcutPanel.Location = new Point(12, 52);
            globalShortcutPanel.Size = new Size(920, 72);
            globalShortcutPanel.Height = 72;
            globalShortcutPanel.FlowDirection = FlowDirection.LeftToRight;
            globalShortcutPanel.WrapContents = false;
            globalShortcutPanel.AutoScroll = false;
            globalShortcutPanel.Padding = new Padding(4);
            shortcutGroup.Controls.Add(globalShortcutPanel);
        }

        outer.Controls.Add(shortcutGroup);

        var nightGroup = new GroupBox
        {
            Text = "NIGHT MODE",
            Location = new Point(12, 615),
            Size = new Size(960, 105),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        nightModeButton = new Button
        {
            Text = nightModeActive ? "Night Mode: ON" : "Night Mode: OFF",
            Location = new Point(18, 36),
            Size = new Size(190, 38),
            BackColor = nightModeActive ? Color.FromArgb(35, 145, 85) : SystemColors.Control,
            UseVisualStyleBackColor = false
        };
        nightModeButton.Click += (_, _) => SetNightMode(!nightModeActive);
        nightGroup.Controls.Add(nightModeButton);

        nightModeTrack = new TrackBar
        {
            Location = new Point(245, 28),
            Width = 520,
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Value = nightModePercent,
            SmallChange = 1,
            LargeChange = 5
        };
        nightModeTrack.ValueChanged += (_, _) =>
        {
            nightModePercent = Math.Clamp(nightModeTrack.Value, 0, 100);
            appSettings.NightModePercent = nightModePercent;
            if (nightModePercentLabel != null) nightModePercentLabel.Text = nightModePercent + "%";
            if (nightModeActive) ApplyNightModeVolumes();
            SaveAppSettings();
            ShowVolumeOverlay();
        };
        nightGroup.Controls.Add(nightModeTrack);
        nightModePercentLabel = new Label
        {
            Text = nightModePercent + "%",
            Location = new Point(785, 35),
            Size = new Size(100, 30),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        nightGroup.Controls.Add(nightModePercentLabel);
        outer.Controls.Add(nightGroup);

        audioSettingTab.Controls.Clear();
        audioSettingTab.Controls.Add(outer);
        globalShortcutPanel.SizeChanged += (_, _) =>
        {
            int cardWidth = Math.Max(150, (globalShortcutPanel.ClientSize.Width - 32) / 4);
            foreach (Control card in globalShortcutPanel.Controls)
            {
                if (card is not Panel) continue;
                card.Width = cardWidth;
                foreach (Control child in card.Controls)
                    child.Width = cardWidth - 8;
            }
        };
    }

    private Dictionary<string, string> GetOverlayStateMap()
    {
        const string stateKey = "__NUTHO313_OVERLAY_STATE__";
        if (!audioMappings.TryGetValue(stateKey, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            audioMappings[stateKey] = map;
        }
        return map;
    }

    private bool GetOverlayLinkedState(string zone)
    {
        string key = "Linked_" + zone;
        var state = GetOverlayStateMap();
        if (state.TryGetValue(key, out string? value) && bool.TryParse(value, out bool linked))
            return linked;

        // Migrate any previously saved per-device link state.
        foreach (var config in configurations)
        {
            var map = GetOrCreateDeviceMap(config);
            if (map.TryGetValue("__Linked_" + zone, out value) && bool.TryParse(value, out linked))
                return linked;
        }
        return true;
    }

    private void SetOverlayLinkedState(string zone, bool linked)
    {
        GetOverlayStateMap()["Linked_" + zone] = linked.ToString();
        foreach (var config in configurations)
        {
            var map = GetOrCreateDeviceMap(config);
            map["__Linked_" + zone] = linked.ToString();
        }
        SaveAudioMappings();
    }

    private string FindPositionMapping(string positionKey)
    {
        foreach (var config in configurations)
        {
            var map = GetOrCreateDeviceMap(config);
            if (map.TryGetValue(positionKey, out string? channelValue) &&
                !string.IsNullOrWhiteSpace(channelValue) && int.TryParse(channelValue, out int channel) &&
                channel >= 0 && channel < config.Device.AudioEndpointVolume.Channels.Count)
                return FormatMappingForDiagram(config, channelValue);
        }
        return "Not assigned";
    }

    private Panel BuildCabinetMappingPanel()
    {
        var panel = new Panel
        {
            Size = new Size(960, 430),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(245, 248, 251)
        };
        panel.Controls.Add(new Label
        {
            Text = "OVERLAY", Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 65, 85), AutoSize = true, Location = new Point(12, 8)
        });

        string[] zones = { "BackGlass", "SSR", "L/R", "Sub", "Center", "SSF" };
        string[] titles = { "BACKGLASS", "SSR", "L / R", "SUB", "CENTER", "SSF" };
        Color[] colors =
        {
            Color.FromArgb(255, 157, 91, 0), Color.FromArgb(235, 55, 40),
            Color.FromArgb(0, 155, 235), Color.FromArgb(170, 65, 220),
            Color.FromArgb(35, 195, 75), Color.FromArgb(235, 205, 0)
        };

        panel.Controls.Add(new Label
        {
            Text = "LEFT", Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(80, 95, 110), AutoSize = true, Location = new Point(300, 31)
        });
        panel.Controls.Add(new Label
        {
            Text = "RIGHT", Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(80, 95, 110), AutoSize = true, Location = new Point(625, 31)
        });

        Panel CreateColoredFrame(Point location, Size size, Color color,
            params (string Text, Rectangle Bounds)[] labels)
        {
            var frame = new Panel { Location = location, Size = size, BackColor = color, Padding = Padding.Empty };
            var inner = new Panel
            {
                Location = new Point(3, 3), Size = new Size(Math.Max(1, size.Width - 6), Math.Max(1, size.Height - 6)),
                BackColor = Color.White
            };
            foreach (var item in labels)
                inner.Controls.Add(new Label
                {
                    Text = item.Text, Location = item.Bounds.Location, Size = item.Bounds.Size,
                    AutoEllipsis = true, TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(55, 70, 85), BackColor = Color.Transparent
                });
            frame.Controls.Add(inner);
            return frame;
        }

        foreach (int zoneIndex in Enumerable.Range(0, zones.Length))
        {
            string zone = zones[zoneIndex];
            Color rowColor = colors[zoneIndex];
            int y = 49 + zoneIndex * 61;
            bool mono = zone is "Sub" or "Center";
            string leftKey = mono ? zone : zone + " L";
            string rightKey = zone + " R";
            string leftText = FindPositionMapping(leftKey);
            string rightText = mono ? "" : FindPositionMapping(rightKey);
            bool linked = mono || GetOverlayLinkedState(zone);

            panel.Controls.Add(new Label
            {
                Text = titles[zoneIndex], Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(35, 55, 75), AutoSize = true, Location = new Point(18, y + 14)
            });

            if (mono)
            {
                panel.Controls.Add(CreateColoredFrame(new Point(205, y + 2), new Size(735, 48), rowColor,
                    (leftText, new Rectangle(3, 0, 723, 42))));
                continue;
            }

            const int leftX = 205, rightX = 575, frameHeight = 48;
            if (linked)
            {
                panel.Controls.Add(CreateColoredFrame(new Point(leftX, y + 2), new Size(735, frameHeight), rowColor,
                    (leftText, new Rectangle(3, 0, 359, 42)),
                    (rightText, new Rectangle(370, 0, 359, 42))));
            }
            else
            {
                panel.Controls.Add(CreateColoredFrame(new Point(leftX, y + 2), new Size(350, frameHeight), rowColor,
                    (leftText, new Rectangle(3, 0, 338, 42))));
                panel.Controls.Add(CreateColoredFrame(new Point(rightX, y + 2), new Size(365, frameHeight), rowColor,
                    (rightText, new Rectangle(3, 0, 353, 42))));
            }

            var link = new Button
            {
                Text = linked ? "Linked" : "Unlinked",
                Location = new Point(108, y + 15), Size = new Size(86, 21),
                Font = new Font("Segoe UI", 7),
                BackColor = linked ? Color.FromArgb(215, 238, 225) : SystemColors.Control,
                Tag = (zone, linked)
            };
            link.Click += (_, _) =>
            {
                var (zoneName, oldState) = ((string, bool))link.Tag!;
                bool newState = !oldState;
                try
                {
                    SetOverlayLinkedState(zoneName, newState);
                    RebuildSelectableTargets();
                    BuildAudioSettingTab();
                    ShowVolumeOverlay();
                    status.Text = $"{zoneName}: {(newState ? "channels linked" : "channels independent")}.";
                }
                catch (Exception ex) { status.Text = "Link setting save error: " + ex.Message; }
            };
            panel.Controls.Add(link);
        }
        return panel;
    }

    private string FormatMappingForDiagram(DeviceConfiguration config, string channelValue)
    {
        if (string.IsNullOrWhiteSpace(channelValue) || !int.TryParse(channelValue, out int channel))
            return "Not assigned";
        return $"{GetCleanDeviceName(config.Device.FriendlyName)} {GetRoutingChannelLabel(config, channel)}";
    }

    private static string GetCleanDeviceName(string name)
    {
        string cleaned = name.Trim();
        foreach (string prefix in new[] { "SSF (", "BackGlass (", "SSF - ", "BackGlass - " })
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(prefix.Length);
                if (cleaned.EndsWith(")")) cleaned = cleaned[..^1];
                break;
            }
        return cleaned;
    }

    private sealed class MappingChoice
    {
        public string Label { get; }
        public string Value { get; }
        public MappingChoice(string label, string value) { Label = label; Value = value; }
        public override string ToString() => Label;
    }

    private void BuildDisplayConfigurationTab(TabPage page)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18), AutoScroll = true };
        var intro = new Label
        {
            Text = "Select a Windows display directly in the diagram, then choose the overlay position and orientation.",
            AutoSize = true,
            MaximumSize = new Size(920, 0),
            Location = new Point(18, 12)
        };
        panel.Controls.Add(intro);

        // Hidden selectors remain the single source of truth for existing persistence code.
        displayScreenCombo = new ComboBox { Visible = false, DropDownStyle = ComboBoxStyle.DropDownList };
        displayPositionCombo = new ComboBox { Visible = false, DropDownStyle = ComboBoxStyle.DropDownList };
        displayPositionCombo.Items.AddRange(new object[] { "Center", "Left", "Top", "Bottom", "Right" });
        displayOrientationCombo = new ComboBox { Visible = false, DropDownStyle = ComboBoxStyle.DropDownList };
        displayOrientationCombo.Items.AddRange(new object[] { "0° — normal", "90° — clockwise", "180° — upside down", "270° — counter-clockwise" });
        panel.Controls.Add(displayScreenCombo);
        panel.Controls.Add(displayPositionCombo);
        panel.Controls.Add(displayOrientationCombo);

        var refresh = new Button { Text = "Refresh Windows screens", Location = new Point(18, 42), Width = 190, Height = 30 };
        refresh.Click += (_, _) => PopulateDisplayChoices();
        panel.Controls.Add(refresh);

        displayMapPreview = new Panel
        {
            Name = "displayMapPreview",
            Location = new Point(18, 82),
            Size = new Size(900, 205),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(242, 245, 248)
        };
        displayMapPreview.Paint += PaintDisplayMapPreview;
        displayMapPreview.MouseClick += (_, e) =>
        {
            foreach (var hit in displayHitRects)
            {
                if (hit.Value.Contains(e.Location))
                {
                    int index = Array.FindIndex(Screen.AllScreens, s => s.DeviceName.Equals(hit.Key, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0)
                    {
                        displayScreenCombo.SelectedIndex = index;
                        UpdateDisplayPreview(displayInfoPreview);
                        displayMapPreview.Invalidate();
                    }
                    break;
                }
            }
        };
        panel.Controls.Add(displayMapPreview);

        panel.Controls.Add(new Label { Text = "Overlay position", AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(18, 302) });
        string[] positions = { "Center", "Left", "Top", "Bottom", "Right" };
        int px = 18;
        foreach (string position in positions)
        {
            var button = new Button { Text = position, Width = 105, Height = 32, Location = new Point(px, 328), Tag = position };
            button.Click += (_, _) =>
            {
                displayPositionCombo.SelectedItem = position;
                UpdateDisplaySelectionButtons();
                UpdateDisplayPreview(displayInfoPreview);
                displayMapPreview.Invalidate();
            };
            displayPositionButtons.Add(button);
            panel.Controls.Add(button);
            px += 112;
        }

        panel.Controls.Add(new Label { Text = "Overlay orientation", AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(18, 376) });
        string[] orientations = { "0°", "90°", "180°", "270°" };
        int ox = 18;
        for (int i = 0; i < orientations.Length; i++)
        {
            int orientationIndex = i;
            var button = new Button { Text = orientations[i], Width = 105, Height = 32, Location = new Point(ox, 402), Tag = i };
            button.Click += (_, _) =>
            {
                displayOrientationCombo.SelectedIndex = orientationIndex;
                UpdateDisplaySelectionButtons();
                UpdateDisplayPreview(displayInfoPreview);
                displayMapPreview.Invalidate();
            };
            displayOrientationButtons.Add(button);
            panel.Controls.Add(button);
            ox += 112;
        }

        // Overlay appearance settings.
        var overlayGroup = new GroupBox
        {
            Text = "Overlay Display Settings",
            Location = new Point(18, 456),
            Size = new Size(900, 220)
        };

        overlayGroup.Controls.Add(new Label { Text = "Overlay duration", AutoSize = true, Location = new Point(24, 38) });
        overlayDurationCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(180, 34), Width = 150 };
        overlayDurationCombo.Items.AddRange(new object[] { "2", "3", "5", "8", "10" });
        overlayDurationCombo.SelectedItem = appSettings.OverlayDurationSeconds.ToString();
        if (overlayDurationCombo.SelectedIndex < 0) overlayDurationCombo.SelectedItem = "5";
        overlayGroup.Controls.Add(overlayDurationCombo);

        overlayGroup.Controls.Add(new Label { Text = "Overlay size", AutoSize = true, Location = new Point(24, 88) });
        overlaySizeValueLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Location = new Point(450, 86),
            Text = $"{Math.Clamp(appSettings.OverlaySizePercent, 10, 90)}%"
        };
        overlaySizeTrack = new TrackBar
        {
            Location = new Point(180, 72),
            Width = 250,
            Minimum = 10,
            Maximum = 90,
            TickFrequency = 10,
            SmallChange = 5,
            LargeChange = 10,
            Value = Math.Clamp(appSettings.OverlaySizePercent, 10, 90)
        };
        overlaySizeTrack.ValueChanged += (_, _) =>
        {
            overlaySizeValueLabel.Text = $"{overlaySizeTrack.Value}%";
            UpdateDisplayPreview(displayInfoPreview);
            displayMapPreview.Invalidate();
        };
        overlayGroup.Controls.Add(overlaySizeTrack);
        overlayGroup.Controls.Add(overlaySizeValueLabel);

        overlayGroup.Controls.Add(new Label { Text = "Overlay opacity", AutoSize = true, Location = new Point(24, 144) });
        overlayOpacityTrack = new TrackBar
        {
            Location = new Point(180, 128),
            Width = 250,
            Minimum = 30,
            Maximum = 100,
            TickFrequency = 10,
            Value = Math.Clamp(appSettings.OverlayOpacity, 30, 100)
        };
        overlayOpacityTrack.ValueChanged += (_, _) =>
        {
            UpdateDisplayPreview(displayInfoPreview);
            displayMapPreview.Invalidate();
        };
        overlayGroup.Controls.Add(overlayOpacityTrack);

        panel.Controls.Add(overlayGroup);

        var apply = new Button
        {
            Text = "Save",
            Width = 160,
            Height = 34,
            Location = new Point(758, 688),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        apply.Click += (_, _) => ApplyDisplayChoices();
        panel.Controls.Add(apply);

        displayInfoPreview = new Label
        {
            Name = "displayPreviewLabel",
            Location = new Point(18, 738),
            Size = new Size(900, 58),
            Padding = new Padding(8),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White
        };
        panel.Controls.Add(displayInfoPreview);
        page.Controls.Add(panel);

        PopulateDisplayChoices();
        displayPositionCombo.SelectedItem = NormalizeDisplayPosition(overlaySettings.Position);
        displayOrientationCombo.SelectedIndex = overlaySettings.Orientation switch { 0 => 0, 90 => 1, 180 => 2, _ => 3 };
        UpdateDisplaySelectionButtons();
        UpdateDisplayPreview(displayInfoPreview);
    }

    private static string NormalizeDisplayPosition(string value)
    {
        if (value.Contains("Center", StringComparison.OrdinalIgnoreCase)) return "Center";
        if (value.Contains("Left", StringComparison.OrdinalIgnoreCase)) return "Left";
        if (value.Contains("Bottom", StringComparison.OrdinalIgnoreCase)) return "Bottom";
        if (value.Contains("Right", StringComparison.OrdinalIgnoreCase)) return "Right";
        return "Top";
    }

    private void UpdateDisplaySelectionButtons()
    {
        foreach (Button button in displayPositionButtons)
        {
            bool selected = string.Equals(button.Tag?.ToString(), displayPositionCombo.SelectedItem?.ToString(), StringComparison.OrdinalIgnoreCase);
            button.BackColor = selected ? Color.FromArgb(70, 145, 190) : SystemColors.Control;
            button.ForeColor = selected ? Color.White : SystemColors.ControlText;
        }
        foreach (Button button in displayOrientationButtons)
        {
            bool selected = Convert.ToInt32(button.Tag) == displayOrientationCombo.SelectedIndex;
            button.BackColor = selected ? Color.FromArgb(70, 145, 190) : SystemColors.Control;
            button.ForeColor = selected ? Color.White : SystemColors.ControlText;
        }
    }

    private void PopulateDisplayChoices()
    {
        if (displayScreenCombo == null) return;
        string wanted = overlaySettings.ScreenDeviceName;
        displayScreenCombo.Items.Clear();
        foreach (Screen screen in Screen.AllScreens)
            displayScreenCombo.Items.Add($"{screen.DeviceName} — {screen.Bounds.Width} × {screen.Bounds.Height} — ({screen.Bounds.X}, {screen.Bounds.Y})");
        int found = -1;
        for (int i = 0; i < Screen.AllScreens.Length; i++)
            if (Screen.AllScreens[i].DeviceName.Equals(wanted, StringComparison.OrdinalIgnoreCase)) { found = i; break; }
        displayScreenCombo.SelectedIndex = found >= 0 ? found : (Screen.AllScreens.Length > 0 ? 0 : -1);
        if (displayInfoPreview != null) UpdateDisplayPreview(displayInfoPreview);
        displayMapPreview?.Invalidate();
    }

    private void UpdateDisplayPreview(Label preview)
    {
        if (displayScreenCombo.SelectedIndex < 0 || displayScreenCombo.SelectedIndex >= Screen.AllScreens.Length)
        {
            preview.Text = "No Windows display detected.";
            return;
        }
        Screen screen = Screen.AllScreens[displayScreenCombo.SelectedIndex];
        string orientation = displayOrientationCombo.SelectedIndex switch
        {
            0 => "0° — normal", 1 => "90° — clockwise", 2 => "180° — upside down", _ => "270° — counter-clockwise"
        };
        preview.Text = $"{screen.DeviceName}   |   {screen.Bounds.Width} × {screen.Bounds.Height}   |   X={screen.Bounds.X}, Y={screen.Bounds.Y}   |   Position: {displayPositionCombo.Text}   |   Orientation: {orientation}";
        displayMapPreview?.Invalidate();
    }

    private void PaintDisplayMapPreview(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Screen[] screens = Screen.AllScreens;
        displayHitRects.Clear();
        if (screens.Length == 0) return;

        int minX = screens.Min(x => x.Bounds.Left), minY = screens.Min(x => x.Bounds.Top);
        int maxX = screens.Max(x => x.Bounds.Right), maxY = screens.Max(x => x.Bounds.Bottom);
        float scale = Math.Min((displayMapPreview.Width - 44f) / Math.Max(1, maxX - minX),
                               (displayMapPreview.Height - 44f) / Math.Max(1, maxY - minY));

        foreach (Screen screen in screens)
        {
            Rectangle r = screen.Bounds;
            var rect = new RectangleF(22 + (r.X - minX) * scale, 22 + (r.Y - minY) * scale,
                Math.Max(26, r.Width * scale), Math.Max(22, r.Height * scale));
            displayHitRects[screen.DeviceName] = rect;
            bool selected = displayScreenCombo.SelectedIndex >= 0 &&
                displayScreenCombo.SelectedIndex < screens.Length &&
                screens[displayScreenCombo.SelectedIndex].DeviceName.Equals(screen.DeviceName, StringComparison.OrdinalIgnoreCase);
            using var fill = new SolidBrush(selected ? Color.FromArgb(210, 70, 145, 190) : Color.FromArgb(220, 205, 215, 225));
            using var pen = new Pen(selected ? Color.FromArgb(20, 105, 160) : Color.FromArgb(90, 105, 120), selected ? 3 : 1.5f);
            g.FillRectangle(fill, rect);
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            string label = screen.DeviceName.Replace(@"\\.\", "") + "\n" + r.Width + "×" + r.Height +
                (selected ? "\nSELECTED — CLICK TO KEEP" : "\nCLICK TO SELECT");
            using var font = new Font("Segoe UI", Math.Clamp((int)(9 * scale + 3), 7, 11), FontStyle.Bold);
            using var brush = new SolidBrush(selected ? Color.White : Color.FromArgb(35, 45, 55));
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(label, font, brush, rect, sf);
        }

        // Draw a recognizable application window. The title bar is part of the bitmap,
        // so it rotates together with the window for 90/180/270 degree orientations.
        if (displayScreenCombo.SelectedIndex >= 0 && displayScreenCombo.SelectedIndex < screens.Length)
        {
            Screen selectedScreen = screens[displayScreenCombo.SelectedIndex];
            if (displayHitRects.TryGetValue(selectedScreen.DeviceName, out RectangleF screenRect))
            {
                int orientation = displayOrientationCombo.SelectedIndex switch { 1 => 90, 2 => 180, 3 => 270, _ => 0 };
                bool quarterTurn = orientation == 90 || orientation == 270;
                int sizePercent = overlaySizeTrack == null ? Math.Clamp(appSettings.OverlaySizePercent, 10, 90) : overlaySizeTrack.Value;

                float aspectWidth = quarterTurn ? 560f : 480f;
                float aspectHeight = quarterTurn ? 480f : 560f;
                float overlayW = screenRect.Width * sizePercent / 100f;
                float overlayH = overlayW * aspectHeight / aspectWidth;
                float maxW = screenRect.Width * 0.92f;
                float maxH = screenRect.Height * 0.92f;
                if (overlayW > maxW) { overlayW = maxW; overlayH = overlayW * aspectHeight / aspectWidth; }
                if (overlayH > maxH) { overlayH = maxH; overlayW = overlayH * aspectWidth / aspectHeight; }
                overlayW = Math.Max(18, overlayW);
                overlayH = Math.Max(18, overlayH);

                string position = displayPositionCombo.SelectedItem?.ToString() ?? "Right";
                const float margin = 5f;
                float ox = position switch
                {
                    "Left" => screenRect.Left + margin,
                    "Center" or "Top" or "Bottom" => screenRect.Left + (screenRect.Width - overlayW) / 2,
                    _ => screenRect.Right - overlayW - margin
                };
                float oy = position switch
                {
                    "Top" => screenRect.Top + margin,
                    "Bottom" => screenRect.Bottom - overlayH - margin,
                    "Center" or "Left" or "Right" => screenRect.Top + (screenRect.Height - overlayH) / 2,
                    _ => screenRect.Top + margin
                };
                ox = Math.Clamp(ox, screenRect.Left, screenRect.Right - overlayW);
                oy = Math.Clamp(oy, screenRect.Top, screenRect.Bottom - overlayH);
                var overlayRect = new RectangleF(ox, oy, overlayW, overlayH);

                using (var shadow = new SolidBrush(Color.FromArgb(85, 0, 0, 0)))
                    g.FillRectangle(shadow, overlayRect.X + 3, overlayRect.Y + 3, overlayRect.Width, overlayRect.Height);

                using var windowBitmap = new Bitmap(480, 560);
                using (Graphics wg = Graphics.FromImage(windowBitmap))
                {
                    wg.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    wg.Clear(Color.FromArgb(15, 35, 52));
                    using var outerPen = new Pen(Color.FromArgb(70, 145, 190), 8);
                    wg.DrawRectangle(outerPen, 4, 4, 472, 552);
                    using var titleBrush = new SolidBrush(Color.FromArgb(47, 111, 153));
                    wg.FillRectangle(titleBrush, 8, 8, 464, 66);
                    using var titleFont = new Font("Segoe UI", 25, FontStyle.Bold);
                    using var titleText = new SolidBrush(Color.White);
                    var titleRect = new RectangleF(18, 10, 444, 62);
                    var titleFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                    wg.DrawString("Nutho313 PinAudio", titleFont, titleText, titleRect, titleFormat);
                    using var dividerPen = new Pen(Color.FromArgb(120, 190, 230), 3);
                    wg.DrawLine(dividerPen, 8, 76, 472, 76);
                }

                RotateFlipType rotate = orientation switch
                {
                    90 => RotateFlipType.Rotate90FlipNone,
                    180 => RotateFlipType.Rotate180FlipNone,
                    270 => RotateFlipType.Rotate270FlipNone,
                    _ => RotateFlipType.RotateNoneFlipNone
                };
                windowBitmap.RotateFlip(rotate);
                using (var imageAttributes = new ImageAttributes())
                {
                    var colorMatrix = new ColorMatrix { Matrix33 = Math.Clamp(overlayOpacityTrack.Value, 30, 100) / 100f };
                    imageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    g.DrawImage(windowBitmap, Rectangle.Round(overlayRect), 0, 0, windowBitmap.Width, windowBitmap.Height,
                        GraphicsUnit.Pixel, imageAttributes);
                }
                using var framePen = new Pen(Color.FromArgb(255, 253, 137, 13), 2.5f);
                g.DrawRectangle(framePen, overlayRect.X, overlayRect.Y, overlayRect.Width, overlayRect.Height);
            }
        }
    }


    private void ApplyDisplayChoices()
    {
        if (displayScreenCombo.SelectedIndex < 0) { status.Text = "No display selected."; return; }
        overlaySettings.ScreenDeviceName = Screen.AllScreens[displayScreenCombo.SelectedIndex].DeviceName;
        overlaySettings.Position = displayPositionCombo.SelectedItem?.ToString() ?? "Right";
        overlaySettings.Orientation = displayOrientationCombo.SelectedIndex switch { 0 => 0, 1 => 90, 2 => 180, _ => 270 };
        appSettings.OverlayEnabled = true;
        appSettings.OverlayDurationSeconds = int.TryParse(overlayDurationCombo.SelectedItem?.ToString(), out int seconds) ? seconds : 5;
        appSettings.OverlaySizePercent = overlaySizeTrack.Value;
        appSettings.OverlayOpacity = overlayOpacityTrack.Value;
        SaveDisplaySettings();
        SaveAppSettings();
        UpdateDisplaySelectionButtons();
        UpdateDisplayPreview(displayInfoPreview);
        volumeOverlay?.Configure(appSettings.OverlayDurationSeconds, appSettings.OverlayOpacity, appSettings.OverlaySizePercent, overlaySettings);
        if (selectableTargets.Count > 0) ShowVolumeOverlay();
    }

    // ---------------------------------------------------------
    // SETTINGS
    // ---------------------------------------------------------

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                string json = File.ReadAllText(SettingsFile);

                var loaded =
                    JsonSerializer.Deserialize<Dictionary<string, ShortcutBinding>>(json);

                if (loaded != null)
                {
                    foreach (var item in loaded)
                        savedShortcuts[item.Key] = item.Value;
                }
            }
        }
        catch
        {
            savedShortcuts.Clear();
        }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(settingsFolder);

            string json = JsonSerializer.Serialize(
                savedShortcuts,
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(SettingsFile, json);
        }
        catch (Exception ex)
        {
            status.Text = "Settings save error: " + ex.Message;
        }
    }

    private string MakeSettingKey(
        DeviceConfiguration config,
        AudioGroup group,
        string action)
    {
        return config.Device.ID + "|" + group.Name + "|" + action;
    }

    private ShortcutBinding GetBinding(
        string key,
        ShortcutBinding defaultBinding)
    {
        if (savedShortcuts.TryGetValue(key, out var saved))
            return saved;

        return defaultBinding;
    }

    // ---------------------------------------------------------
    // AUDIO DEVICES
    // ---------------------------------------------------------

    private void RefreshDevices()
    {
        if (nightModeActive) SetNightMode(false);
        UnregisterAllHotkeys();

        foreach (var device in devices)
            device.Dispose();

        devices.Clear();
        configurations.Clear();
        hotkeyActions.Clear();
        shortcutActions.Clear();
        mainPanel.Controls.Clear();

        try
        {
            var endpoints = enumerator.EnumerateAudioEndPoints(
                DataFlow.Render,
                DeviceState.Active);

            foreach (var device in endpoints)
                devices.Add(device);

            for (int i = 0; i < devices.Count; i++)
                BuildDevice(devices[i], i);

            RebuildAllShortcuts();
            BuildAudioRoutingTab();

            status.Text = $"{devices.Count} active audio devices detected.";
        }
        catch (Exception ex)
        {
            status.Text = "Detection error: " + ex.Message;
        }
    }

    private void BuildDevice(MMDevice device, int deviceNumber)
    {
        int count = device.AudioEndpointVolume.Channels.Count;

        var config = new DeviceConfiguration
        {
            Device = device,
            DeviceNumber = deviceNumber,
            IsSsf = count == 8
        };

        configurations.Add(config);

        if (config.IsSsf)
            CreateSsfGroups(config);
        else
            CreateGenericGroups(config, count);

        int contentHeight = 32;

        foreach (var group in config.Groups)
            contentHeight += GetGroupHeight(group) + 8;

        var deviceBox = new GroupBox
        {
            Text = GetCleanDeviceName(device.FriendlyName),
            Width = 960,
            Height = contentHeight + 38,
            AutoSize = false,
            Padding = new Padding(8),
            Margin = new Padding(4, 5, 4, 8)
        };

        var content = new FlowLayoutPanel
        {
            Location = new Point(8, 22),
            Width = 925,
            Height = contentHeight,
            AutoSize = false,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            Padding = new Padding(0)
        };

        var info = new Label
        {
            Text = $"{count} endpoint volume channels detected",
            AutoSize = false,
            Width = 900,
            Height = 25,
            ForeColor = Color.DimGray,
            Margin = new Padding(4, 0, 4, 3)
        };

        content.Controls.Add(info);

        foreach (var group in config.Groups)
            AddGroupInterface(content, config, group);

        deviceBox.Controls.Add(content);
        mainPanel.Controls.Add(deviceBox);
    }

    private int GetGroupHeight(AudioGroup group)
    {
        return group.Channels.Length == 2 ? 125 : 105;
    }

    private void CreateSsfGroups(DeviceConfiguration config)
    {
        config.Groups.Add(new AudioGroup
        {
            Name = "SUB",
            Channels = new[] { 3 },
            KeyUp = 0x76,
            KeyDown = 0x77,
            Linked = true
        });

        config.Groups.Add(new AudioGroup
        {
            Name = "FRONT",
            Channels = new[] { 6, 7 },
            KeyUp = 0x72,
            KeyDown = 0x73,
            Linked = true
        });

        config.Groups.Add(new AudioGroup
        {
            Name = "LR",
            Channels = new[] { 0, 1 },
            KeyUp = 0x70,
            KeyDown = 0x71,
            Linked = true
        });

        config.Groups.Add(new AudioGroup
        {
            Name = "REAR",
            Channels = new[] { 4, 5 },
            KeyUp = 0x74,
            KeyDown = 0x75,
            Linked = true
        });

        config.Groups.Add(new AudioGroup
        {
            Name = "CENTER",
            Channels = new[] { 2 },
            KeyUp = 0x78,
            KeyDown = 0x79,
            Linked = true
        });
    }

    private void CreateGenericGroups(DeviceConfiguration config, int count)
    {
        if (count == 2)
        {
            config.Groups.Add(new AudioGroup
            {
                Name = "L / R",
                Channels = new[] { 0, 1 },
                KeyUp = 0x70,
                KeyDown = 0x71,
                Modifiers = MOD_CONTROL,
                Linked = true
            });

            return;
        }

        for (int i = 0; i < count; i++)
        {
            int keyNumber = i % 5;
            uint modifiers = MOD_CONTROL;

            if (i >= 5)
                modifiers |= MOD_SHIFT;

            int keyUp = 0x70 + keyNumber * 2;

            config.Groups.Add(new AudioGroup
            {
                Name = $"CHANNEL {i + 1}",
                Channels = new[] { i },
                KeyUp = keyUp,
                KeyDown = keyUp + 1,
                Modifiers = modifiers,
                Linked = true
            });
        }
    }

    private string GetChannelName(DeviceConfiguration config, int index)
    {
        if (!config.IsSsf)
        {
            if (config.Device.AudioEndpointVolume.Channels.Count == 2)
                return index == 0 ? "L" : "R";

            return $"Channel {index + 1}";
        }

        string[] names =
        {
            "L", "R", "CENTER", "SUB",
            "RL", "RR", "SL", "SR"
        };

        return names[index];
    }

    // ---------------------------------------------------------
    // GROUP INTERFACE
    // ---------------------------------------------------------

    private void AddGroupInterface(
        FlowLayoutPanel parent,
        DeviceConfiguration config,
        AudioGroup group)
    {
        bool isPair = group.Channels.Length == 2;

        var groupPanel = new Panel
        {
            Width = 910,
            Height = GetGroupHeight(group),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(3, 2, 3, 3)
        };

        var title = new Label
        {
            Text = group.Name,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Location = new Point(10, 3),
            AutoSize = true
        };

        groupPanel.Controls.Add(title);

        var shortcutPanel = new Panel
        {
            Location = new Point(10, isPair ? 82 : 65),
            Width = 880,
            Height = 38
        };

        groupPanel.Controls.Add(shortcutPanel);

        if (isPair)
        {
            AddChannelSlider(
                config, groupPanel, group.Channels[0],
                GetChannelName(config, group.Channels[0]),
                10, 390);

            AddChannelSlider(
                config, groupPanel, group.Channels[1],
                GetChannelName(config, group.Channels[1]),
                500, 390);

            var linkButton = new Button
            {
                Text = group.Linked ? "🔗" : "Unlink",
                Location = new Point(415, 35),
                Width = 70,
                Height = 27
            };

            linkButton.Click += (_, _) =>
            {
                group.Linked = !group.Linked;
                linkButton.Text = group.Linked ? "🔗" : "Unlink";

                RebuildAllShortcuts();

                status.Text = group.Linked
                    ? $"{group.Name}: channels linked."
                    : $"{group.Name}: channels independent.";
            };

            groupPanel.Controls.Add(linkButton);
        }
        else
        {
            AddChannelSlider(
                config, groupPanel, group.Channels[0],
                GetChannelName(config, group.Channels[0]),
                10, 880);
        }

        groupPanel.Tag = shortcutPanel;
        groupPanel.AccessibleName = group.Name;

        parent.Controls.Add(groupPanel);
    }

    private void AddChannelSlider(
        DeviceConfiguration config,
        Panel parent,
        int index,
        string name,
        int x,
        int width)
    {
        var nameLabel = new Label
        {
            Text = name,
            Location = new Point(x, 38),
            Width = 65,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };

        var slider = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Width = width - 135,
            Height = 42,
            Location = new Point(x + 65, 24)
        };

        var valueLabel = new Label
        {
            Location = new Point(x + width - 65, 42),
            AutoSize = true
        };

        try
        {
            slider.Value = Math.Clamp(
                (int)Math.Round(
                    config.Device.AudioEndpointVolume.Channels[index]
                        .VolumeLevelScalar * 100),
                0, 100);

            valueLabel.Text = slider.Value + "%";
        }
        catch
        {
            slider.Enabled = false;
            valueLabel.Text = "Unavailable";
        }

        slider.Scroll += (_, _) =>
        {
            if (!updatingSliders)
                SetChannelVolume(config, index, slider.Value);
        };

        config.Controls[index] = new ChannelControl
        {
            Index = index,
            Slider = slider,
            Value = valueLabel
        };

        parent.Controls.Add(nameLabel);
        parent.Controls.Add(slider);
        parent.Controls.Add(valueLabel);
    }

    // ---------------------------------------------------------
    // SHORTCUT EDITOR
    // ---------------------------------------------------------

    private void RebuildAllShortcuts()
    {
        UnregisterAllHotkeys();

        shortcutActions.Clear();
        hotkeyActions.Clear();
        nextHotkeyId = HOTKEY_BASE;

        foreach (var config in configurations)
        {
            foreach (var group in config.Groups)
            {
                Panel? panel = FindShortcutPanel(config, group);

                if (panel != null)
                    UpdateGroupShortcuts(config, group, panel);
            }
        }

        AddGlobalSelectorShortcuts();
        RebuildSelectableTargets();
        RebuildHotkeys();
    }

    private void AddGlobalSelectorShortcuts()
    {
        DeviceConfiguration? config = configurations.Find(c => c.IsSsf) ?? configurations.FirstOrDefault();
        if (config == null) return;

        AddGlobalShortcutAction(config, "Channel Selector", "GLOBAL_SELECTOR", "Selector");
        AddGlobalShortcutAction(config, "Night Mode", "GLOBAL_NIGHT_MODE", "NightMode");
        AddGlobalShortcutAction(config, "Volume Down", "GLOBAL_DOWN", "SelectedDown");
        AddGlobalShortcutAction(config, "Volume Up", "GLOBAL_UP", "SelectedUp");

        if (globalShortcutPanel == null) return;
        globalShortcutPanel.SuspendLayout();
        globalShortcutPanel.Controls.Clear();
        foreach (var action in shortcutActions.FindAll(x => x.ActionType != "Direct"))
        {
            var binding = GetBinding(action.SettingKey, action.DefaultBinding);
            string icon = action.ActionType switch
            {
                "Selector" => "⟳",
                "SelectedUp" => "🔊 +",
                "SelectedDown" => "🔉 −",
                "NightMode" => "☾",
                _ => "•"
            };

            var card = new Panel
            {
                Width = Math.Max(150, (globalShortcutPanel.ClientSize.Width - 32) / 4),
                Height = 64,
                Margin = new Padding(4, 0, 4, 0),
                BackColor = Color.Transparent
            };
            var button = new Button
            {
                Text = icon + "   " + action.Label,
                Width = card.Width - 8,
                Height = 36,
                Location = new Point(2, 0),
                Font = new Font("Segoe UI", 8, FontStyle.Regular),
                Tag = action
            };
            var assignedKey = new Label
            {
                Text = binding.Key == 0 && binding.JoyButton == 0 ? "Not assigned" : FormatBinding(binding),
                Width = card.Width - 8,
                Height = 22,
                Location = new Point(2, 38),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(75, 90, 105),
                Font = new Font("Segoe UI", 8, FontStyle.Bold)
            };

            button.Click += (_, _) =>
            {
                using var dialog = new ShortcutCaptureForm(action.Label);
                joystickCaptureActive = true;
                DialogResult captureResult;
                try { captureResult = dialog.ShowDialog(this); }
                finally { joystickCaptureActive = false; }
                if (captureResult == DialogResult.OK)
                {
                    savedShortcuts[action.SettingKey] = dialog.Result;
                    SaveSettings();
                    RebuildAllShortcuts();
                    status.Text = action.Label + ": " + FormatBinding(dialog.Result) + " saved.";
                }
            };

            card.Controls.Add(button);
            card.Controls.Add(assignedKey);
            globalShortcutPanel.Controls.Add(card);
        }
        globalShortcutPanel.ResumeLayout();
    }

    private void AddGlobalShortcutAction(DeviceConfiguration config, string label, string key, string actionType)
    {
        shortcutActions.Add(new ShortcutAction
        {
            SettingKey = "GLOBAL|" + key, Label = label, Config = config,
            Channels = Array.Empty<int>(), Increase = false,
            DefaultBinding = new ShortcutBinding { Key = 0, Modifiers = 0 },
            ActionType = actionType
        });
    }

    private bool IsCabinetZoneLinked(DeviceConfiguration config, string zone, int[] channels)
    {
        var state = GetOverlayStateMap();
        if (state.TryGetValue("Linked_" + zone, out string? saved) && bool.TryParse(saved, out bool linked))
            return linked;
        var map = GetOrCreateDeviceMap(config);
        if (map.TryGetValue("__Linked_" + zone, out saved) && bool.TryParse(saved, out linked))
            return linked;
        return channels.Length == 2 && IsPairLinked(config, channels[0], channels[1]);
    }

    private void RebuildSelectableTargets()
    {
        string? previouslySelected = selectedTargetIndex >= 0 && selectedTargetIndex < selectableTargets.Count
            ? selectableTargets[selectedTargetIndex].Name : null;
        selectableTargets.Clear();
        string[] zones = { "BackGlass", "SSR", "L/R", "Sub", "Center", "SSF" };
        foreach (string zone in zones)
        {
            foreach (var config in configurations)
            {
                int[] channels = GetMappedChannels(config, zone);
                if (channels.Length == 0) continue;
                string displayName = zone switch { "BackGlass" => "Back Glass", "SSR" => "SSR", "L/R" => "L / R", "Center" => "Center", "Sub" => "SUB", _ => "SSF" };
                if (channels.Length == 2 && !IsCabinetZoneLinked(config, zone, channels))
                {
                    selectableTargets.Add(new SelectableVolumeTarget { Name = displayName + " L", Config = config, Channels = new[] { channels[0] } });
                    selectableTargets.Add(new SelectableVolumeTarget { Name = displayName + " R", Config = config, Channels = new[] { channels[1] } });
                }
                else
                    selectableTargets.Add(new SelectableVolumeTarget { Name = displayName + (channels.Length == 2 ? " L / R" : ""), Config = config, Channels = channels });
                break;
            }
        }
        selectableTargets.Add(new SelectableVolumeTarget { Name = "Night Mode", IsNightMode = true });
        selectedTargetIndex = previouslySelected == null ? -1 : selectableTargets.FindIndex(t => t.Name == previouslySelected);
    }

    private void AddPairTargets(string name, DeviceConfiguration config, int left, int right)
    {
        AudioGroup? group = config.Groups.Find(g => g.Channels.Length == 2 && Array.IndexOf(g.Channels, left) >= 0 && Array.IndexOf(g.Channels, right) >= 0);
        if (group != null && group.Linked)
            selectableTargets.Add(new SelectableVolumeTarget { Name = name + " L / R", Config = config, Channels = new[] { left, right } });
        else
        {
            selectableTargets.Add(new SelectableVolumeTarget { Name = name + " L", Config = config, Channels = new[] { left } });
            selectableTargets.Add(new SelectableVolumeTarget { Name = name + " R", Config = config, Channels = new[] { right } });
        }
    }

    private void AddSingleTarget(string name, DeviceConfiguration config, int channel)
    {
        selectableTargets.Add(new SelectableVolumeTarget { Name = name, Config = config, Channels = new[] { channel } });
    }

    private Panel? FindShortcutPanel(
        DeviceConfiguration config,
        AudioGroup group)
    {
        foreach (Control deviceBox in mainPanel.Controls)
        {
            if (deviceBox is not GroupBox)
                continue;

            if (!deviceBox.Text.Equals(
                config.Device.FriendlyName,
                StringComparison.Ordinal))
                continue;

            foreach (Control content in deviceBox.Controls)
            {
                foreach (Control item in content.Controls)
                {
                    if (item is Panel panel &&
                        item.AccessibleName == group.Name &&
                        item.Tag is Panel shortcutPanel)
                    {
                        return shortcutPanel;
                    }
                }
            }
        }

        return null;
    }

    private void UpdateGroupShortcuts(
        DeviceConfiguration config,
        AudioGroup group,
        Panel panel)
    {
        panel.Controls.Clear();

        if (group.Channels.Length == 1 || group.Linked)
        {
            AddEditableShortcut(
                panel, config, group,
                "Volume −", "DOWN",
                group.Channels,
                false,
                group.KeyDown,
                group.Modifiers,
                0);

            AddEditableShortcut(
                panel, config, group,
                "Volume +", "UP",
                group.Channels,
                true,
                group.KeyUp,
                group.Modifiers,
                400);
        }
        else
        {
            AddEditableShortcut(
                panel, config, group,
                GetChannelName(config, group.Channels[0]) + " −",
                "CH0DOWN",
                new[] { group.Channels[0] },
                false,
                group.KeyDown,
                group.Modifiers,
                0);

            AddEditableShortcut(
                panel, config, group,
                GetChannelName(config, group.Channels[0]) + " +",
                "CH0UP",
                new[] { group.Channels[0] },
                true,
                group.KeyUp,
                group.Modifiers,
                190);

            AddEditableShortcut(
                panel, config, group,
                GetChannelName(config, group.Channels[1]) + " −",
                "CH1DOWN",
                new[] { group.Channels[1] },
                false,
                group.KeyDown,
                group.Modifiers | MOD_SHIFT,
                440);

            AddEditableShortcut(
                panel, config, group,
                GetChannelName(config, group.Channels[1]) + " +",
                "CH1UP",
                new[] { group.Channels[1] },
                true,
                group.KeyUp,
                group.Modifiers | MOD_SHIFT,
                630);
        }
    }

    private void AddEditableShortcut(
        Panel panel,
        DeviceConfiguration config,
        AudioGroup group,
        string label,
        string actionName,
        int[] channels,
        bool increase,
        int defaultKey,
        uint defaultModifiers,
        int x)
    {
        string settingKey =
            MakeSettingKey(config, group, actionName);

        var defaultBinding = new ShortcutBinding
        {
            Key = (uint)defaultKey,
            Modifiers = defaultModifiers
        };

        var binding = GetBinding(settingKey, defaultBinding);

        var action = new ShortcutAction
        {
            SettingKey = settingKey,
            Label = label,
            Config = config,
            Channels = channels,
            Increase = increase,
            DefaultBinding = defaultBinding
        };

        shortcutActions.Add(action);

        var button = new Button
        {
            Text = $"{label} ({FormatBinding(binding)})",
            Location = new Point(x, 2),
            Width = 180,
            Height = 30,
            Tag = action
        };

        button.Click += (_, _) =>
        {
            using var dialog = new ShortcutCaptureForm(label);

            joystickCaptureActive = true;
            DialogResult captureResult;
            try { captureResult = dialog.ShowDialog(this); }
            finally { joystickCaptureActive = false; }

            if (captureResult == DialogResult.OK)
            {
                savedShortcuts[settingKey] = dialog.Result;
                SaveSettings();

                RebuildAllShortcuts();

                status.Text =
                    $"{label}: {FormatBinding(dialog.Result)} saved.";
            }
        };

        panel.Controls.Add(button);
    }

    private string FormatBinding(ShortcutBinding binding)
    {
        if (binding.JoyButton > 0)
            return "Joy Btn " + binding.JoyButton;

        string result = "";

        if ((binding.Modifiers & MOD_CONTROL) != 0)
            result += "Ctrl + ";

        if ((binding.Modifiers & MOD_ALT) != 0)
            result += "Alt + ";

        if ((binding.Modifiers & MOD_SHIFT) != 0)
            result += "Shift + ";

        result += ((Keys)binding.Key).ToString();

        return result;
    }

    // ---------------------------------------------------------
    // HOTKEY REGISTRATION
    // ---------------------------------------------------------

    private void RebuildHotkeys()
    {
        UnregisterAllHotkeys();

        hotkeyActions.Clear();
        nextHotkeyId = HOTKEY_BASE;

        foreach (var action in shortcutActions)
        {
            ShortcutBinding binding =
                GetBinding(action.SettingKey, action.DefaultBinding);

            int id = nextHotkeyId++;

            hotkeyActions[id] = new HotkeyAction
            {
                Id = id,
                Key = binding.Key,
                Modifiers = binding.Modifiers,
                JoyButton = binding.JoyButton,
                JoyId = binding.JoyId ?? "",
                Config = action.Config,
                Channels = action.Channels,
                Increase = action.Increase,
                Description = action.Label,
                ActionType = action.ActionType
            };
        }

        RegisterAllHotkeys();
    }

    private void RegisterAllHotkeys()
    {
        if (!IsHandleCreated)
            return;

        foreach (var action in hotkeyActions.Values)
        {
            if (action.Key == 0) continue;
            bool registered = RegisterHotKey(
                Handle,
                action.Id,
                action.Modifiers,
                action.Key);

            if (registered)
            {
                registeredHotkeyIds.Add(action.Id);
            }
            else
            {
                status.Text =
                    "Shortcut unavailable or already used: " +
                    action.Description;
            }
        }
    }

    private void UnregisterAllHotkeys()
    {
        if (!IsHandleCreated)
            return;

        foreach (int id in registeredHotkeyIds)
            UnregisterHotKey(Handle, id);

        registeredHotkeyIds.Clear();
    }

    // ---------------------------------------------------------
    // VOLUME CONTROL
    // ---------------------------------------------------------

    private void SetChannelVolume(
        DeviceConfiguration config,
        int channel,
        int volume)
    {
        var group = config.Groups.Find(
            g => Array.IndexOf(g.Channels, channel) >= 0);

        int[] targets =
            group != null && group.Linked && group.Channels.Length > 1
                ? group.Channels
                : new[] { channel };

        updatingSliders = true;

        try
        {
            foreach (int target in targets)
            {
                float requested = volume / 100f;
                float actual = requested;
                if (nightModeActive)
                {
                    nightModeOriginalVolumes[GetNightVolumeKey(config, target)] = requested;
                    actual = requested * nightModePercent / 100f;
                }
                config.Device.AudioEndpointVolume.Channels[target].VolumeLevelScalar = actual;

                if (config.Controls.TryGetValue(target, out var control))
                {
                    int shown = Math.Clamp((int)Math.Round(actual * 100f), control.Slider.Minimum, control.Slider.Maximum);
                    control.Slider.Value = shown;
                    control.Value.Text = shown + "%";
                }
            }
        }
        catch (Exception ex)
        {
            status.Text = "Volume error: " + ex.Message;
        }

        updatingSliders = false;
    }

    private void ChangeVolume(
        DeviceConfiguration config,
        int[] channels,
        bool increase)
    {
        try
        {
            int current = (int)Math.Round(
                config.Device.AudioEndpointVolume.Channels[channels[0]]
                    .VolumeLevelScalar * 100);

            int next = Math.Clamp(
                current + (increase ? VOLUME_STEP : -VOLUME_STEP),
                0,
                100);

            updatingSliders = true;

            foreach (int channel in channels)
            {
                float requested = next / 100f;
                float actual = requested;
                if (nightModeActive)
                {
                    nightModeOriginalVolumes[GetNightVolumeKey(config, channel)] = requested;
                    actual = requested * nightModePercent / 100f;
                }
                config.Device.AudioEndpointVolume.Channels[channel].VolumeLevelScalar = actual;

                if (config.Controls.TryGetValue(channel, out var control))
                {
                    int shown = Math.Clamp((int)Math.Round(actual * 100f), control.Slider.Minimum, control.Slider.Maximum);
                    control.Slider.Value = shown;
                    control.Value.Text = shown + "%";
                }
            }

            updatingSliders = false;

            // Affiche l'overlay après chaque raccourci ou bouton de volume.
            ShowVolumeOverlay();
        }
        catch (Exception ex)
        {
            updatingSliders = false;
            status.Text = "Volume adjustment error: " + ex.Message;
        }
    }

    private void ShowVolumeOverlay()
    {
        if (volumeOverlay == null) return;
        try
        {
            var rows = new List<VolumeOverlay.VolumeRow>();
            foreach (var zone in new[] { ("BackGlass", "Back Glass"), ("SSR", "SSR"), ("L/R", "L / R"), ("Sub", "SUB"), ("Center", "Center"), ("SSF", "SSF") })
            {
                DeviceConfiguration? config = configurations.FirstOrDefault(c => GetMappedChannels(c, zone.Item1).Length > 0);
                if (config == null) continue;
                int[] channels = GetMappedChannels(config, zone.Item1);
                bool linked = IsCabinetZoneLinked(config, zone.Item1, channels);
                rows.Add(CreateOverlayRow(zone.Item2, config, channels, linked));
            }
            rows.Add(new VolumeOverlay.VolumeRow("Night Mode", nightModePercent, nightModePercent, false, false, true, true, nightModeActive));
            string[] shortcutLabels = new[]
            {
                GetGlobalShortcutDisplay("GLOBAL_SELECTOR"),
                GetGlobalShortcutDisplay("GLOBAL_NIGHT_MODE"),
                GetGlobalShortcutDisplay("GLOBAL_DOWN"),
                GetGlobalShortcutDisplay("GLOBAL_UP")
            };
            volumeOverlay.ShowVolume(rows, selectedTargetIndex >= 0 && selectedTargetIndex < selectableTargets.Count ? selectableTargets[selectedTargetIndex].Name : null, overlaySettings, shortcutLabels);
        }
        catch { }
    }

    private string GetGlobalShortcutDisplay(string key)
    {
        string settingKey = "GLOBAL|" + key;
        var action = shortcutActions.FirstOrDefault(x => x.SettingKey.Equals(settingKey, StringComparison.OrdinalIgnoreCase));
        if (action == null) return "—";
        ShortcutBinding binding = GetBinding(action.SettingKey, action.DefaultBinding);
        return binding.Key == 0 && binding.JoyButton == 0 ? "Not set" : FormatBinding(binding);
    }

    private bool IsPairLinked(DeviceConfiguration config, int left, int right)
    {
        AudioGroup? group = config.Groups.Find(g => g.Channels.Length == 2 && Array.IndexOf(g.Channels, left) >= 0 && Array.IndexOf(g.Channels, right) >= 0);
        return group?.Linked ?? false;
    }

    private VolumeOverlay.VolumeRow CreateOverlayRow(string name, DeviceConfiguration config, int[] channels, bool linked)
    {
        try
        {
            int left = (int)Math.Round(config.Device.AudioEndpointVolume.Channels[channels[0]].VolumeLevelScalar * 100);
            int right = channels.Length > 1 ? (int)Math.Round(config.Device.AudioEndpointVolume.Channels[channels[1]].VolumeLevelScalar * 100) : left;
            return new VolumeOverlay.VolumeRow(name, Math.Clamp(left, 0, 100), Math.Clamp(right, 0, 100), channels.Length > 1, linked, true);
        }
        catch { return new VolumeOverlay.VolumeRow(name, 0, 0, channels.Length > 1, linked, false); }
    }

    private void CycleVolumeSelection()
    {
        RebuildSelectableTargets();
        if (selectableTargets.Count == 0)
        {
            status.Text = "Volume Selector: no selectable output is available.";
            return;
        }

        selectedTargetIndex = (selectedTargetIndex + 1) % selectableTargets.Count;
        status.Text = "Selected target: " + selectableTargets[selectedTargetIndex].Name;
        ShowVolumeOverlay();
    }

    private void ChangeSelectedVolume(bool increase)
    {
        // Cette action ne passe jamais par la logique des groupes liés/directs :
        // seuls les canaux de la cible actuellement sélectionnée sont modifiés.
        RebuildSelectableTargets();
        if (selectableTargets.Count == 0)
        {
            status.Text = "Volume: no selectable output is available.";
            return;
        }
        if (selectedTargetIndex < 0 || selectedTargetIndex >= selectableTargets.Count)
        {
            status.Text = "Volume: select an output with Volume Selector first.";
            return;
        }

        SelectableVolumeTarget target = selectableTargets[selectedTargetIndex];
        if (target.IsNightMode)
        {
            SetNightMode(!nightModeActive);
            status.Text = "Night Mode " + (nightModeActive ? "ON" : "OFF") + " — " + nightModePercent + "%";
            ShowVolumeOverlay();
            return;
        }

        try
        {
            var endpointChannels = target.Config.Device.AudioEndpointVolume.Channels;
            int current = (int)Math.Round(endpointChannels[target.Channels[0]].VolumeLevelScalar * 100);
            int next = Math.Clamp(current + (increase ? VOLUME_STEP : -VOLUME_STEP), 0, 100);

            updatingSliders = true;
            foreach (int channel in target.Channels)
            {
                float actual = next / 100f;
                if (nightModeActive)
                {
                    string key = GetNightVolumeKey(target.Config, channel);
                    float baseline = nightModeOriginalVolumes.TryGetValue(key, out float saved) ? saved : endpointChannels[channel].VolumeLevelScalar;
                    float baselineNext = Math.Clamp(baseline * 100f + (increase ? VOLUME_STEP : -VOLUME_STEP), 0f, 100f) / 100f;
                    nightModeOriginalVolumes[key] = baselineNext;
                    actual = baselineNext * nightModePercent / 100f;
                    next = (int)Math.Round(actual * 100f);
                }
                endpointChannels[channel].VolumeLevelScalar = actual;
                if (target.Config.Controls.TryGetValue(channel, out ChannelControl? control))
                {
                    control.Slider.Value = Math.Clamp((int)Math.Round(actual * 100f), control.Slider.Minimum, control.Slider.Maximum);
                    control.Value.Text = Math.Clamp((int)Math.Round(actual * 100f), 0, 100) + "%";
                }
            }
            updatingSliders = false;
            status.Text = target.Name + " : " + next + "%";
            ShowVolumeOverlay();
        }
        catch (Exception ex)
        {
            updatingSliders = false;
            status.Text = "Selected volume error: " + ex.Message;
        }
    }

    private string GetNightVolumeKey(DeviceConfiguration config, int channel)
        => config.Device.ID + "|" + channel.ToString();

    private void SetNightMode(bool enabled)
    {
        if (enabled == nightModeActive) return;

        if (enabled)
        {
            var snapshot = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var config in configurations)
            {
                try
                {
                    var channels = config.Device.AudioEndpointVolume.Channels;
                    for (int i = 0; i < channels.Count; i++)
                        snapshot[GetNightVolumeKey(config, i)] = Math.Clamp(channels[i].VolumeLevelScalar, 0f, 1f);
                }
                catch (Exception ex)
                {
                    status.Text = "Night Mode could not read " + config.Device.FriendlyName + ": " + ex.Message;
                    return;
                }
            }

            if (snapshot.Count == 0)
            {
                status.Text = "Night Mode: no audio channels were captured; nothing changed.";
                return;
            }

            nightModeOriginalVolumes.Clear();
            foreach (var item in snapshot) nightModeOriginalVolumes[item.Key] = item.Value;
            nightModeActive = true;
            ApplyNightModeVolumes();
        }
        else
        {
            // Mark Night Mode OFF before touching sliders. Their ValueChanged handlers must not
            // interpret the restored baseline as a new Night Mode value and attenuate it again.
            nightModeActive = false;
            updatingSliders = true;
            try
            {
                foreach (var config in configurations)
                {
                    try
                    {
                        var channels = config.Device.AudioEndpointVolume.Channels;
                        for (int i = 0; i < channels.Count; i++)
                        {
                            string key = GetNightVolumeKey(config, i);
                            if (!nightModeOriginalVolumes.TryGetValue(key, out float original)) continue;

                            float restoreValue = Math.Clamp(original, 0f, 1f);
                            channels[i].VolumeLevelScalar = restoreValue;
                            UpdateChannelControl(config, i, restoreValue);
                        }
                    }
                    catch (Exception ex)
                    {
                        status.Text = "Night Mode restore warning: " + config.Device.FriendlyName + ": " + ex.Message;
                    }
                }
            }
            finally
            {
                updatingSliders = false;
            }
            nightModeOriginalVolumes.Clear();
        }

        if (nightModeButton != null)
        {
            nightModeButton.Text = nightModeActive ? "Night Mode: ON" : "Night Mode: OFF";
            nightModeButton.BackColor = nightModeActive ? Color.FromArgb(35, 145, 85) : SystemColors.Control;
        }
        ShowVolumeOverlay();
    }

    private void ApplyNightModeVolumes()
    {
        updatingSliders = true;
        try
        {
            foreach (var config in configurations)
            {
                var channels = config.Device.AudioEndpointVolume.Channels;
                for (int i = 0; i < channels.Count; i++)
                {
                    string key = GetNightVolumeKey(config, i);
                    if (!nightModeOriginalVolumes.TryGetValue(key, out float original))
                        continue;

                    // Always calculate from the captured baseline. Never multiply the current (already reduced) value.
                    float actual = Math.Clamp(original * (nightModePercent / 100f), 0f, 1f);
                    channels[i].VolumeLevelScalar = actual;
                    UpdateChannelControl(config, i, actual);
                }
            }
        }
        catch (Exception ex)
        {
            status.Text = "Night Mode apply error: " + ex.Message;
        }
        finally
        {
            updatingSliders = false;
        }
    }

    private void UpdateChannelControl(DeviceConfiguration config, int channel, float scalar)
    {
        if (!config.Controls.TryGetValue(channel, out ChannelControl? control)) return;
        int value = Math.Clamp((int)Math.Round(scalar * 100f), 0, 100);
        control.Slider.Value = Math.Clamp(value, control.Slider.Minimum, control.Slider.Maximum);
        control.Value.Text = value + "%";
    }

    // ---------------------------------------------------------
    // HELPERS
    // ---------------------------------------------------------

    private string KeyName(int key)
    {
        return ((Keys)key).ToString();
    }

    // ---------------------------------------------------------
    // JOYSTICK / PINSCAPE / OPENPINDEV BUTTONS
    // ---------------------------------------------------------

    private void ExecuteHotkeyAction(HotkeyAction action)
    {
        switch (action.ActionType)
        {
            case "Selector": CycleVolumeSelection(); break;
            case "SelectedUp": ChangeSelectedVolume(true); break;
            case "SelectedDown": ChangeSelectedVolume(false); break;
            case "NightMode":
                SetNightMode(!nightModeActive);
                status.Text = "Night Mode " + (nightModeActive ? "ON" : "OFF");
                break;
            default: ChangeVolume(action.Config, action.Channels, action.Increase); break;
        }
    }

    private void PollJoystickButtons()
    {
        var current = new Dictionary<string, uint>();
        foreach (var state in JoystickInput.Poll())
        {
            joyPrevButtons.TryGetValue(state.DevId, out uint previous);
            current[state.DevId] = state.Buttons;
            uint pressed = state.Buttons & ~previous;
            if (pressed == 0 || joystickCaptureActive) continue;

            for (int bit = 0; bit < 32; bit++)
            {
                if ((pressed & (1u << bit)) == 0) continue;
                int button = bit + 1;
                foreach (var action in hotkeyActions.Values.ToList())
                {
                    if (action.JoyButton == button &&
                        string.Equals(action.JoyId, state.DevId, StringComparison.OrdinalIgnoreCase))
                        ExecuteHotkeyAction(action);
                }
            }
        }
        joyPrevButtons = current;
    }

    // ---------------------------------------------------------
    // GLOBAL KEYBOARD EVENTS
    // ---------------------------------------------------------

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();

            if (hotkeyActions.TryGetValue(id, out var action))
            {
                ExecuteHotkeyAction(action);
                return;
            }
        }

        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterAllHotkeys();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterAllHotkeys();
        base.OnHandleDestroyed(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exitFromTrayMenu && appSettings.MinimizeToTrayOnClose && appSettings.ShowInTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            trayIcon?.ShowBalloonTip(1500, "Nutho313 PinAudio", "The application is still running in the notification area.", ToolTipIcon.Info);
            return;
        }
        if (nightModeActive) SetNightMode(false);
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        joystickTimer?.Stop();
        joystickTimer?.Dispose();
        UnregisterAllHotkeys();
        if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; }

        volumeOverlay?.Dispose();

        foreach (var device in devices)
            device.Dispose();

        enumerator.Dispose();

        base.OnFormClosed(e);
    }

    // ---------------------------------------------------------
    // JOYSTICK READER (winmm) - HID gamepads incl. Pinscape / OpenPinDev
    // ---------------------------------------------------------

    private static class JoystickInput
    {
        public sealed class JoyState
        {
            public string DevId = "";
            public string Name = "";
            public uint Buttons;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOYINFOEX
        {
            public uint dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos;
            public uint dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JOYCAPS
        {
            public ushort wMid, wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax;
            public uint wNumButtons, wPeriodMin, wPeriodMax;
            public uint wRmin, wRmax, wUmin, wUmax, wVmin, wVmax;
            public uint wCaps, wMaxAxes, wNumAxes, wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
        }

        [DllImport("winmm.dll")]
        private static extern int joyGetNumDevs();

        [DllImport("winmm.dll")]
        private static extern int joyGetPosEx(int joyId, ref JOYINFOEX info);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "joyGetDevCapsW")]
        private static extern int joyGetDevCaps(IntPtr joyId, ref JOYCAPS caps, int size);

        private const uint JOY_RETURNBUTTONS = 0x00000080;

        public static List<JoyState> Poll()
        {
            var result = new List<JoyState>();
            try
            {
                int count = Math.Min(joyGetNumDevs(), 16);
                for (int id = 0; id < count; id++)
                {
                    var info = new JOYINFOEX
                    {
                        dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(),
                        dwFlags = JOY_RETURNBUTTONS
                    };
                    if (joyGetPosEx(id, ref info) != 0) continue; // not connected

                    var caps = new JOYCAPS();
                    string devId = "joy" + id;
                    string name = "Joystick " + id;
                    if (joyGetDevCaps((IntPtr)id, ref caps, Marshal.SizeOf<JOYCAPS>()) == 0)
                    {
                        devId = caps.wMid.ToString("X4") + ":" + caps.wPid.ToString("X4");
                        name = caps.szPname ?? name;
                    }
                    result.Add(new JoyState { DevId = devId, Name = name, Buttons = info.dwButtons });
                }
            }
            catch { }
            return result;
        }
    }

    // ---------------------------------------------------------
    // SHORTCUT CAPTURE WINDOW
    // ---------------------------------------------------------

    private class ShortcutCaptureForm : Form
    {
        public ShortcutBinding Result { get; private set; } = new();

        private readonly System.Windows.Forms.Timer joyTimer = new() { Interval = 25 };
        private Dictionary<string, uint> joyPrev = new();

        public ShortcutCaptureForm(string actionName)
        {
            Text = "Configure shortcut";
            Size = new Size(440, 230);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;

            var instruction = new Label
            {
                Text =
                    $"New shortcut for {actionName}\n\n" +
                    "Press your desired key or combination.\n" +
                    "Example: F2, Ctrl + F2, Ctrl + Shift + A\n" +
                    "Or press a button on your Pinscape / OpenPinDev controller.\n\n" +
                    "Press Escape to cancel.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10)
            };

            Controls.Add(instruction);

            KeyDown += CaptureKey;

            // Baseline: buttons already held when the dialog opens are ignored.
            foreach (var s in JoystickInput.Poll()) joyPrev[s.DevId] = s.Buttons;
            joyTimer.Tick += (_, _) => PollJoystick();
            joyTimer.Start();
        }

        private void PollJoystick()
        {
            var current = new Dictionary<string, uint>();
            foreach (var s in JoystickInput.Poll())
            {
                joyPrev.TryGetValue(s.DevId, out uint previous);
                current[s.DevId] = s.Buttons;
                uint pressed = s.Buttons & ~previous;
                if (pressed == 0) continue;

                int bit = 0;
                while ((pressed & (1u << bit)) == 0) bit++;
                Result = new ShortcutBinding { JoyButton = bit + 1, JoyId = s.DevId };
                joyTimer.Stop();
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            joyPrev = current;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            joyTimer.Stop();
            joyTimer.Dispose();
            base.OnFormClosed(e);
        }

        private void CaptureKey(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            if (e.KeyCode == Keys.ControlKey ||
                e.KeyCode == Keys.ShiftKey ||
                e.KeyCode == Keys.Menu ||
                e.KeyCode == Keys.LWin ||
                e.KeyCode == Keys.RWin)
                return;

            uint modifiers = 0;

            if (e.Control)
                modifiers |= MOD_CONTROL;

            if (e.Alt)
                modifiers |= MOD_ALT;

            if (e.Shift)
                modifiers |= MOD_SHIFT;

            Result = new ShortcutBinding
            {
                Key = (uint)e.KeyCode,
                Modifiers = modifiers
            };

            DialogResult = DialogResult.OK;
            Close();

            e.SuppressKeyPress = true;
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------
    // VOLUME OVERLAY
    // ---------------------------------------------------------

    private class VolumeOverlay : Form
    {
        public sealed class VolumeRow
        {
            public string Name { get; }
            public int Left { get; }
            public int Right { get; }
            public bool IsStereo { get; }
            public bool Linked { get; }
            public bool Available { get; }
            public bool IsNightMode { get; }
            public bool NightModeActive { get; }
            public VolumeRow(string name, int left, int right, bool isStereo, bool linked, bool available, bool isNightMode = false, bool nightModeActive = false)
            { Name = name; Left = left; Right = right; IsStereo = isStereo; Linked = linked; Available = available; IsNightMode = isNightMode; NightModeActive = nightModeActive; }
        }

        private readonly System.Windows.Forms.Timer hideTimer;
        private IReadOnlyList<VolumeRow> currentRows = Array.Empty<VolumeRow>();
        private string? selectedTarget;
        private IReadOnlyList<string> shortcutLabels = Array.Empty<string>();
        private const int LogicalWidth = 480;
        private const int LogicalHeight = 670;

        public VolumeOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(17, 29, 42);
            Opacity = 0.97;
            // La fenêtre est portrait ; le contenu est tourné de 180° par rapport à la version précédente.
            ClientSize = new Size(LogicalHeight, LogicalWidth);
            hideTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            hideTimer.Tick += (_, _) => { hideTimer.Stop(); Hide(); };
            DoubleBuffered = true;
        }

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080;
                return cp;
            }
        }

        private OverlayDisplaySettings displaySettings = new();
        private int scalePercent = 100;
        private int renderScalePercent = 100;

        public void Configure(int durationSeconds, int opacityPercent, int sizePercent, OverlayDisplaySettings settings)
        {
            hideTimer.Interval = Math.Clamp(durationSeconds, 1, 30) * 1000;
            Opacity = Math.Clamp(opacityPercent, 30, 100) / 100.0;
            displaySettings = settings;

            // The slider is the desired percentage of the selected display width.
            // Convert it to the overlay's logical scale, then let placement clamp it to the display bounds.
            Screen? target = Screen.AllScreens.FirstOrDefault(s =>
                s.DeviceName.Equals(settings.ScreenDeviceName, StringComparison.OrdinalIgnoreCase));
            target ??= Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position);
            bool quarterTurn = displaySettings.Orientation == 90 || displaySettings.Orientation == 270;
            int orientedLogicalWidth = quarterTurn ? LogicalHeight : LogicalWidth;
            scalePercent = Math.Clamp(sizePercent, 10, 90) * target.Bounds.Width / Math.Max(1, orientedLogicalWidth);
            scalePercent = Math.Clamp(scalePercent, 10, 900);
            renderScalePercent = scalePercent;
            LogicalSizeChanged(scalePercent);
        }

        private void LogicalSizeChanged(int scalePercent)
        {
            int w = LogicalWidth * scalePercent / 100;
            int h = LogicalHeight * scalePercent / 100;
            bool quarterTurn = displaySettings.Orientation == 90 || displaySettings.Orientation == 270;
            Size = quarterTurn ? new Size(h, w) : new Size(w, h);
            Region?.Dispose();
            Region = new Region(RoundedPath(new Rectangle(0, 0, Width, Height), 20));
        }

        public void ShowVolume(IReadOnlyList<VolumeRow> rows, string? selected, OverlayDisplaySettings settings, IReadOnlyList<string>? shortcuts = null)
        {
            if (IsDisposed) return;
            currentRows = rows;
            selectedTarget = selected;
            shortcutLabels = shortcuts ?? Array.Empty<string>();
            displaySettings = settings;
            PlaceOnPlayfieldDisplay();
            Invalidate();
            hideTimer.Stop();
            if (!Visible) Show();
            BringToFront();
            hideTimer.Start();
        }

        private void PlaceOnPlayfieldDisplay()
        {
            Screen? target = Screen.AllScreens.FirstOrDefault(s => s.DeviceName.Equals(displaySettings.ScreenDeviceName, StringComparison.OrdinalIgnoreCase));
            target ??= Screen.AllScreens.FirstOrDefault(s => s.DeviceName.EndsWith("DISPLAY2", StringComparison.OrdinalIgnoreCase));
            target ??= Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position);
            Rectangle area = target.Bounds;
            bool quarterTurn = displaySettings.Orientation == 90 || displaySettings.Orientation == 270;
            int effectiveScale = scalePercent;
            int logicalDisplayWidth = quarterTurn ? LogicalHeight : LogicalWidth;
            int logicalDisplayHeight = quarterTurn ? LogicalWidth : LogicalHeight;
            double fit = Math.Min(area.Width / (double)logicalDisplayWidth, area.Height / (double)logicalDisplayHeight);
            if (effectiveScale / 100.0 > fit) effectiveScale = Math.Max(35, (int)Math.Floor(fit * 100.0));
            renderScalePercent = effectiveScale;
            int scaledWidth = LogicalWidth * effectiveScale / 100;
            int scaledHeight = LogicalHeight * effectiveScale / 100;
            Size = quarterTurn ? new Size(scaledHeight, scaledWidth) : new Size(scaledWidth, scaledHeight);
            Region?.Dispose();
            Region = new Region(RoundedPath(new Rectangle(0, 0, Width, Height), 20));
            int margin = 24;
            int x = displaySettings.Position switch
            {
                "Center" => area.Left + (area.Width - Width) / 2,
                "Right" => area.Right - Width - margin,
                _ => area.Left + margin
            };
            int y = displaySettings.Position switch
            {
                "Center" => area.Top + (area.Height - Height) / 2,
                "Bottom" => area.Bottom - Height - margin,
                "Left" or "Right" => area.Top + (area.Height - Height) / 2,
                _ => area.Top + margin
            };
            x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - Width));
            y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - Height));
            Location = new Point(x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            switch (displaySettings.Orientation)
            {
                case 0: break;
                case 90: g.TranslateTransform(ClientSize.Width, 0); g.RotateTransform(90f); break;
                case 180: g.TranslateTransform(ClientSize.Width, ClientSize.Height); g.RotateTransform(180f); break;
                default: g.TranslateTransform(0, ClientSize.Height); g.RotateTransform(270f); break;
            }
            g.ScaleTransform(renderScalePercent / 100f, renderScalePercent / 100f);

            Rectangle outer = new Rectangle(3, 3, LogicalWidth - 6, LogicalHeight - 6);
            using (var clipPath = RoundedPath(outer, 20))
                g.SetClip(clipPath);
            using (var bg = new SolidBrush(Color.FromArgb(247, 10, 22, 34))) FillRounded(g, bg, outer, 20);
            using (var border = new Pen(Color.FromArgb(70, 145, 190), 3)) DrawRounded(g, border, outer, 20);
            using var titleFont = new Font("Segoe UI", 15, FontStyle.Bold);
            using var zoneFont = new Font("Segoe UI", 9, FontStyle.Bold);
            using var smallFont = new Font("Segoe UI", 7, FontStyle.Bold);
            using var white = new SolidBrush(Color.White);
            using var muted = new SolidBrush(Color.FromArgb(180, 200, 215));
            using var titleBrush = new SolidBrush(Color.FromArgb(120, 190, 230));
            // The overlay itself is rotated 270°; drawing along the logical X axis
            // makes the title appear vertical (90°) on the physical screen.
            SizeF titleSize = g.MeasureString("NUTHO313 PINAUDIO", titleFont);
            g.DrawString("NUTHO313 PINAUDIO", titleFont, titleBrush, new PointF((LogicalWidth - titleSize.Width) / 2f, 10));

            // Top-down cabinet: the backglass is a separate compartment behind the playfield.
            // The two backglass speakers are placed at the rear corners. No extra blue BACK GLASS
            // or PLAYFIELD captions are drawn over the cabinet.
            using var cabinetPen = new Pen(Color.FromArgb(135, 170, 195), 2.2f);
            using var playfieldFill = new SolidBrush(Color.FromArgb(255, 28, 39, 53));

            Rectangle backglass = new Rectangle(24, 52, 432, 132);
            FillRounded(g, playfieldFill, backglass, 16);
            DrawRounded(g, cabinetPen, backglass, 16);
            using (var brandLogo = LoadBrandLogo())
            {
                if (brandLogo != null)
                {
                    g.DrawImage(brandLogo, new Rectangle(44, 60, 44, 44));
                    g.DrawImage(brandLogo, new Rectangle(392, 60, 44, 44));
                }
            }

            Rectangle cabinet = new Rectangle(24, 192, 432, 365);
            FillRounded(g, playfieldFill, cabinet, 24);
            DrawRounded(g, cabinetPen, cabinet, 24);

            VolumeRow? back = currentRows.FirstOrDefault(r => r.Name.Contains("Back", StringComparison.OrdinalIgnoreCase));
            VolumeRow? ssr = currentRows.FirstOrDefault(r => r.Name.Contains("SSR", StringComparison.OrdinalIgnoreCase) || r.Name.Contains("Rear", StringComparison.OrdinalIgnoreCase));
            VolumeRow? lr = currentRows.FirstOrDefault(r => r.Name.Contains("L / R", StringComparison.OrdinalIgnoreCase));
            VolumeRow? sub = currentRows.FirstOrDefault(r => r.Name.Contains("SUB", StringComparison.OrdinalIgnoreCase));
            VolumeRow? center = currentRows.FirstOrDefault(r => r.Name.Contains("Center", StringComparison.OrdinalIgnoreCase));
            VolumeRow? ssf = currentRows.FirstOrDefault(r => r.Name.Contains("SSF", StringComparison.OrdinalIgnoreCase) || r.Name.Contains("Front", StringComparison.OrdinalIgnoreCase));

            DrawPairZone(g, back, "BACK GLASS", 240, 128, selectedTarget, Color.FromArgb(255, 157, 91, 0), zoneFont, smallFont, white);
            DrawPairZone(g, ssr, "SSR", 240, 225, selectedTarget, Color.FromArgb(235, 55, 40), zoneFont, smallFont, white);
            DrawPairZone(g, lr, "L / R", 240, 295, selectedTarget, Color.FromArgb(0, 155, 235), zoneFont, smallFont, white);
            DrawZone(g, sub, "SUB", 240, 365, 330, selectedTarget, Color.FromArgb(170, 65, 220), zoneFont, smallFont, white);
            DrawZone(g, center, "CENTER", 240, 435, 330, selectedTarget, Color.FromArgb(35, 195, 75), zoneFont, smallFont, white);
            DrawPairZone(g, ssf, "SSF", 240, 505, selectedTarget, Color.FromArgb(235, 205, 0), zoneFont, smallFont, white);
            VolumeRow? night = currentRows.FirstOrDefault(r => r.IsNightMode);
            DrawNightModeToggle(g, night, selectedTarget == "Night Mode", zoneFont, smallFont);
            DrawShortcutLegend(g, shortcutLabels, smallFont, muted);
            g.ResetTransform();
        }

        private static void DrawNightModeToggle(Graphics g, VolumeRow? row, bool selected, Font titleFont, Font smallFont)
        {
            if (row == null) return;
            Rectangle toggle = new Rectangle(330, 585, 130, 24);
            Color accent = row.NightModeActive ? Color.FromArgb(35, 205, 105) : Color.FromArgb(105, 125, 145);
            using (var fill = new SolidBrush(Color.FromArgb(235, 20, 34, 48))) FillRounded(g, fill, toggle, 12);
            using (var border = new Pen(selected ? Color.FromArgb(253, 137, 13) : accent, selected ? 3 : 1.8f)) DrawRounded(g, border, toggle, 12);
            Rectangle knob = row.NightModeActive
                ? new Rectangle(toggle.Right - 25, toggle.Y + 3, 18, 18)
                : new Rectangle(toggle.X + 7, toggle.Y + 3, 18, 18);
            using (var knobFill = new SolidBrush(row.NightModeActive ? accent : Color.FromArgb(120, 140, 155))) g.FillEllipse(knobFill, knob);
            string state = row.NightModeActive ? "ON" : "OFF";
            string text = "NIGHT MODE  " + state + "  " + row.Left + "%";
            SizeF size = g.MeasureString(text, smallFont);
            using var textBrush = new SolidBrush(Color.White);
            g.DrawString(text, smallFont, textBrush, toggle.X + (toggle.Width - size.Width) / 2f, toggle.Y - 19);
        }

        private static void DrawShortcutLegend(Graphics g, IReadOnlyList<string> shortcuts, Font font, Brush brush)
        {
            string[] names = { "⟳ CHANNEL SELECTOR", "☾ NIGHT MODE", "VOLUME DOWN", "VOLUME UP" };
            // Horizontal text at the bottom of the overlay, below the sliders: 2 columns x 2 rows.
            float[] colCenters = { 132f, 348f };
            float[] rowTops = { 620f, 638f };

            for (int i = 0; i < names.Length; i++)
            {
                string key = i < shortcuts.Count ? shortcuts[i] : "—";
                string line = names[i] + " [" + key + "]";
                DrawCenteredText(g, line, font, brush, colCenters[i % 2], rowTops[i / 2]);
            }
        }

        private static void DrawCenteredText(Graphics g, string text, Font font, Brush brush, float centerX, float y)
        {
            SizeF size = g.MeasureString(text, font);
            g.DrawString(text, font, brush, centerX - size.Width / 2f, y);
        }

        private static void DrawZone(Graphics g, VolumeRow? row, string label, int centerX, int y, int width, string? selected, Color color, Font labelFont, Font smallFont, Brush white)
        {
            if (row == null) return;
            bool isSelected = selected == row.Name || selected == row.Name + " L / R" || selected == label || selected == label + " L / R";
            int value = row.IsStereo ? (row.Left + row.Right) / 2 : row.Left;
            Rectangle labelRect = new Rectangle(centerX - width / 2, y - 15, width, 22);
            using (var fill = new SolidBrush(Color.FromArgb(210, color))) FillRounded(g, fill, labelRect, 7);
            using (var pen = new Pen(isSelected ? Color.FromArgb(253, 137, 13) : color, isSelected ? 3 : 1.5f)) DrawRounded(g, pen, labelRect, 7);
            using var textBrush = new SolidBrush(Color.White);
            SizeF size = g.MeasureString(label, labelFont);
            g.DrawString(label, labelFont, textBrush, centerX - size.Width / 2, y - 13);
            DrawCenteredBar(g, value, centerX, y + 12, width - 10, isSelected, color, smallFont, white);
        }

        private static void DrawPairZone(Graphics g, VolumeRow? row, string label, int centerX, int y, string? selected, Color color, Font labelFont, Font smallFont, Brush white)
        {
            if (row == null) return;
            bool linked = row.Linked;
            if (linked || !row.IsStereo)
            {
                bool selectedPair = selected == row.Name || selected == row.Name + " L / R";
                int value = (row.Left + row.Right) / 2;
                // Linked L/R: one bar centered between the physical left/right speakers.
                DrawZone(g, row, label + " L / R", centerX, y, 330, selectedPair ? selected : null, color, labelFont, smallFont, white);
                return;
            }
            // Unlinked: same label as every other zone; L and R bars side by side across the cabinet width.
            Rectangle titleRect = new Rectangle(centerX - 165, y - 15, 330, 22);
            using (var titleFill = new SolidBrush(Color.FromArgb(210, color))) FillRounded(g, titleFill, titleRect, 7);
            using (var titleBorder = new Pen(color, 1.5f)) DrawRounded(g, titleBorder, titleRect, 7);
            SizeF titleSize = g.MeasureString(label, labelFont);
            g.DrawString(label, labelFont, white, centerX - titleSize.Width / 2f, y - 13);
            DrawSideBar(g, row.Left, centerX - 90, y + 24, selected == row.Name + " L", color, smallFont, white, "L");
            DrawSideBar(g, row.Right, centerX + 90, y + 24, selected == row.Name + " R", color, smallFont, white, "R");
        }

        private static void DrawCenteredBar(Graphics g, int value, int centerX, int y, int width, bool selected, Color color, Font font, Brush white)
        {
            Rectangle bar = new Rectangle(centerX - width / 2, y, width, 18);
            using (var track = new SolidBrush(Color.FromArgb(55, 69, 82))) FillRounded(g, track, bar, 5);
            int fillWidth = Math.Clamp((int)(width * Math.Clamp(value, 0, 100) / 100.0), 0, width);
            if (fillWidth > 0) { using var fill = new SolidBrush(selected ? Color.FromArgb(253, 137, 13) : color); FillRounded(g, fill, new Rectangle(bar.X, bar.Y, fillWidth, bar.Height), 5); }
            using (var outline = new Pen(selected ? Color.FromArgb(253, 137, 13) : color, selected ? 2.5f : 1)) DrawRounded(g, outline, bar, 5);
            g.DrawString(Math.Clamp(value, 0, 100) + "%", font, white, new PointF(bar.Right + 4, y - 2));
        }

        private static void DrawSideBar(Graphics g, int value, int centerX, int y, bool selected, Color color, Font font, Brush white, string side)
        {
            g.DrawString(side, font, white, new PointF(centerX - 4, y - 12));
            DrawCenteredBar(g, value, centerX, y + 2, 140, selected, color, font, white);
        }

        private static void FillRounded(Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using var path = RoundedPath(rect, radius);
            g.FillPath(brush, path);
        }

        private static void DrawRounded(Graphics g, Pen pen, Rectangle rect, int radius)
        {
            using var path = RoundedPath(rect, radius);
            g.DrawPath(pen, path);
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle rect, int radius)
        {
            // Clamp corner radius for very narrow fills; otherwise the arcs can extend
            // beyond the small fill rectangle and leave visible stray pixels.
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            if (rect.Width <= 2 || rect.Height <= 2)
            {
                path.AddRectangle(rect);
                return path;
            }
            radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawBar(Graphics g, string channel, int value, int x, int y, int width, bool selected, Font font, Brush textBrush)
        {
            if (channel.Length > 0) g.DrawString(channel, font, textBrush, new PointF(x, y - 2));
            int labelWidth = channel.Length > 0 ? 22 : 0;
            int percentWidth = 38;
            int barX = x + labelWidth;
            int barWidth = width - labelWidth - percentWidth - 6;
            Rectangle bar = new Rectangle(barX, y, barWidth, 13);
            using (var track = new SolidBrush(Color.FromArgb(55, 69, 82))) FillRounded(g, track, bar, 5);
            int fillWidth = Math.Clamp((int)(barWidth * Math.Clamp(value, 0, 100) / 100.0), 0, barWidth);
            if (fillWidth > 0)
            {
                using var fill = new SolidBrush(selected ? Color.FromArgb(253, 137, 13) : Color.FromArgb(70, 165, 220));
                FillRounded(g, fill, new Rectangle(bar.X, bar.Y, fillWidth, bar.Height), 5);
            }
            using (var outline = new Pen(selected ? Color.FromArgb(253, 137, 13) : Color.FromArgb(95, 125, 145), selected ? 2 : 1)) DrawRounded(g, outline, bar, 5);
            g.DrawString(Math.Clamp(value, 0, 100) + "%", font, textBrush, new PointF(x + width - percentWidth, y - 2));
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            hideTimer.Stop();
            hideTimer.Dispose();
            base.OnFormClosed(e);
        }
    }

}
