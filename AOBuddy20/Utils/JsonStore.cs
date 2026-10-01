// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: JsonStore.cs
//
// Last modified: 2026-10-01
// Created:       2026-10-01 (ported from AOBuddy10 JsonStore.cs, R2.3)
//
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

#nullable disable

using Newtonsoft.Json;

namespace AOBuddy20.Utils;

/// <summary>
///     THE persistence helper (AOBuddy10 R2.3): every JSON state file the bot writes goes through
///     here, so a failing disk/permission can no longer vanish inside a per-site `catch { }`. Two
///     calls: Load returns null when the file is absent OR corrupt, with ONE logged line per path;
///     Save serializes at the call site, writes a .tmp first and File.Replace's it over the target,
///     so a crash mid-write can never leave a truncated state file. Logs once per path+direction.
/// </summary>
public static class JsonStore
{
    private static readonly HashSet<string> _complained = new HashSet<string>();

    public static T Load<T>(string path, Action<string> log = null) where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Complain(path, "read", ex.Message, log);
            return null;
        }
    }

    /// <summary>Returns false (and logs once) when the write failed; never throws.</summary>
    public static bool Save(string path, string text, Action<string> log = null)
    {
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path))
            {
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }

            return true;
        }
        catch (Exception ex)
        {
            Complain(path, "write", ex.Message, log);
            try
            {
                File.Delete(path + ".tmp");
            }
            catch
            {
            }

            return false;
        }
    }

    private static void Complain(string path, string what, string why, Action<string> log)
    {
        // Once per path+direction: the failure will repeat on every save until fixed.
        if (!_complained.Add(path + ":" + what))
        {
            return;
        }

        log?.Invoke($"JSONSTORE: couldn't {what} {path}: {why}");
    }
}
