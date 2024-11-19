using System.Collections.ObjectModel;
using System.Diagnostics;

namespace NoteWorthy;

internal static class Settings
{
    private static string file_path = Path.Combine(Directory.GetCurrentDirectory(), "settings.txt");
    private static ReadOnlyDictionary<string, string> settings = LoadSettings();
    public static int TabSize = int.Parse(GetSetting("tab_size") ?? "4");

    /// <summary>
    /// Load settings from the settings file and return them formatted as a dictionary.
    /// </summary>
    public static ReadOnlyDictionary<string, string> LoadSettings()
    {
        if (!SettingsFileExists()) CreateDefaultSettingsFile();

        string[] lines = File.ReadAllLines(file_path);

        try
        {
            Dictionary<string, string> s = new();
            
            foreach (string line in lines) {
                if (line.Length == 0 || line.StartsWith("// ")) continue;
                string[] parts = line.Split('=');
                parts[1] = parts[1].Substring(0, parts[1].Length - parts[1].IndexOf(" //"));
                s.Add(parts[0].Trim(), parts[1].Trim());
            }

            return new ReadOnlyDictionary<string, string>(s);
        } catch (Exception)
        {
            Console.Clear();
            Console.WriteLine("Your settings were formatted incorrectly, so the file was reset to its default state. Press any key to continue.");
            Console.ReadKey(true);
            CreateDefaultSettingsFile();
            return LoadSettings();
        }
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
tab_size=4 // number of spaces per tab (defaults to 4)

// color options: https://spectreconsole.net/appendix/colors use the # column to identify.
primary_color=12 // custom text color for ctrl+b (12 is blue)
secondary_color=2 // custom text color for ctrl+u (2 is green)
tertiary_color=9 // custom text color for ctrl+i (9 is red)
");
    }

    public static void OpenSettingsFile()
    {
        Process.Start("notepad.exe", file_path);
    }

    public static bool SettingsFileExists()
    {
        return File.Exists(file_path);
    }

    public static void ReloadSettings()
    {
        settings = LoadSettings();
        TabSize = int.Parse(GetSetting("tab_size") ?? "4");
    }
}
