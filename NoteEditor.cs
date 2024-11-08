﻿using Spectre.Console;
using System.Linq;
using System.Text;

namespace NoteWorthy;
internal class NoteEditor
{
    /// <summary>
    /// Buffer height of the editor. This is the width of the editor minus the panel top/bottom borders
    /// </summary>
    private int BUFFER_HEIGHT = _CalculateBufferHeight();
    
    /// <summary>
    /// Buffer width of the editor. This is the width of the editor minus the width of the note tree and the padding / borders.
    /// </summary>
    private int BUFFER_WIDTH = _CalculateBufferWidth();

    /// <summary>
    /// The lines that make up the note
    /// </summary>
    private List<List<ColorChar>> lines;

    /// <summary>
    /// The history of the note. Used for undoing changes with Ctrl+Z.
    /// Keeps track of the note's state every 10 characters.
    /// Will only track the past 75 states.
    /// When an element is undone, it is moved to the <see cref="redoStack"/>.
    /// The type of this stack is a tuple of the previous content of the note and the cursor position
    /// </summary>
    private LimitedStack<(
        List<List<ColorChar>> note_content,
        (int cursor_line_num, int cursor_pos_in_line)
    )> history = new(maxSize: 10);

    /// <summary>
    /// Just like the history stack, except this stack keeps track of the states that were undone.
    /// Used for Ctrl+Y functionality.
    /// 
    /// Is not a limited stack because the redoStack can only hold as much as is in the history stack
    /// </summary>
    private Stack<(
        List<List<ColorChar>> note_content,
        (int cursor_line_num, int cursor_pos_in_line)
    )> redoStack = new();

    /// <summary>
    /// The path to the note in the file system
    /// </summary>
    private string? note_path;

    /// <summary>
    /// The number of the line the cursor is inside of in the editor.
    /// </summary>
    private int line_num = 0;

    /// <summary>
    /// The position of the cursor in the current line
    /// </summary>
    private int pos_in_line = 0;

    /// <summary>
    /// True if there are unsaved changes
    /// </summary>
    private bool unsaved_changes = false;

    /// <summary>
    /// The amount of characters since the state was last saved. Used for Ctrl+Z shortcut.
    /// The state will be saved every 10 characters.
    /// </summary>
    private int chars_since_last_state_save = 0;

    /// <summary>
    /// If true, typing chars will insert them into the line. If false, typing chars will overwrite the whatever is at the cursor.
    /// </summary>
    private bool insertModeOn = Settings.GetSetting("write_mode") == "insert";

    /// <summary>
    /// If the primary color is on, the next char will be colored with the primary color.
    /// </summary>
    private bool primary_color_on = false;

    /// <summary>
    /// If the secondary color is on, the next char will be colored with the secondary color.
    /// </summary>
    private bool secondary_color_on = false;

    /// <summary>
    /// If the tertiary color is on, the next char will be colored with the tertiary color.
    /// </summary>
    private bool tertiary_color_on = false;

    /// <param name="note_path">File path of the note to show in the editor. Set NULL to not show any note</param>
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public NoteEditor(string? notePath)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    {
        this.note_path = notePath;

        if (this.note_path == null) return;

        if (!File.Exists(this.note_path))
        {
            throw new FileNotFoundException();
        }

        LoadNote();
    }

    private void LoadNote()
    {
        this.lines = new();
        this.lines.Add(new List<ColorChar>());

        byte[] bytes = File.ReadAllBytes(this.note_path!);
        // There will always be an even number of bytes in the file because each char byte has a corresponding color byte
        for (long i = 0; i < bytes.LongLength; i += 2)
        {
            byte c = bytes[i];
            byte color = bytes[i + 1];

            if (c == (byte)'\n')
            {
                // Create a new line. In this loading process,
                // future chars will be appended to this line until another line is made.
                lines.Add(new List<ColorChar>());
            }
            else
            {
                // Append the char to the note
                lines[lines.Count - 1].Add(new ColorChar(c, color));
            }
        }

        FixNoteBuffersIfNecessary();
    }

