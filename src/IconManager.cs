using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace TaskbarMediaControls;

public enum IconType {
    Previous,
    Next,
    Play,
    Pause,
}

public static class IconManager {
    private static readonly string PrevIcon = "TaskbarMediaControls.Resources.back.ico";
    private static readonly string PlayIcon = "TaskbarMediaControls.Resources.play.ico";
    private static readonly string PauseIcon = "TaskbarMediaControls.Resources.pause.ico";
    private static readonly string NextIcon = "TaskbarMediaControls.Resources.skip.ico";


    private static readonly string PrevIcon_Light = "TaskbarMediaControls.Resources.back_light.ico";
    private static readonly string PlayIcon_Light = "TaskbarMediaControls.Resources.play_light.ico";
    private static readonly string PauseIcon_Light = "TaskbarMediaControls.Resources.pause_light.ico";
    private static readonly string NextIcon_Light = "TaskbarMediaControls.Resources.skip_light.ico";

    private static string GetIconPathForType(IconType type, bool isDarkMode) {

        switch (type) {
            case IconType.Previous:
                return isDarkMode ? PrevIcon : PrevIcon_Light;
            case IconType.Next:
                return isDarkMode ? NextIcon : NextIcon_Light;
            case IconType.Play:
                return isDarkMode ? PlayIcon : PlayIcon_Light;
            case IconType.Pause:
                return isDarkMode ? PauseIcon : PauseIcon_Light;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    private static bool IsSystemDarkMode() {
        try {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            Debug.WriteLine($"System dark mode: {val}");
            return val is int i && i == 0;
        } catch {
            return true;
        }
    }

    public static Icon LoadIcon(IconType type) {
        return LoadIcon(type, IsSystemDarkMode());
    }

    public static Icon LoadIcon(IconType type, bool isDarkMode) {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(GetIconPathForType(type, isDarkMode));
        if (stream == null)
            throw new Exception($"Resource not found: {type}");

        return new Icon(stream);
    }
}