using System.Collections.ObjectModel;

namespace NoteWorthy;

internal static class Settings
{
    private static string file_path = Path.Combine(Directory.GetCurrentDirectory(), "settings.txt");
    private static ReadOnlyDictionary<string, string> settings = LoadSettings();

    /// <summary>
    /// Load settings from the settings file and return them formatted as a dictionary.
    /// </summary>
    public static ReadOnlyDictionary<string, string> LoadSettings()
    {
        if (!File.Exists(file_path))
        {
            CreateDefaultSettingsFile();
        }

        string[] lines = File.ReadAllLines(file_path);

        Dictionary<string, string> s = new();
        foreach (string line in lines) {
            if (line.Length == 0 || line.StartsWith("// ")) continue;
            string[] parts = line.Split('=');
            parts[1] = parts[1].Substring(0, parts[1].Length - parts[1].IndexOf(" //"));
            s.Add(parts[0].Trim(), parts[1].Trim());
        }

        return new ReadOnlyDictionary<string, string>(s);
    }

    public static string? GetSetting(string key)
    {
        string? value;
        bool exists = settings.TryGetValue(key, out value);

        if (!exists) return null;
        if (value!.Contains(" //"))
        {
            return value.Substring(0, value.IndexOf(" //")).Trim();
        }
        return value.Trim();
    }

    public static void CreateDefaultSettingsFile()
    {
        File.WriteAllText(file_path, @"write_mode=insert // options: insert | overwrite - default char insert behaviour
theme=gray // options: gray | white | black - app color theme

// color options: blue | red | green | yellow | purple | cyan | white | black | gray | orange
primary_color=blue // custom text color for ctrl+b
secondary_color=red // custom text color for ctrl+i
tertiary_color=green // custom text color for ctrl+u
");
    }
}
