using Spectre.Console;
using System.Linq;

namespace NoteWorthy;
internal class NoteEditor
{
    public static readonly int DISPLAY_HEIGHT = Console.BufferHeight - 2; // -2 for panel border on top and bottom

    /// <summary>
    /// The lines that make up the note
    /// </summary>
    private List<List<char>> lines;

    /// <summary>
    /// The history of the note. Used for undoing changes with Ctrl+Z.
    /// Keeps track of the note's state every 10 characters.
    /// Will only track the past 75 states.
    /// When an element is undone, it is moved to the <see cref="redoStack"/>.
    /// The type of this stack is a tuple of the previous content of the note and the cursor position
    /// </summary>
    private LimitedStack<(
        List<List<char>> note_content,
        (int cursor_line_num, int cursor_pos_in_line)
    )> history = new(maxSize: 75);

    /// <summary>
    /// Just like the history stack, except this stack keeps track of the states that were undone.
    /// Used for Ctrl+Y functionality.
    /// 
    /// Is not a limited stack because the redoStack can only hold as much as is in the history stack
    /// </summary>
    private Stack<(
        List<List<char>> note_content,
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
    /// Buffer width of the editor. This is the width of the editor minus the width of the note tree and the padding / borders.
    /// </summary>
    private int BUFFER_WIDTH = Console.BufferWidth - NoteTree.DISPLAY_WIDTH - 4;

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
    /// The index of the line to display at the top of the editor.
    /// Used for allowing scrolling. When the view is scrolled down by one, this value is incremented.
    /// </summary>
    private int display_lines_start_index = 0;

    /// <param name="note_path">File path of the note to show in the editor. Set NULL to not show any note</param>
    public NoteEditor(string? notePath)
    {
        this.note_path = notePath;
        this.lines = new();

        if (this.note_path == null) return;

        if (!File.Exists(this.note_path))
        {
            throw new FileNotFoundException();
        }

        foreach (string line in File.ReadAllLines(this.note_path))
        {
            this.lines.Add(line.ToList());
        }

        if (this.lines.Count == 0)
        {
            this.lines.Add(new List<char>());
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
        List<List<char>> noteCopy = lines.Select(line => new List<char>(line)).ToList();
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
        int top = line_num - display_lines_start_index;

        if (top > Console.BufferHeight - 2)
        {
            throw new ArgumentOutOfRangeException("Cursor top position is out of bounds");
        }

        if (left > BUFFER_WIDTH)
        {
            throw new ArgumentOutOfRangeException("Cursor left position is out of bounds");
        }

        Console.SetCursorPosition(left + NoteTree.DISPLAY_WIDTH + 2, top + 1);
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
        SaveState();

        // If there is only one line, there aren't enough lines to delete
        if (lines.Count == 1) return;

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
        SaveState();

        if (AtEndOfLine())
        {
            lines.Insert(line_num + 1, new List<char>());
            pos_in_line = 0;

            if (lines.Count >= NoteEditor.DISPLAY_HEIGHT)
            {
                // Scroll the view down by one line
                display_lines_start_index++;
            }
        }
        else
        {
            AnsiConsole.Cursor.Hide();

            // Pressing enter while not at the end of the line will take the text to the right of the cursor and move it to the newly-created line
            List<char> current_line = lines[line_num].Slice(0, pos_in_line);
            List<char> newline = lines[line_num].Slice(pos_in_line, lines[line_num].Count - pos_in_line);

            // Rewrite current line and append the new line
            lines[line_num] = current_line;
            lines.Insert(line_num + 1, newline);

            // Move cursor to beginning of new line
            pos_in_line = 0;
            line_num++;

            AnsiConsole.Cursor.Show();
        }

        unsaved_changes = true;
    }

    /// <summary>
    /// Insert <paramref name="c"/> at the current position in the editor.
    /// </summary>
    /// <param name="c">Char to insert</param>
    public void InsertChar(char c)
    {
        HandlePossibleStateSave();

        if (LineIsFull()) return;
        
        if (AtEndOfLine())
        {
            // Just add the character to the end of the line if the cursor is at the end of the line,
            // regardless of the mode (insert or overwrite)
            lines[line_num].Add(c);
        }
        else
        {
            if (insertModeOn)
            {
                // Insert the char at the current position
                lines[line_num].Insert(pos_in_line, c);
            }
            else
            {
                // Overwrite the char at the current positon
                lines[line_num][pos_in_line] = c;
            }
        }

        unsaved_changes = true;
        pos_in_line++;
    }

    /// <summary>
    /// Delete a character with the Backspace key.
    /// Deletes the character to the left of the cursor.
    /// </summary>
    public void DeleteCharWithBackspace()
    {
        HandlePossibleStateSave();

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
        HandlePossibleStateSave();

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
        SaveState();

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
        SaveState();

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
        SaveState();

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
        SaveState();

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

    private void HandlePossibleStateSave()
    {
        if (chars_since_last_state_save >= 10)
        {
            SaveState();
        }
        else
        {
            chars_since_last_state_save++;
        }
    }

    public void Undo()
    {
        if (history.IsEmpty) return;

        // Save the current state to the redo stack with a deep copy
        List<List<char>> noteCopy = lines.Select(line => new List<char>(line)).ToList();
        redoStack.Push(
            (note_content: noteCopy, (cursor_line_num: line_num, cursor_pos_in_line: pos_in_line))
        );

        // Get the last state from the history stack
        ( List<List<char>> note_content,
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
        List<List<char>> noteCopy = lines.Select(line => new List<char>(line)).ToList();
        history.Push(
            (note_content: noteCopy, (cursor_line_num: line_num, cursor_pos_in_line: pos_in_line))
        );

        // Get the last state from the redo stack
        (List<List<char>> note_content,
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
    private bool OnLastLine()
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
    /// Save the note to the file
    /// </summary>
    public void Save()
    {
        if (this.note_path == null) return;
        if (!unsaved_changes) return;

        // Note: do not call SaveState() in this function. It will create double entries in the history stack

        File.WriteAllLines(
            this.note_path,
            lines.Select((List<char> line) => new string(line.ToArray()).TrimEnd())
        );
        unsaved_changes = false;
    }

    /// <summary>
    /// Generate the <see cref="Spectre.Console.Panel"/> for displaying the note editor
    /// </summary>
    public Panel GenerateDisplayPanel()
    {
        if (this.note_path == null)
        {
            return new Panel(new Text("No note open"))
                .Expand()
                .RoundedBorder();
        }

        string unsaved_changes_indicator = unsaved_changes ? " *" : "";
        List<List<char>> lines_to_display = lines.GetRange(display_lines_start_index, Math.Min(lines.Count, NoteEditor.DISPLAY_HEIGHT));

        return new Panel(
            new Markup(
                string.Join("\n", lines_to_display.Select(
                    (List<char> line) => new string(line.ToArray()).TrimEnd())
                )
            )
        ).Header($"[yellow] {Path.GetFileName(note_path)}{unsaved_changes_indicator} [/]")
         .Expand()
         .RoundedBorder();
    }

    public void MoveCursorUp()
    {
        if (line_num == 0 && display_lines_start_index == 0) return;
        else if (line_num - display_lines_start_index == 0 && display_lines_start_index > 0)
        {
            // Scroll the view up by one line
            display_lines_start_index--;
            return;
        }

        line_num--;
        if (pos_in_line > lines[line_num].Count)
        {
            pos_in_line = lines[line_num].Count;
        }
    }

    public void MoveCursorDown()
    {
        if (lines.Count > NoteEditor.DISPLAY_HEIGHT && line_num == NoteEditor.DISPLAY_HEIGHT + display_lines_start_index - 1 && lines.Count > NoteEditor.DISPLAY_HEIGHT + display_lines_start_index)
        {
            // Scroll the view down by one line
            display_lines_start_index++;
        }

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
            if (line_num == 0) return;
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
        display_lines_start_index = 0;
    }

    public void MoveCursorToEndOfEditor()
    {
        line_num = lines.Count - 1;
        pos_in_line = lines[line_num].Count;
        display_lines_start_index = lines.Count - NoteEditor.DISPLAY_HEIGHT;
    }

    public void MoveLineUp()
    {
        if (line_num == 0) return;
        SaveState();

        List<char> temp = lines[line_num];
        lines[line_num] = lines[line_num - 1];
        lines[line_num - 1] = temp;

        line_num--;
        unsaved_changes = true;
    }

    public void MoveLineDown()
    {
        if (line_num == lines.Count - 1) return;
        SaveState();

        List<char> temp = lines[line_num];
        lines[line_num] = lines[line_num + 1];
        lines[line_num + 1] = temp;

        line_num++;
        unsaved_changes = true;
    }
}
