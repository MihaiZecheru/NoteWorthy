﻿using System.Collections.ObjectModel;
using System.Diagnostics;

namespace NoteWorthy;

internal static class Settings
{
    private static string file_path = Path.Combine(Directory.GetCurrentDirectory(), "settings.txt");
    private static ReadOnlyDictionary<string, string> settings = LoadSettings();

    public static bool InsertMode = GetSetting("write_mode") != "overwrite";
    public static bool AutoCapitalizeLines = GetSetting("auto_capitalize_lines") == "true";
    public static bool AutoCapitalizeNoteAndDirNames = GetSetting("auto_capitalize_note_and_dir_names") == "true";
    public static bool AutoColorNumbers = GetSetting("auto_color_numbers") == "true";
    public static bool AutoColorVariables = GetSetting("auto_color_variables") == "true";
    public static bool AutoColorVocabDefinitions = GetSetting("auto_color_vocab_definitions") == "true";
    public static bool AutoFormatSubtitles = GetSetting("auto_format_subtitles") == "true";

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

    private static void CreateDefaultSettingsFile()
    {
        File.WriteAllText(file_path, @"// Note: all settings are case sensitive. If an invalid value is provided, the default will be used
// Do not touch the comments

// -------------------------
// General settings
// -------------------------

// options: insert | overwrite - starting character type behaviour - default: insert
write_mode=insert

// true | false - true to capitalize the first letter of each line - default: false
auto_capitalize_lines=false

// true | false - true to capitalize the first letter of every word in the note / dir name
auto_capitalize_note_and_dir_names=true

// true | false - true to automatically color all numbers in the text with the secondary_color - default: false
auto_color_numbers=true

// true | false - true to automatically color all variables (single characters like 'x' or 'y' that are not 'a' or 'i' specifically) in the text with the primary_color - default: false
auto_color_variables=true

// true | false - true to automatically color the vocab definitions in the text with the primary_color - default: false
// ex - key: value - 'key' would be colored automatically when the semicolon and space are typed
auto_color_vocab_definitions=true

// true | false - true to automatically center and color subtitles with the primary color. Subtitles are the line above a dotted line inserted by Ctrl+L
auto_format_subtitles=true

// -------------------------
// color options: https://spectreconsole.net/appendix/colors use the # column to represent the color
// -------------------------

// custom text color for ctrl+b - default: 12 (blue)
primary_color=12

// custom text color for ctrl+u - default: 2 (green)
secondary_color=2

// custom text color for ctrl+i - default: 9 (red)
tertiary_color=9
");
    }

    public static void OpenSettingsFile()
    {
        if (!Settings.SettingsFileExists()) CreateDefaultSettingsFile();
        Process.Start("notepad.exe", file_path);
    }

    private static bool SettingsFileExists()
    {
        return File.Exists(file_path);
    }

    public static void ReloadSettings()
    {
        settings = LoadSettings();
    }
}