    /// <summary>
    /// Make the note fit the screen no matter what. If the note is too big, disable typing.
    /// 
    /// If the note is too big in height, make the last line an elipsis to show there's more lines.
    /// Add an elipsis to the end of each overflowing line to show there's more text on that line
    /// </summary>
    private void FixNoteBuffersIfNecessary()
    {
        // Typing is enabled by default. If the note is too big, though, it will be disabled.
        typingDisabled = false;

        // " ..." a space and three dots
        ColorChar[] elipsis = { new ColorChar((byte)' ', 0), new ColorChar((byte)'.', 0), new ColorChar((byte)'.', 0), new ColorChar((byte)'.', 0) };

        // If the note is too big to fit in the editor, disable typing
        if (lines!.Count > BUFFER_HEIGHT)
        {
            DisableTyping();
            lines[BUFFER_HEIGHT - 1] = elipsis.ToList().GetRange(1, 3);
        }

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Count > BUFFER_WIDTH)
            {
                if (!typingDisabled) DisableTyping();
                lines[i] = lines[i].GetRange(0, BUFFER_WIDTH - 4);
                lines[i].AddRange(elipsis);
            }
        }
    }

    /// <summary>
    /// Save the current state of the note to the history stack for use in Ctrl+Z / Ctrl+Y
    /// </summary>
    private void SaveState()
    {
        // Clear the redoStack when a change is made cus you can't go back
        redoStack.Clear();

        // Reset the char counter since the state was saved
        chars_since_last_state_save = 0;

        // Deep copy the note to save its state.
        List<List<ColorChar>> noteCopy = lines.Select(line => new List<ColorChar>(line)).ToList();
        history.Push(
            (note_content: noteCopy, (cursor_line_num: line_num, cursor_pos_in_line: pos_in_line))
        );
    }

    public void ToggleInsertMode()
    {
        insertModeOn = !insertModeOn;
    }

    /// <summary>
    /// Set the cursor position in the editor to (<see cref="pos_in_line"/>, <see cref="line_num"/>).
    /// (0, 0) is the top left corner of the panel with the editor.
    /// Does not correspond to the actual console cursor position.
    /// </summary>
    public void UpdateCursorPosInEditor()
    {
        int left = pos_in_line;
        int top = line_num;

        if (top > BUFFER_HEIGHT)
        {
            throw new ArgumentOutOfRangeException("Cursor top position is out of bounds");
        }

        if (left > BUFFER_WIDTH)
        {
            throw new ArgumentOutOfRangeException("Cursor left position is out of bounds");
        }

        if (Program.NoteTreeVisible())
        {
            // +2 for left and right editor border
            // account for the note tree being visible
            Console.SetCursorPosition(left + NoteTree.DISPLAY_WIDTH + 2 + 5, top + 1);
        }
        else
        {
            // +2 for left and right editor border
            Console.SetCursorPosition(left + 2 + 5, top + 1);
        }
    }

    public string? GetNotePath()
    {
        return note_path;
    }

    /// <summary>
    /// Delete the line at the current position
    /// </summary>
    public void DeleteLine()
    {
        // If there is only one line, there aren't enough lines to delete
        if (lines.Count == 1 && lines[0].Count == 0) return;

        if (lines.Count == 1)
        {
            // Delete the line by replacing it with an empty one
            lines[0] = new();
            pos_in_line = 0;
            return;
        }

        lines.RemoveAt(line_num);
        if (line_num == lines.Count)
        {   
            line_num--;
        }
        pos_in_line = 0;
        unsaved_changes = true;
    }

    /// <summary>
    /// Insert line at the current position
    /// </summary>
    public void InsertLine()
    {
        if (lines.Count == BUFFER_HEIGHT) return;

        if (AtEndOfLine())
        {
            lines.Insert(line_num + 1, new List<ColorChar>());
            pos_in_line = 0;
        }
        else
        {
            AnsiConsole.Cursor.Hide();

            // Pressing enter while not at the end of the line will take the text to the right of the cursor and move it to the newly-created line
            List<ColorChar> current_line = lines[line_num].Slice(0, pos_in_line);
            List<ColorChar> newline = lines[line_num].Slice(pos_in_line, lines[line_num].Count - pos_in_line);

            // Rewrite current line and append the new line
            lines[line_num] = current_line;
            lines.Insert(line_num + 1, newline);

            // Move cursor to beginning of new line
            pos_in_line = 0;
            AnsiConsole.Cursor.Show();
        }

        line_num++;
        unsaved_changes = true;
    }

    /// <summary>
    /// Insert <paramref name="c"/> at the current position in the editor.
    /// </summary>
    /// <param name="c">Char to insert</param>
    public void InsertChar(char c)
    {
        if (LineIsFull() && insertModeOn) return;
        // For overwrite mode
        else if (LineIsFull() && AtEndOfLine()) return;

        // If the char is not ascii
        if (c < 0 || c > 127)
        {
            c = RemoveDiacritics(c);
        }

        ColorChar _char;
        if (primary_color_on || secondary_color_on || tertiary_color_on)
        {
            string? color = Settings.GetSetting(
                primary_color_on ? "primary_color" : secondary_color_on ? "secondary_color" : "tertiary_color"
            );
            
            if (color == null)
            {
                if (HasUnsavedChanges())
                {
                    Program.AskToSaveUnsavedChanges("[red]The app is going to quit because the color setting is invalid. [/]" +
                        "Please fix the settings file. But first...");
                }
                else
                {
                    Console.Clear();
                    Console.WriteLine("App closed because the color setting is invalid. Your note didn't have any unsaved changes, so you didn't lose any work.");
                }
                Environment.Exit(0);
                return;
            }
            else
            {
                _char = new ColorChar((byte)c, byte.Parse(color));
            }
        }
        else
        {
            // 0 is the null value (no color specified)
            _char = new ColorChar((byte)c, 0);
        }

        if (AtEndOfLine())
        {
            // Just add the character to the end of the line if the cursor is at the end of the line,
            // regardless of the mode (insert or overwrite)
            lines[line_num].Add(_char);
        }
        else
        {
            if (insertModeOn)
            {
                // Insert the char at the current position
                lines[line_num].Insert(pos_in_line, _char);
            }
            else
            {
                // Overwrite the char at the current positon
                lines[line_num][pos_in_line] = _char;
            }
        }

        unsaved_changes = true;
        pos_in_line++;
    }

    /// <summary>
    /// Map of ascii char to unicode char with diacritics. Used in <see cref="RemoveDiacritics(char)"/>
    /// </summary>
    private static Dictionary<char, char[]> diacritics = new Dictionary<char, char[]>()
    {
        { 'A', new char[] { 'À', 'Á', 'Â', 'Ã', 'Ä', 'Å', 'Æ' } },
        { 'E', new char[] { 'È', 'É', 'Ê', 'Ë' } },
        { 'I', new char[] { 'Ì', 'Í', 'Î', 'Ï' } },
        { 'O', new char[] { 'Ò', 'Ó', 'Ô', 'Õ', 'Ö', 'Ø' } },
        { 'U', new char[] { 'Ù', 'Ú', 'Û', 'Ü' } },
        { 'Y', new char[] { 'Ý' } },
        { 'C', new char[] { 'Ç' } },
        { 'D', new char[] { 'Ð' } },
        { 'N', new char[] { 'Ñ' } },
        { 'a', new char[] { 'à', 'á', 'â', 'ã', 'ä', 'å', 'æ' } },
        { 'e', new char[] { 'è', 'é', 'ê', 'ë' } },
        { 'i', new char[] { 'ì', 'í', 'î', 'ï' } },
        { 'o', new char[] { 'ò', 'ó', 'ô', 'õ', 'ö', 'ø' } },
        { 'u', new char[] { 'ù', 'ú', 'û', 'ü' } },
        { 'y', new char[] { 'ý' } },
        { 'c', new char[] { 'ç' } },
        { 'n', new char[] { 'ñ' } },
        { 'd', new char[] { 'ð' } },
        { 's', new char[] { 'ß' } },
    };

    private char RemoveDiacritics(char c)
    {
        foreach (KeyValuePair<char, char[]> entry in NoteEditor.diacritics)
        {
            if (entry.Value.Contains(c))
            {
                return entry.Key;
            }
        }

        return '?';
    }

    /// <summary>
    /// Delete a character with the Backspace key.
    /// Deletes the character to the left of the cursor.
    /// </summary>
    public void DeleteCharWithBackspace()
    {
        if (AtBeginningOfLine())
        {
            if (OnFirstLine()) return;
            int prev_line_length = LineLength(line_num - 1);
            lines[line_num - 1].AddRange(lines[line_num]);
            lines.RemoveAt(line_num);
            line_num--;
            pos_in_line = prev_line_length;
        }
        // Not at beginning of line - just remove the char
        else
        {
            lines[line_num].RemoveAt(pos_in_line - 1);
            pos_in_line--;
        }

        unsaved_changes = true;
    }

    /// <summary>
    /// Delete a single character with the delete key.
    /// Deletes the char to the right of the cursor.
    /// </summary>
    public void DeleteCharWithDeleteKey()
    {
        if (AtEndOfLine())
        {
            if (OnLastLine()) return;
            lines[line_num].AddRange(lines[line_num + 1]);
            lines.RemoveAt(line_num + 1);
        }
        else
        {
            lines[line_num].RemoveAt(pos_in_line);
        }

        unsaved_changes = true;
    }

    /// <summary>
    /// Delete a word with Ctrl+Backspace.
    /// Deletes the word to the left of the cursor.
    /// </summary>
    public void DeleteWordWithBackspace()
    {
        if (AtBeginningOfLine())
        {
            // If the cursor is at the beginning of the first line,
            // there is nothing to delete on the current line.
            // Instead, delete the current line and append it to the previous line

            if (OnFirstLine()) return;
            int prev_line_length = LineLength(line_num - 1);
            lines[line_num - 1].AddRange(lines[line_num]);
            lines.RemoveAt(line_num);
            line_num--;
            pos_in_line = prev_line_length;
        }
        else
        {
            int start_of_word = FindIndexOf_StartOfPreviousWord();
            lines[line_num].RemoveRange(start_of_word, pos_in_line - start_of_word);
            pos_in_line = start_of_word;
        }

        unsaved_changes = true;
    }
    
    /// <summary>
    /// Delete a word with Ctrl+Delete.
    /// Deletes the word to the right of the cursor.
    /// </summary>
    public void DeleteWordWithDeleteKey()
    {
        if (AtEndOfLine())
        {
            // If the cursor is at the end of the last line,
            // there is nothing to delete on the current line.
            // Instead, delete the current line and append it to the next line

            if (OnLastLine()) return;
            lines[line_num].AddRange(lines[line_num + 1]);
            lines.RemoveAt(line_num + 1);
        }
        else
        {
            int end_of_word = FindIndexOf_EndOfNextWord();
            lines[line_num].RemoveRange(pos_in_line, end_of_word - pos_in_line);
            // pos_in_line does not have to be modified because the word to the RIGHT of the cursor is deleted
        }

        unsaved_changes = true;
    }

    /// <summary>
    /// Find and return the index corresponding to the end of the next word (the word to the right of the cursor).
    /// Used for <see cref="DeleteWordWithDeleteKey"/> and <see cref="NavigateToNextWord"/>
    /// 
    /// Ex: My name is John Smith
    /// ...........^ cursor is there
    /// This function would return the index of the 'J' in 'John' (11) 
    /// </summary>
    private int FindIndexOf_EndOfNextWord()
    {
        // If the cursor is not at the end of the line,
        // delete the word to the right of the cursor (delete key)

        int end_of_word = pos_in_line;

        // Loop I: Clear duplicate spaces at the beginning of the word (left side of the word)
        while (end_of_word < lines[line_num].Count - 1 && lines[line_num][end_of_word] == ' ')
        {
            end_of_word++;
        }

        // Loop II: Clear the word (all text up until the next space)
        while (end_of_word < lines[line_num].Count - 1 && lines[line_num][end_of_word] != ' ')
        {
            end_of_word++;
        }

        // Loop III: Clear duplicate spaces at the end of the word (right side of the word)
        while (end_of_word < lines[line_num].Count && lines[line_num][end_of_word] == ' ')
        {
            end_of_word++;
        }

        // Loop III will stop deleting just one char before the end of the line always, because if it's at the end of the line,
        // the last character can't be a space. Therefore, if the end of the word is at the end of the line, account for that.

        // However, if the word is a single char, like the word 'a', it shouldn't do that. So, also check if the 2nd-last char is a space before account for Loop III
        if (end_of_word == GetCharsInLine() - 1 && lines[line_num][end_of_word - 1] != ' ') end_of_word++;

        return end_of_word;
    }

    /// <summary>
    /// Find and return the index corresponding to the start of the previous word (the word to the left of the cursor).
    /// Used for <see cref="DeleteWordWithBackspace"/> and <see cref="NavigateToPreviousWord"/>.
    /// 
    /// Ex: My name is John Smith
    /// ...........^ cursor is there
    /// This function would return the index of the 'y' in 'My' (1) 
    /// </summary>
    private int FindIndexOf_StartOfPreviousWord()
    {
        // If the cursor is not at the beginning of the line,
        // delete the word to the left of the cursor (backspace)

        int start_of_word = pos_in_line - 1;

        // Loop I: Clear duplicate spaces at the end of the word (right side of the word)
        while (start_of_word > 0 && lines[line_num][start_of_word] == ' ')
        {
            start_of_word--;
        }

        // Loop II: Clear the word (all text up until the next space)
        while (start_of_word > 0 && lines[line_num][start_of_word] != ' ')
        {
            start_of_word--;
        }

        // Loop III: Clear duplicate spaces at the beginning of the word (left side of the word)
        while (start_of_word > 0 && lines[line_num][start_of_word] == ' ')
        {
            start_of_word--;
        }

        // Account for Loop III, which will always remove one space extra
        // But if already at the beginning of the line, stay there
        if (start_of_word != 0) start_of_word++;

        return start_of_word;
    }

    /// <summary>
    /// Ctrl+Left arrow key functionality.
    /// Navigate the cursor to the start of the previous word (to the left of the cursor).
    /// </summary>
    public void NavigateToPreviousWord()
    {
        if (AtBeginningOfLine())
        {
            if (OnFirstLine()) return;
            line_num--;
            pos_in_line = GetCharsInLine();
        }
        else
        {
            pos_in_line = FindIndexOf_StartOfPreviousWord();
        }
    }

    /// <summary>
    /// Ctrl+Right arrow key functionality.
    /// Navigate the cursor to the end of the next word (to the right of the cursor).
    /// </summary>
    public void NavigateToNextWord()
    {
        if (AtEndOfLine())
        {
            if (OnLastLine()) return;
            line_num++;
            pos_in_line = 0;
        }
        else
        {
            pos_in_line = FindIndexOf_EndOfNextWord();
        }
    }

    public void Undo()
    {
        if (history.IsEmpty) return;

        // Save the current state to the redo stack with a deep copy
        List<List<ColorChar>> noteCopy = lines.Select(line => new List<ColorChar>(line)).ToList();
        redoStack.Push(
            (note_content: noteCopy, (cursor_line_num: line_num, cursor_pos_in_line: pos_in_line))
        );

        // Get the last state from the history stack
        ( List<List<ColorChar>> note_content,
          (int cursor_line_num, int cursor_pos_in_line) ) lastState = history.Pop();
        lines = lastState.note_content;
        line_num = lastState.Item2.cursor_line_num;
        pos_in_line = lastState.Item2.cursor_pos_in_line;
        unsaved_changes = true;
    }

    public void Redo()
    {
        if (redoStack.Count == 0) return;

        // Save the current state to the history stack with a deep copy
        List<List<ColorChar>> noteCopy = lines.Select(line => new List<ColorChar>(line)).ToList();
        history.Push(
            (note_content: noteCopy, (cursor_line_num: line_num, cursor_pos_in_line: pos_in_line))
        );

        // Get the last state from the redo stack
        (List<List<ColorChar>> note_content,
          (int cursor_line_num, int cursor_pos_in_line)) lastState = redoStack.Pop();
        lines = lastState.note_content;
        line_num = lastState.Item2.cursor_line_num;
        pos_in_line = lastState.Item2.cursor_pos_in_line;
        unsaved_changes = true;
    }

    /// <summary>
    /// Return the amount of characters in the given <paramref name="_line_num"/>
    /// </summary>
    private int LineLength(int _line_num)
    {
        return lines[_line_num].Count;
    }

    /// <summary>
    /// Returns true if the cursor is at the beginning of the current line
    /// </summary>
    /// <returns></returns>
    private bool AtBeginningOfLine()
    {
        return pos_in_line == 0;
    }

    /// <summary>
    /// Returns true if the cursor is at the end of the current line
    /// </summary>
    private bool AtEndOfLine()
    {
        // Does not use .Count - 1 because the cursor goes after the last char
        return pos_in_line == lines[line_num].Count;
    }

    /// <summary>
    /// Returns true if the cursor is somewhere in the first line
    private bool OnFirstLine()
    {
        return line_num == 0;
    }

    /// <summary>
    /// Returns true if the cursor is somewhere in the last line
    /// </summary>
    public bool OnLastLine()
    {
        return line_num == lines.Count - 1;
    }

    /// <summary>
    /// Returns true if the current line cannot hold any more characters.
    /// </summary>
    private bool LineIsFull()
    {
        return lines[line_num].Count == BUFFER_WIDTH;
    }

    /// <summary>
    /// Returns the number of characters in the current line.
    /// Used for checking if the cursor is at the end of the line or moving the cursor to the end of the line.
    /// </summary>
    /// <returns></returns>
    private int GetCharsInLine()
    {
        return lines[line_num].Count;
    }

    /// <summary>
    /// Returns true if there are unsaved changes
    /// </summary>
    public bool HasUnsavedChanges()
    {
        return unsaved_changes;
    }

    /// <summary>
    /// Get the amount of lines in the note
    /// </summary>
    public int GetLineCount() {
        return lines.Count;
    }

    /// <summary>
    /// Get the amount of characters in the whole note
    /// </summary>
    /// <returns></returns>
    public int GetNoteCharCount()
    {
        int count = 0;
        
        for (int i = 0; i < this.lines.Count; i++)
        {
            count += this.lines[i].Count;
        }

        return count;
    }

    /// <summary>
    /// Save the note to the file
    /// </summary>
    public void Save()
    {
        if (this.note_path == null) return;
        if (!unsaved_changes) return;

        SaveState();

        // Get bytes
        List<byte> bytes = new();
        for (int i = 0; i < this.lines.Count; i++)
        {
            foreach (ColorChar c in this.lines[i])
            {
                (byte _char, byte _color) = c.GetBytes();
                bytes.Add(_char);
                bytes.Add(_color);
            }

            if (i != lines.Count - 1)
            {
                // Colorless new-line
                bytes.Add((byte)'\n');
                bytes.Add(0);
            }
        }

        File.WriteAllBytes(
            this.note_path,
            bytes.ToArray()
        );

        unsaved_changes = false;
    }

    /// <summary>
    /// Generate the <see cref="Spectre.Console.Panel"/> for displaying the note editor
    /// </summary>
    public Panel GenerateDisplayPanel()
    {
        // No note in the editor
        if (this.note_path == null)
        {
            return new Panel(new Text("No note open"))
                .Expand()
                .RoundedBorder();
        }
        // The note is too big and typing is disabled. The user cannot focus the editor and the text is truncated.
        else if (typingDisabled)
        {
            return new Panel(GetDisplayMarkup()).Header($"[yellow] {Path.GetFileName(note_path)} [white]-[/] [red]enlarge console to edit note[/] [/]")
             .Expand()
             .RoundedBorder();
        }
        // Normal display
        else
        {
            string unsaved_changes_indicator = unsaved_changes ? " *" : "";
            return new Panel(GetDisplayMarkup()).Header($"[yellow] {Path.GetFileName(note_path)}{unsaved_changes_indicator} [/]")
             .Expand()
             .RoundedBorder();
        }
    }

    public void MoveCursorUp()
    {
        if (OnFirstLine()) return;

        line_num--;
        if (pos_in_line > lines[line_num].Count)
        {
            pos_in_line = lines[line_num].Count;
        }
    }

    public void MoveCursorDown()
    {
        if (OnLastLine())
        {
            int end_of_line = lines[line_num].Count;
            if (pos_in_line < end_of_line)
            {
                pos_in_line = end_of_line;
            }
        }
        else
        {
            line_num++;
            if (pos_in_line > lines[line_num].Count)
            {
                pos_in_line = GetCharsInLine();
            }
        }
    }

    public void MoveCursorLeft()
    {
        if (pos_in_line == 0)
        {
            if (OnFirstLine()) return;
            line_num--;
            pos_in_line = GetCharsInLine();
        }
        else
        {
            pos_in_line--;
        }
    }

    public void MoveCursorRight()
    {
        if (pos_in_line == GetCharsInLine())
        {
            if (OnLastLine()) return;
            line_num++;
            pos_in_line = 0;
        }
        else
        {
            pos_in_line++;
        }
    }

    public void MoveCursorToStartOfLine()
    {
        pos_in_line = 0;
    }

    public void MoveCursorToEndOfLine()
    {
        pos_in_line = lines[line_num].Count;
    }

    public void MoveCursorToStartOfEditor()
    {
        line_num = 0;
        pos_in_line = 0;
    }

    public void MoveCursorToEndOfEditor()
    {
        line_num = lines.Count - 1;
        pos_in_line = lines[line_num].Count;
    }

    public void MoveLineUp()
    {
        if (OnFirstLine()) return;

        List<ColorChar> temp = lines[line_num];
        lines[line_num] = lines[line_num - 1];
        lines[line_num - 1] = temp;

        line_num--;
        unsaved_changes = true;
    }

    public void MoveLineDown()
    {
        if (OnLastLine()) return;

        List<ColorChar> temp = lines[line_num];
        lines[line_num] = lines[line_num + 1];
        lines[line_num + 1] = temp;

        line_num++;
        unsaved_changes = true;
    }

    private Markup GetDisplayMarkup()
    {
        StringBuilder s = new();
        for (int i = 0; i < this.lines.Count; i++)
        {
            string line_number = (i + 1).ToString("D2");

            // Space to make the line number stick to the right border
            s.Append($"[yellow]{line_number} | [/]");

            // this.lines[i] is the line
            for (int j = 0; j < this.lines[i].Count; j++)
            {
                Color? color = this.lines[i][j].Color;
                string _char = HandleSquareBrackets(this.lines[i][j].Char);

                // this.lines[i][j] is the char
                if (this.lines[i][j].Color == null)
                {
                    s.Append(_char);
                }
                else
                {
                    s.Append($"[{color!.Value}]{_char}[/]");
                }
            };

            s.Append('\n');
        };

        for (int i = this.lines.Count; i < BUFFER_HEIGHT; i++)
        {
            string line_number = (i + 1).ToString("D2");

            // Space to make the line number stick to the right border
            s.Append($"[yellow]{line_number} | [/]\n");
        }

        // Remove trailing \n
        return new Markup(s.ToString().Substring(0, s.Length - 1));
    }

    public void TogglePrimaryColor()
    {
        primary_color_on = !primary_color_on;
        secondary_color_on = false;
        tertiary_color_on = false;
    }

    public void ToggleSecondaryColor()
    {
        primary_color_on = false;
        secondary_color_on = !secondary_color_on;
        tertiary_color_on = false;
    }

    public void ToggleTertiaryColor()
    {
        primary_color_on = false;
        secondary_color_on = false;
        tertiary_color_on = !tertiary_color_on;
    }

    public bool IsPrimaryColorEnabled()
    {
        return primary_color_on;
    }

    public bool IsSecondaryColorEnabled()
    {
        return secondary_color_on;
    }

    public bool IsTertiaryColorEnabled()
    {
        return tertiary_color_on;
    }

    public bool IsInsertModeEnabled()
    {
        return insertModeOn;
    }

    /// <summary>
    /// The width buffer for the editor is the console width buffer - the width of the note tree - 4 - 6.
    /// -4 comes from the border and padding of the editor panel 
    /// -5 gives space for the line numbers on the right side of the panel: " | xx"
    /// </summary>
    public static int _CalculateBufferWidth()
    {
        if (Program.NoteTreeVisible())
            return Console.BufferWidth - NoteTree.DISPLAY_WIDTH - 4 - 5;
        else
            return Console.BufferWidth - 4 - 5;
    }

    /// <summary>
    /// The height buffer for the editor is the console height buffer - 2.
    /// the -2 comes from the panel top and bottom border.
    /// </summary>
    /// <returns></returns>
    public static int _CalculateBufferHeight()
    {
        return Console.BufferHeight - 2; // -2 for panel border on the top and bottom
    }

    public void UpdateBuffers()
    {
        BUFFER_HEIGHT = _CalculateBufferHeight();
        BUFFER_WIDTH = _CalculateBufferWidth();
        // Reload the note to truncate the lines and disable typing if necessary
        if (note_path != null) LoadNote();
    }

    /// <summary>
    /// This will be set to true when the text is too big to fit in the editor, for having been written 
    /// while the console was larger than it is now. Typing will be disabled until the user makes the console
    /// bigger, and every line will be truncated to fit the editor, but the user WILL be able to view the note.
    /// 
    /// When typing is disabled, the editor is like in preview mode. The user can see what's there, but they won't
    /// be able to make any changes until they make the console bigger.
    /// 
    /// Similar to what happens when the user opens a file with spacebar instead of enter. Enter shifts the focus to
    /// the editor, but spacebar just shows the note in preview mode, keeping the focus on the tree.
    /// </summary>
    private bool typingDisabled = false;

    public void DisableTyping()
    {
        typingDisabled = true;
        Program.UnfocusEditor();
    }

    public void EnableTyping()
    {
        typingDisabled = false;
    }

    public bool IsTypingDisabled()
    {
        return typingDisabled;
    }

    public (int, int) GetCursorPosition()
    {
        return (line_num, pos_in_line);
    }

    /// <summary>
    /// Navigate to the given <paramref name="line"/> in the editor
    /// </summary>
    /// <param name="line">The line to go to. The first line is 1, not 0, since user inputs it</param>
    public void GoToLine(int line)
    {
        // Line cannot be less than 1
        if (line <= 0) throw new Exception("Something went wrong. It's not possible for line to be less than 1. line = " + line);

        // User inputs line starting at 1
        line--;

        // If given line greater than max line, go to last line
        if (line > lines.Count - 1)
            line_num = lines.Count - 1;
        else
            line_num = line;

        // Go to beginning of line
        pos_in_line = 0;
    }

    private string HandleSquareBrackets(char c)
    {
        if (c == '[') return "[[";
        if (c == ']') return "]]";
        return c.ToString();
    }

    public void InsertTab()
    {
        int size = Settings.TabSize;
        bool tab_fits = lines[line_num].Count <= BUFFER_WIDTH - 4;

        // Check early return conditions
        if (insertModeOn)
        {
            if (!tab_fits) return;
        }
        else
        {
            if (!tab_fits && pos_in_line > BUFFER_WIDTH - 4) return;
        }
        
        for (int i = 0; i < size; i++)
        {
            InsertChar(' ');
        }
    }

    private static Dictionary<ConsoleKey, string> tree_ctrl_functions = new Dictionary<ConsoleKey, string>()
    {
        { ConsoleKey.Q, "Quit NoteWorthy" },
        { ConsoleKey.L, "Toggle focus to the editor" },
        { ConsoleKey.O, "Open current directory in file explorer" },
        { ConsoleKey.UpArrow, "Preview the previous note" },
        { ConsoleKey.DownArrow, "Preview the next note" },
        { ConsoleKey.W, "Close the editor" },
        { ConsoleKey.N, "Create new note" },
        { ConsoleKey.M, "Create new folder" },
        { ConsoleKey.R, "Reload the tree" },
        { ConsoleKey.D, "Delete the selected tree item" },
        { ConsoleKey.D8, "Open the settings file" },
        { ConsoleKey.D1, "Toggle tree visibility" },
        { ConsoleKey.H, "Toggle this help panel" }
    };

    private static Dictionary<ConsoleKey, string> tree_regular_functions = new Dictionary<ConsoleKey, string>()
    {
        { ConsoleKey.UpArrow, "Move selection up" },
        { ConsoleKey.DownArrow, "Move selection down" },
        { ConsoleKey.Home, "Move selection to top tree" },
        { ConsoleKey.End, "Move selection to bottom tree" },
        { ConsoleKey.Escape, "Go to parent dir" },
        { ConsoleKey.Enter, "Open the selected note" },
        { ConsoleKey.Spacebar, "Preview the selected note" },
        { ConsoleKey.Tab, "Switch focus to the editor" },
        { ConsoleKey.H, "Toggle this help panel" },
        { ConsoleKey.F2, "Rename the selected item" },
        { ConsoleKey.N, "Create new note" },
        { ConsoleKey.M, "Create new folder" },
        { ConsoleKey.Delete, "Delete selected tree item" },
        { ConsoleKey.Backspace, "Delete selected tree item" }
    };

    private static Dictionary<ConsoleKey, string> editor_ctrl_functions = new Dictionary<ConsoleKey, string>()
    {
        { ConsoleKey.Q, "Quit NoteWorthy" },
        { ConsoleKey.L, "Toggle focus to the tree" },
        { ConsoleKey.S, "Save note" },
        { ConsoleKey.R, "Reload note" },
        { ConsoleKey.D, "Delete current line" },
        { ConsoleKey.W, "Close editor" },
        { ConsoleKey.Z, "Undo" },
        { ConsoleKey.Y, "Redo" },
        { ConsoleKey.End, "Move to end of note" },
        { ConsoleKey.Home, "Move to start of note" },
        { ConsoleKey.LeftArrow, "Move to previous word" },
        { ConsoleKey.RightArrow, "Move to next word" },
        { ConsoleKey.K, "Toggle insert mode" },
        { ConsoleKey.Backspace, "Delete word" },
        { ConsoleKey.Delete, "Delete word (to the right)" },
        { ConsoleKey.N, "Create new note" },
        { ConsoleKey.M, "Create new folder" },
        { ConsoleKey.O, "Open current directory in file explorer" },
        { ConsoleKey.D8, "Open the settings file" },
        { ConsoleKey.UpArrow, "Preview the previous note" },
        { ConsoleKey.DownArrow, "Preview the next note" },
        { ConsoleKey.B, "Toggle primary color" },
        { ConsoleKey.U, "Toggle secondary color" },
        { ConsoleKey.I, "Toggle tertiary color" },
        { ConsoleKey.D1, "Toggle tree visibility" },
        { ConsoleKey.G, "Go to line" },
        { ConsoleKey.H, "Toggle the help panel" },
    };

    private static Dictionary<ConsoleKey, string> editor_regular_functions = new Dictionary<ConsoleKey, string>()
    {
        { ConsoleKey.Escape, "Unfocus editor / focus tree" },
        { ConsoleKey.Enter, "Insert new line" },
        { ConsoleKey.UpArrow, "Move cursor up" },
        { ConsoleKey.DownArrow, "Move cursor down" },
        { ConsoleKey.LeftArrow, "Move cursor left" },
        { ConsoleKey.RightArrow, "Nove cursor right" },
        { ConsoleKey.End, "Move to end of line" },
        { ConsoleKey.Home, "Move to start of line" },
        { ConsoleKey.Insert, "Toggle insert mode" },
        { ConsoleKey.Backspace, "Delete character" },
        { ConsoleKey.Delete, "Delete character (to the right)" },
        { ConsoleKey.F2, "Rename note" },
        { ConsoleKey.Tab, "Insert tab" }
    };

    /// <summary>
    /// Open the help panel and show it until a key is pressed
    /// </summary>
    public void ToggleHelpPanel()
    {
        bool cursorWasVisible = Console.CursorVisible;
        AnsiConsole.Cursor.Hide();

        int start_display_index = 0;
        ConsoleKeyInfo lastKeyPress = new ConsoleKeyInfo();
        _renderHelpPanel(lastKeyPress, start_display_index);

        while (true)
        {
            if (!Console.KeyAvailable)
            {
                Thread.Sleep(1000);
            }
            else
            {
                lastKeyPress = Console.ReadKey(true);

                if (lastKeyPress.Key == ConsoleKey.H && lastKeyPress.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    break;
                }
            }

            _renderHelpPanel(lastKeyPress, start_display_index);
            start_display_index++;
        }

        if (cursorWasVisible)
        {
            AnsiConsole.Cursor.Show();
        }
    }

    private void _renderHelpPanel(ConsoleKeyInfo lastKeyPressInfo, int start_display_index)
    {
        // The shortcut / key that the user pressed should be highlighted in the help panel

        // 2x2 grid. 1st column is tree and 2nd is editor. top is ctrl and bottom is regular
        var layout = new Layout("HelpScreen").SplitColumns(
            new Layout("Tree").SplitRows(
                new Layout("TreeCtrl"),
                new Layout("TreeRegular")
            ),
            new Layout("Editor").SplitRows(
                new Layout("EditorCtrl"),
                new Layout("EditorRegular")
            )
        );

        // Tree Ctrl
        layout.GetLayout("TreeCtrl").Update(_generateHelpPanel(lastKeyPressInfo, tree_ctrl_functions, isCtrl: true, "Tree Ctrl", start_display_index));

        // Editor Ctrl
        layout.GetLayout("EditorCtrl").Update(_generateHelpPanel(lastKeyPressInfo, editor_ctrl_functions, isCtrl: true, "Editor Ctrl", start_display_index));

        // Tree Regular
        layout.GetLayout("TreeRegular").Update(_generateHelpPanel(lastKeyPressInfo ,tree_regular_functions, isCtrl: false, "Tree Regular", start_display_index));

        // Editor Regular
        layout.GetLayout("EditorRegular").Update(_generateHelpPanel(lastKeyPressInfo, editor_regular_functions, isCtrl: false, "Editor Regular", start_display_index));

        Console.SetCursorPosition(0, 0);
        AnsiConsole.Write(layout);
    }

    /// <summary>
    /// Generate one of the four panels to display in the help panel
    /// </summary>
    /// <param name="keymap">The keymap mapping keys to shortcut descriptions</param>
    /// <param name="isCtrl">True if the <paramref name="keymap"/> is for control keys, false if it's for regulaer</param>
    /// <returns></returns>
    private Panel _generateHelpPanel(ConsoleKeyInfo lastKeyPressInfo, Dictionary<ConsoleKey, string> keymap, bool isCtrl, string panelHeader, int start_display_index)
    {
        int bufferHeight = (Console.BufferHeight - 4) / 2;

        int skip_amount = keymap.Count > bufferHeight ? start_display_index % (keymap.Count - bufferHeight + 1): 0;

        string ctrlString = isCtrl ? "Ctrl+" : "";
        return new Panel(
            keymap.Skip(skip_amount).Aggregate("", (acc, kv) =>
            {
                string color = kv.Key == lastKeyPressInfo.Key ? "aqua" : "yellow";
                return acc + $"[{color}]{ctrlString}{kv.Key}[/] - {kv.Value}\n";
            })
        ).Expand().RoundedBorder().Header("[aqua]" + panelHeader + "[/]").BorderColor(Color.Aqua);
    }
}
