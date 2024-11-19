using System.Collections.ObjectModel;
using System.Diagnostics;

namespace NoteWorthy;

internal static class Settings
{
    private static string file_path = Path.Combine(Directory.GetCurrentDirectory(), "settings.txt");
    private static ReadOnlyDictionary<string, string> settings = LoadSettings();

    public static bool InsertMode = GetSetting("write_mode") != "overwrite";
    public static bool AutoCapitalizeLines = GetSetting("auto_capitalize_lines") == "true";
    public static bool AutoColorNumbers = GetSetting("auto_color_numbers") == "true";
    public static bool AutoColorVariables = GetSetting("auto_color_variables") == "true";
    public static bool AutoSave = GetSetting("auto_save") == "true";

    public static byte PrimaryColor = byte.Parse(GetSetting("primary_color") ?? "12");
    public static byte SecondaryColor = byte.Parse(GetSetting("secondary_color") ?? "2");
    public static byte TertiaryColor = byte.Parse(GetSetting("tertiary_color") ?? "9");

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

    private static string? GetSetting(string key)
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
        File.WriteAllText(file_path, @"// Note: all settings are case sensitive. If an invalid value is provided, the default will be used

// General settings
write_mode=insert // options: insert | overwrite - starting character type behaviour - default: insert
auto_capitalize_lines=false // true | false - true to capitalize the first letter of each line - default: false
auto_color_numbers=false // true | false - true to automatically color all numbers in the text with the secondary_color - default: false
auto_color_variables=false // true | false - true to automatically color all variables (single characters like 'p' or 'c' that are not 'a' or 'i') in the text with the secondary_color - default: false
auto_save=false // true | false - true to automatically save the file after typing - default: false

// color options: https://spectreconsole.net/appendix/colors use the # column to represent the color
primary_color=12 // custom text color for ctrl+b - default: 12 (blue)
secondary_color=2 // custom text color for ctrl+u - default: 2 (green)
tertiary_color=9 // custom text color for ctrl+i - default: 9 (red)
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
    }
}
