using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace TaskbarMediaControls;

public static class VlcHotkeyManager {
    public static readonly IReadOnlyDictionary<string, string> RequiredHotkeys =
        new Dictionary<string, string>(StringComparer.Ordinal) {
            { "global-key-play-pause", "Media Play Pause" },
            { "global-key-next",       "Media Next Track" },
            { "global-key-prev",       "Media Prev Track" },
        };

    public static string VlcrcPath { get; } = BuildVlcrcPath();

    public static bool IsVlcInstalled =>
        !string.IsNullOrEmpty(VlcrcPath) && File.Exists(VlcrcPath);

    public static bool NeedsConfiguration => IsVlcInstalled && !AreHotkeysConfigured();

    public static bool AreHotkeysConfigured() {
        if (!IsVlcInstalled)
            return false;

        string text;
        try {
            text = File.ReadAllText(VlcrcPath);
        } catch { return false; }

        foreach (var kvp in RequiredHotkeys) {
            string pattern = @"(?m)^[ \t]*" + Regex.Escape(kvp.Key) + "=" + Regex.Escape(kvp.Value) + @"[ \t]*\r?$";
            if (!Regex.IsMatch(text, pattern))
                return false;
        }

        return true;
    }

    public static bool IsVlcRunning() {
        try {
            return Process.GetProcessesByName("vlc").Length > 0;
        } catch { return false; }
    }

    public static void ApplyHotkeys() {
        if (!IsVlcInstalled)
            throw new FileNotFoundException("VLC's configuration file was not found.", VlcrcPath);

        CloseVlc();

        File.Copy(VlcrcPath, VlcrcPath + ".bak", overwrite: true);

        string text = File.ReadAllText(VlcrcPath);

        foreach (var kvp in RequiredHotkeys) {
            string replacement = kvp.Key + "=" + kvp.Value;
            string pattern = @"(?m)^#?[ \t]*" + Regex.Escape(kvp.Key) + "=.*$";

            if (Regex.IsMatch(text, pattern)) {
                text = Regex.Replace(text, pattern, replacement);
            } else {
                // Key missing - add it
                text = text.TrimEnd() + Environment.NewLine + replacement + Environment.NewLine;
            }
        }

        File.WriteAllText(VlcrcPath, text, new UTF8Encoding(false));
    }

    private static string BuildVlcrcPath() {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrEmpty(appData) ? string.Empty : Path.Combine(appData, "vlc", "vlcrc");
    }

    private static void CloseVlc() {
        foreach (Process process in Process.GetProcessesByName("vlc")) {
            try {
                // Graceful first so VLC can save its state; force-kill if it ignores asking nicely.
                if (!process.CloseMainWindow() || !process.WaitForExit(3000))
                    process.Kill();

                process.WaitForExit(3000);
            } catch { 
                /* Already gone or access denied */ 
            } finally { 
                process.Dispose(); 
            }
        }
    }
}
