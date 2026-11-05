using Microsoft.Win32;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TaskbarMediaControls;

internal static class NotifyIconSettings {
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";

    private const int CompareSize = 32;
    private const long MaxMeanDifference = 12;

    private readonly record struct Candidate(string Tooltip, int Uid, Bitmap Image);

    public static bool IsHidden(string exePath) {
        var exeName = Path.GetFileName(exePath);
        bool found = false;
        bool allHidden = true;

        try {
            using var baseKey = Registry.CurrentUser.OpenSubKey(SettingsKey);
            if (baseKey == null)
                return false;

            foreach (string subkeyName in baseKey.GetSubKeyNames()) {
                using var entry = baseKey.OpenSubKey(subkeyName);
                if (entry == null)
                    continue;

                string? path = entry.GetValue("ExecutablePath") as string;
                if (path == null || !Matches(path, exeName))
                    continue;

                if (entry.GetValue("IsPromoted") == null)
                    continue;

                found = true;
                if (Equals(entry.GetValue("IsPromoted"), 1))
                    allHidden = false;
            }
        } catch {
            return false;
        }

        return found && allHidden;
    }

    public static void EnsureEntries(string exePath, string[] tooltips, bool hidden) {
        try {
            PopulateMissingEntries(exePath, tooltips, hidden);
            PromoteEntries(exePath, hidden);
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"NotifyIconSettings: {ex.Message}");
        }
    }

    private static void PopulateMissingEntries(string exePath, string[] tooltips, bool hidden) {
        var candidates = BuildCandidates(tooltips);
        if (candidates.Count == 0)
            return;

        try {
            using var baseKey = Registry.CurrentUser.OpenSubKey(SettingsKey, writable: true);
            if (baseKey == null)
                return;

            foreach (string subkeyName in baseKey.GetSubKeyNames()) {
                using var entry = baseKey.OpenSubKey(subkeyName, writable: true);
                if (entry == null)
                    continue;

                if (entry.GetValue("ExecutablePath") != null)
                    continue;

                if (entry.GetValue("IconSnapshot") is not byte[] snapshot)
                    continue;

                Candidate? match = MatchSnapshot(snapshot, candidates);
                if (match == null)
                    continue;

                entry.SetValue("ExecutablePath", exePath, RegistryValueKind.String);
                entry.SetValue("UID", match.Value.Uid, RegistryValueKind.DWord);
                entry.SetValue("InitialTooltip", match.Value.Tooltip, RegistryValueKind.String);
                entry.SetValue("IsPromoted", hidden ? 0 : 1, RegistryValueKind.DWord);
            }
        } finally {
            foreach (var candidate in candidates)
                candidate.Image.Dispose();
        }
    }

    private static void PromoteEntries(string exePath, bool hidden) {
        var exeName = Path.GetFileName(exePath);
        int value = hidden ? 0 : 1;

        using var baseKey = Registry.CurrentUser.OpenSubKey(SettingsKey, writable: true);
        if (baseKey == null)
            return;

        foreach (string subkeyName in baseKey.GetSubKeyNames()) {
            using var entry = baseKey.OpenSubKey(subkeyName, writable: true);
            if (entry == null)
                continue;

            string? path = entry.GetValue("ExecutablePath") as string;
            if (path == null || !Matches(path, exeName))
                continue;

            if (entry.GetValue("IsPromoted") != null)
                continue;

            entry.SetValue("IsPromoted", value, RegistryValueKind.DWord);
        }
    }

    private static bool Matches(string path, string exeName) =>
        string.Equals(Path.GetFileName(path), exeName, StringComparison.OrdinalIgnoreCase);

    private static List<Candidate> BuildCandidates(string[] tooltips) {
        var candidates = new List<Candidate>();

        foreach (bool dark in new[] { true, false }) {
            foreach (IconType type in Enum.GetValues<IconType>()) {
                string tooltip;
                int uid;

                switch (type) {
                    case IconType.Previous:
                        tooltip = tooltips[0];
                        uid = 1;
                        break;
                    case IconType.Next:
                        tooltip = tooltips[2];
                        uid = 3;
                        break;
                    case IconType.Play:
                    case IconType.Pause:
                        tooltip = tooltips[1];
                        uid = 2;
                        break;
                    default:
                        continue;
                }

                try {
                    using var icon = IconManager.LoadIcon(type, dark);
                    candidates.Add(new Candidate(tooltip, uid, RenderIcon(icon)));
                } catch (Exception ex) {
                    System.Diagnostics.Debug.WriteLine($"NotifyIconSettings: {type}/{dark}: {ex.Message}");
                }
            }
        }

        return candidates;
    }

    private static Candidate? MatchSnapshot(byte[] snapshot, List<Candidate> candidates) {
        using var image = DecodeSnapshot(snapshot);
        if (image == null)
            return null;

        Candidate? best = null;
        long bestScore = long.MaxValue;

        foreach (var candidate in candidates) {
            long score = Difference(image, candidate.Image);
            if (score < bestScore) {
                bestScore = score;
                best = candidate;
            }
        }

        long mean = bestScore / (CompareSize * CompareSize * 4L);
        return mean <= MaxMeanDifference ? best : null;
    }

    private static Bitmap? DecodeSnapshot(byte[] png) {
        try {
            using var stream = new MemoryStream(png);
            using var image = Image.FromStream(stream);
            return Resize(image, CompareSize, CompareSize);
        } catch {
            return null;
        }
    }

    private static Bitmap RenderIcon(Icon icon) {
        var bitmap = new Bitmap(CompareSize, CompareSize, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap)) {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawIcon(icon, new Rectangle(0, 0, CompareSize, CompareSize));
        }
        return bitmap;
    }

    private static Bitmap Resize(Image image, int width, int height) {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap)) {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, 0, 0, width, height);
        }
        return bitmap;
    }

    private static long Difference(Bitmap a, Bitmap b) {
        long total = 0;

        for (int y = 0; y < CompareSize; y++) {
            for (int x = 0; x < CompareSize; x++) {
                Color ca = a.GetPixel(x, y);
                Color cb = b.GetPixel(x, y);
                total += Math.Abs(ca.A - cb.A) + Math.Abs(ca.R - cb.R) +
                         Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B);
            }
        }

        return total;
    }
}
