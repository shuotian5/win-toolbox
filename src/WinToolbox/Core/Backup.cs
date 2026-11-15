using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace WinToolbox.Core;

/// <summary>
/// Records how every value looked before the toolbox touched it, so any change can be
/// undone later — including values that did not exist at all (which must be deleted on
/// restore rather than set to some made-up default).
///
/// The journal lives in ProgramData and is shared by every module, so "Restore all" is a
/// single pass. Each entry is written once: turning a switch off and on again never
/// overwrites the original snapshot with an intermediate value.
/// </summary>
internal static class Backup
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinToolbox");

    private static readonly string FilePath = Path.Combine(Dir, "backup.json");
    private static readonly object Gate = new();
    private static BackupFile _data = Load();

    // ------------------------------------------------------------------ model

    internal sealed class RegBackup
    {
        public bool Existed { get; set; }
        public string Kind { get; set; } = "missing";
        public string? Value { get; set; }
        public string Saved { get; set; } = "";
    }

    internal sealed class BackupFile
    {
        public Dictionary<string, RegBackup> Registry { get; set; } = new();
        public Dictionary<string, int> ServiceStart { get; set; } = new();
        public Dictionary<string, bool> ServiceWasRunning { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static BackupFile Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                var parsed = JsonSerializer.Deserialize<BackupFile>(text);
                if (parsed is not null) return parsed;
            }
        }
        catch
        {
            // A corrupt journal must not stop the app; we simply start a fresh one.
        }
        return new BackupFile();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, JsonOpts));
        }
        catch
        {
            // Losing the journal only means "undo" becomes best-effort; never fatal.
        }
    }

    // ------------------------------------------------------------- registry

    private static string RegKey(RegistryHive hive, string path, string? name) =>
        $"{(int)hive}|{path}|{name ?? ""}";

    /// <summary>
    /// Snapshot a single registry value, but only the first time it is seen.
    /// </summary>
    public static void Remember(RegistryHive hive, string path, string? name)
    {
        string key = RegKey(hive, path, name);
        lock (Gate)
        {
            if (_data.Registry.ContainsKey(key)) return;

            var entry = new RegBackup { Saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var sub = baseKey.OpenSubKey(path, writable: false);
                object? current = name is null ? null : sub?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

                if (current is null)
                {
                    entry.Existed = false;
                    entry.Kind = "missing";
                }
                else
                {
                    entry.Existed = true;
                    entry.Kind = KindName(sub!.GetValueKind(name));
                    entry.Value = current.ToString();
                }
            }
            catch
            {
                entry.Existed = false;
                entry.Kind = "missing";
            }

            _data.Registry[key] = entry;
            Save();
        }
    }

    public static bool HasRegistryBackup(RegistryHive hive, string path, string? name)
    {
        lock (Gate) return _data.Registry.ContainsKey(RegKey(hive, path, name));
    }

    /// <summary>Count of distinct registry values captured so far.</summary>
    public static int Count
    {
        get { lock (Gate) return _data.Registry.Count + _data.ServiceStart.Count; }
    }

    private static string KindName(RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => "dword",
        RegistryValueKind.QWord => "qword",
        RegistryValueKind.String => "string",
        RegistryValueKind.ExpandString => "expand",
        RegistryValueKind.MultiString => "multistring",
        RegistryValueKind.Binary => "binary",
        _ => "string",
    };

    private static void WriteReg(RegistryHive hive, string path, string? name, string kind, string? value)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var sub = baseKey.CreateSubKey(path, writable: true);
        if (sub is null) return;

        if (name is null) return;

        switch (kind)
        {
            case "dword":
                sub.SetValue(name, int.Parse(value ?? "0"), RegistryValueKind.DWord);
                break;
            case "qword":
                sub.SetValue(name, long.Parse(value ?? "0"), RegistryValueKind.QWord);
                break;
            case "expand":
                sub.SetValue(name, value ?? "", RegistryValueKind.ExpandString);
                break;
            case "multistring":
                sub.SetValue(name, (value ?? "").Split('\n'), RegistryValueKind.MultiString);
                break;
            default:
                sub.SetValue(name, value ?? "", RegistryValueKind.String);
                break;
        }
    }

    // -------------------------------------------------------------- services

    public static void RememberService(string name, int startType, bool running)
    {
        lock (Gate)
        {
            if (_data.ServiceStart.ContainsKey(name)) return;
            _data.ServiceStart[name] = startType;
            _data.ServiceWasRunning[name] = running;
            Save();
        }
    }

    /// <summary>The start type recorded the first time this service was touched, if any.</summary>
    public static int? GetSavedServiceStart(string name)
    {
        lock (Gate) return _data.ServiceStart.TryGetValue(name, out int value) ? value : null;
    }

    // --------------------------------------------------------------- restore

    /// <summary>
    /// Undo every captured registry value and service start type. Returns the number of
    /// items that were put back.
    /// </summary>
    public static int RestoreAll()
    {
        int restored = 0;

        List<KeyValuePair<string, RegBackup>> regs;
        List<KeyValuePair<string, int>> svcs;
        List<KeyValuePair<string, bool>> running;

        lock (Gate)
        {
            regs = _data.Registry.ToList();
            svcs = _data.ServiceStart.ToList();
            running = _data.ServiceWasRunning.ToList();
        }

        foreach (var (key, entry) in regs)
        {
            try
            {
                var parts = key.Split('|');
                if (parts.Length != 3) continue;
                var hive = (RegistryHive)int.Parse(parts[0]);
                string path = parts[1];
                string? name = string.IsNullOrEmpty(parts[2]) ? null : parts[2];

                if (name is null) continue;

                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                if (!entry.Existed)
                {
                    using var existing = baseKey.OpenSubKey(path, writable: true);
                    existing?.DeleteValue(name, throwOnMissingValue: false);
                }
                else
                {
                    WriteReg(hive, path, name, entry.Kind, entry.Value);
                }
                restored++;
            }
            catch
            {
                // Skip values that were removed by an update in the meantime.
            }
        }

        foreach (var (name, start) in svcs)
        {
            try
            {
                if (Services.SetStartType(name, start)) restored++;
            }
            catch { }
        }

        foreach (var (name, wasRunning) in running)
        {
            try
            {
                if (wasRunning && Services.QueryState(name) != Native.SERVICE_RUNNING)
                    Services.TryStart(name);
            }
            catch { }
        }

        lock (Gate)
        {
            _data = new BackupFile();
            Save();
        }

        return restored;
    }
}
