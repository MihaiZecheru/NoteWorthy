using Spectre.Console;
using System.Text;
using TextCopy;

namespace NoteWorthy;
internal class NoteEditor
{
    /// <summary>
    /// ColorChar representing a space. Used in a lot of shortcuts.
    /// </summary>
    private ColorChar SPACE = new ColorChar((byte)' ', 0);

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
    /// The current line. lines[curr_line_index]
    /// </summary>
    private List<ColorChar> curr_line { get => lines[curr_line_index]; set => lines[curr_line_index] = value; }

    /// <summary>
    /// The current character. curr_char
    /// </summary>
    private ColorChar curr_char { get => lines[curr_line_index][curr_char_index]; set => lines[curr_line_index][curr_char_index] = value; }

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
    private int curr_line_index = 0;

    /// <summary>
    /// The position of the cursor in the current line
    /// </summary>
    private int curr_char_index = 0;

    /// <summary>
    /// True if there are unsaved changes
    /// </summary>
    private bool unsaved_changes = false;

    /// <summary>
    /// If true, typing chars will insert them into the line. If false, typing chars will overwrite the whatever is at the cursor.
    /// Is initially the value in the Settings file.
    /// </summary>
    private bool insertModeOn = Settings.InsertMode;

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

        SaveState();
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
        ColorChar[] elipsis = { SPACE, SPACE, SPACE, SPACE };

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

        // Deep copy the note to save its state.
        List<List<ColorChar>> noteCopy = lines.Select(line => new List<ColorChar>(line)).ToList();

        history.Push(
            (note_content: noteCopy, (cursor_line_num: curr_line_index, cursor_pos_in_line: curr_char_index))
        );
    }

    public void ToggleInsertMode()
    {
        insertModeOn = !insertModeOn;
    }

    /// <summary>
    /// Set the cursor position in the editor to (<see cref="curr_char_index"/>, <see cref="curr_line_index"/>).
    /// (0, 0) is the top left corner of the panel with the editor.
    /// Does not correspond to the actual console cursor position.
    /// </summary>
    public void UpdateCursorPosInEditor()
    {
        int left = curr_char_index;
        int top = curr_line_index;

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
            curr_char_index = 0;
            return;
        }

        lines.RemoveAt(curr_line_index);
        
        if (curr_line_index == lines.Count)
        {   
            curr_line_index--;
        }

        curr_char_index = 0;
        Set_unsaved_changes();
    }

    /// <summary>
    /// Insert line at the current position.
    /// 
    /// Accounts for special cases like the automatic dash + space indent and the automatic vocab-indent when a word is defined with a semicolon
    /// </summary>
    /// <param name="direct_insert">Default false. True when pasting. If true, the new line will be added without any formatting changes
    /// or anything else. The new line is inserted simply without any other modifications in order to keep the paste pure.</param>
    public void InsertLine(bool direct_insert = false)
    {
        // True to set auto_capitalization_info.last_change_was_an_auto_capitalization_or_auto_color to false at the end.
        bool reset_auto_capitalization_info = true;

        if (lines.Count == BUFFER_HEIGHT) return;

        if (AtEndOfLine())
        {
            List<char> line_chars = curr_line.Select(l => l.Char).ToList();

            // Auto-capitalization check
            // Only auto-capitalize in the event of an 'enter' keypress when there is only one word written on the line, because then a space wouldn't have been pressed which is where the word typically gets capitalized
            if (Settings.AutoCapitalizeLines && GetSpacesCountInLine(curr_line) == 0 && curr_line.Count >= 1 && char.IsAsciiLetterLower(curr_line[0].Char) && curr_line[0].Char != ' ')
            {
                curr_line[0].Char = char.ToUpper(curr_line[0].Char);
                auto_capitalization_slash_auto_color_info = ((curr_line_index, 0), true);
                reset_auto_capitalization_info = false;
            }
            // Auto-capitalize 'i' check
            // If enter is pressed when the last two chars are ' i' then auto capitalize i
            else if (Settings.AutoCapitalizeLines && curr_line.Count >= 2 && curr_line[curr_char_index - 1] == 'i' && curr_line[curr_char_index - 2] == ' ')
            {
                curr_line[curr_char_index - 1].Char = 'I';
                auto_capitalization_slash_auto_color_info = ((curr_line_index, curr_char_index - 1), true);
                reset_auto_capitalization_info = false;
            }

            // If the current line is indented + dash + space, the new line will be indented with a dash + space too
            // Ex, if it starts with this: "    - "
            if (!direct_insert && curr_line.Count >= 6 && curr_line[0] == ' ' && curr_line[1] == ' ' && curr_line[2] == ' ' && curr_line[3] == ' ' && curr_line[4] == '-' && curr_line[5] == ' ')
            {
                // If enter is pressed when the line is just a dash + space, delete the dash + space in that line.
                if (curr_line.Count == 6)
                {
                    // Clear current line
                    curr_line = new();
                    curr_char_index = 0;
                    return;
                }
                else
                {
                    lines.Insert(curr_line_index + 1, new List<ColorChar>()
                    {
                        SPACE, SPACE, SPACE, SPACE, new ColorChar((byte)'-', 0), SPACE
                    });
                    curr_char_index = 6;
                }
            }
            // If enter is pressed when the line is just a bunch of spaces, delete the spaces in that line.
            // Used for when the vocab definition indent occurs but the user doesn't use it and goes to the next line; in this case the
            // indent that isn't wanted should be cleared
            else if (!direct_insert && line_chars.All(c => c == ' '))
            {
                // Clear current line
                curr_line = new();
                // Add new empty line
                lines.Insert(curr_line_index + 1, new());
                curr_char_index = 0;
            }
            // If enter is pressed when the line starts with more than 4 spaces (a tab) but isn't JUST the spaces, then tab the new line enough
            // to match up. Note: by this point, the entire not isn't spaces because of the previous condition
            else if (!direct_insert && line_chars.Count > 4 && line_chars[0] == ' ' && line_chars[1] == ' ' && line_chars[2] == ' ' && line_chars[3] == ' ')
            {
                ColorChar space_char = new((byte)' ', 0);
                int spaces_to_add = GetLeadingSpacesCount(new string(curr_line.Select(c => c.Char).ToArray()));

                lines.Insert(curr_line_index + 1, new());

                for (int i = 0; i < spaces_to_add; i++)
                {
                    lines[curr_line_index + 1].Add(space_char);
                }

                curr_char_index = spaces_to_add;
            }
            // Normal enter keypress, no special cases
            else
            {
                lines.Insert(curr_line_index + 1, new());
                curr_char_index = 0;
            }
        }
        else
        {
            AnsiConsole.Cursor.Hide();

            // Pressing enter while not at the end of the line will take the text to the right of the cursor and move it to the newly-created line
            List<ColorChar> current_line = curr_line.Slice(0, curr_char_index);
            List<ColorChar> newline = curr_line.Slice(curr_char_index, curr_line.Count - curr_char_index);

            // Rewrite current line and append the new line
            curr_line = current_line;
            lines.Insert(curr_line_index + 1, newline);

            // Move cursor to beginning of new line
            curr_char_index = 0;
            AnsiConsole.Cursor.Show();
        }

        curr_line_index++;
        if (reset_auto_capitalization_info && auto_capitalization_slash_auto_color_info.last_change_was_an_auto_capitalization_or_auto_color)
            auto_capitalization_slash_auto_color_info = ((0, 0), false);
        Set_unsaved_changes();
    }

    /// <summary>
    /// Get the amount of leading spaces in a string
    /// Ex: "    - " will return 4
    /// </summary>
    private int GetLeadingSpacesCount(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ') count++;
            else break;
        }
        return count;
    }

    /// <summary>
    /// Used so that if the user pressed the backspace after their word was auto-capitalized, it will undo that
    /// `line` is the line index where the auto-capitalization occurred, and `col` is the curr_char_index when the auto-capitalization occurred
    /// last_change_was_an_auto_capitalization_or_auto_color is true when the very last change to the text in the editor was an auto-capitalization
    /// </summary>
    private ((int line, int col), bool last_change_was_an_auto_capitalization_or_auto_color) auto_capitalization_slash_auto_color_info = ((0, 0), false);

    /// <summary>
    /// Insert <paramref name="c"/> at the current position in the editor.
    /// </summary>
    /// <param name="c">Char to insert</param>
    /// <param name="direct_insert">False by default. True when pasting.
    /// If true, auto-capitalization and indent-matching will be skipped.
    /// The character will be inserted directly without any of the features that are meant for when the user is typing by hand.
    /// Modifications regarding color will be preserved, i.e. AutoColorNumbers, AutoColorVariables, etc.</param>
    /// <returns>Returns true if the display should be updated. Only returns false when a character is inserted normally and with no color. Otherwise, if there's a special condition, will return true.</returns>
    public bool InsertChar(char c, bool direct_insert = false)
    {
        // Note: do not replace highlighted chars with `c`; it would be too resource-heavy

        // True to set auto_capitalization_info.last_change_was_an_auto_capitalization_or_auto_color to false at the end.
        bool reset_auto_capitalization_info = true;

        // True if the first char was autocapitalized due to the setting
        bool was_auto_capitalized = false;

        // True if the display should be updated
        bool update_display = true;

        /**
         * Checks to see if the character will overflow to the next line
         * If there is overflow, there are two possible circumstances:
         * (1) the line contains multiple words, and therefore the last word will be wrapped to the next line if there are lines available
         * (2) the line is one big word (due to the user spamming characters or having a dashed line from Ctrl+L), and therefore the character is added to the next line if there are lines available
        */

        // True if the line was full and the last word/char was automatically moved to the next line
        bool auto_moved_to_next_line = false;

        // If the char is not ascii
        if (c < 0 || c > 127)
        {
            c = RemoveDiacritics(c);
        }

        if (LineIsFull() && !AtEndOfLine()) return false;

        // Check for auto move to next line
        if (AtEndOfLine() && LineIsFull())
        {
            // Add new line
            if (curr_line_index < BUFFER_HEIGHT - 1)
            {
                lines.Insert(curr_line_index + 1, new List<ColorChar>());
                auto_moved_to_next_line = true;
            }
            else
            {
                return false;
            }

            bool curr_line_is_a_dash_indent = curr_line[0] == ' ' && curr_line[1] == ' ' && curr_line[2] == ' '
                && curr_line[3] == ' ' && curr_line[4] == '-' && curr_line[5] == ' ';

            // Check for auto indent due to the overflowing line being a dash + space indent
            if (curr_line_is_a_dash_indent)
            {
                lines[curr_line_index + 1] = new List<ColorChar>()
                {
                    SPACE, SPACE, SPACE, SPACE, SPACE, SPACE
                };
            }

            // If line is multiple words (at least 3), then bring past word to next line
            if (c != ' ' && curr_line[curr_char_index - 1] != ' ' && GetSpacesCountInLine(curr_line) >= 3)
            {
                // TODO: chek for edge case: "    - " space + dash combo, auto indent if that's the case
                int start_of_last_word_index = FindIndexOf_StartOfPreviousWord() + 1; // +1 to leave the space
                List<ColorChar> word = curr_line.GetRange(start_of_last_word_index, curr_line.Count - start_of_last_word_index);
                lines[curr_line_index + 1].AddRange(word);
                curr_line.RemoveRange(start_of_last_word_index, curr_line.Count - start_of_last_word_index);
                curr_line_index++;
                curr_char_index = word.Count;
                if (curr_line_is_a_dash_indent) curr_char_index += 6;
            }
            else
            {
                curr_line_index++;
                curr_char_index = curr_line_is_a_dash_indent ? 6 : 0;
                // If the last char in the line is a space, don't worry bout bringing the last word over;
                // let the user write his word on the new line
                if (c == ' ') return true;
            }
        }

        // Auto-capitalize the first word if the setting is on and the user just finished typing the word (i.e. if character to insert `c` is the first space in the line)
        // The 'or' condition is for checking for "    - " case
        if (
            c == ' '
            &&
            !direct_insert
            &&
            Settings.AutoCapitalizeLines
            &&
            curr_line.Count >= 1
            &&
            char.IsAsciiLetterLower(curr_line[0].Char)
            &&
            GetSpacesCountInLine(curr_line) == 0
        )
        {
            // Capitalize the first char in the line
            curr_line[0].Char = char.ToUpper(curr_line[0].Char);
            auto_capitalization_slash_auto_color_info = ((curr_line_index, 0), true);
            reset_auto_capitalization_info = false;
            was_auto_capitalized = true;
        }
        // auto-capitalize the first word but when it's after a dash + space ("    - ")
        else if (
            c == ' '
            &&
            !direct_insert
            &&
            Settings.AutoCapitalizeLines
            &&
            curr_line.Count >= 7 && curr_line[0] == ' ' && curr_line[1] == ' ' && curr_line[2] == ' ' && curr_line[3] == ' ' && curr_line[4] == '-' && curr_line[5] == ' '
            && GetSpacesCountInLine(curr_line) == 5 // checking for if this space is the first space not including the spaces in "    - "
        )
        {
            curr_line[6].Char = char.ToUpper(curr_line[6].Char);
            auto_capitalization_slash_auto_color_info = ((curr_line_index, 6), true);
            reset_auto_capitalization_info = false;
            was_auto_capitalized = true;
        }
        // Check to auto-capitalize the letter 'i'. Works when the is proceeds and follows a space, ex: ' i '
        else if (
            c == ' '
            &&
            !direct_insert
            &&
            Settings.AutoCapitalizeLines
            &&
            curr_line.Count >= 3
            &&
            curr_char_index >= 2
            &&
            curr_line[curr_char_index - 1] == 'i'
            &&
            curr_line[curr_char_index - 2] == ' '
        )
        {
            curr_line[curr_char_index - 1].Char = 'I';
            auto_capitalization_slash_auto_color_info = ((curr_line_index, curr_char_index - 1), true);
            reset_auto_capitalization_info = false;
        }

        // Add color to char
        ColorChar _char;
        if (primary_color_on || secondary_color_on || tertiary_color_on)
        {
            byte color = primary_color_on ? Settings.PrimaryColor : secondary_color_on ? Settings.SecondaryColor : Settings.TertiaryColor;
            
            // Set the char
            _char = new ColorChar((byte)c, color);

            // The color was only activated for a single character. Use the color on this character and then turn the color off
            if (colorActiveForOneChar)
            {
                primary_color_on = false;
                secondary_color_on = false;
                tertiary_color_on = false;
                colorActiveForOneChar = false;
            }
        }
        else
        {
            // If AutoColorNumbers is on and the char is a number, color it with the SecondaryColor as specified in the settings file
            if (Settings.AutoColorNumbers && char.IsAsciiDigit(c))
            {
                _char = new ColorChar((byte)c, Settings.SecondaryColor);
            }
            // If AutoColorVariables is on and the char is a space, and two chars before is a space, then make one char before have the PrimaryColor as specified in the settings file
            // ex: " x " the x becomes colored
            else if (Settings.AutoColorVariables && curr_line.Count >= 1 && curr_char_index >= 1 && (c == ' ' || char.IsSymbol(c)) && curr_line[curr_char_index - 1].Char != ' ' && (curr_char_index - 1 == 0 || curr_line[curr_char_index - 2] == ' ' || char.IsSymbol(curr_line[curr_char_index - 2].Char)) && char.IsAsciiLetter(curr_line[curr_char_index - 1].Char))
            {
                // The space that was typed
                _char = new ColorChar((byte)c, 0);

                // Changing the color of the previous variable
                char char_to_color = curr_line[curr_char_index - 1].Char;

                // Do not color 'A', 'a', 'I', or 'i'
                if (char.ToLower(char_to_color) != 'a' && char.ToLower(char_to_color) != 'i')
                {
                    // Replace the variable with a colored version of the char
                    auto_capitalization_slash_auto_color_info = ((curr_line_index, curr_char_index - 1), true);
                    reset_auto_capitalization_info = false;
                    curr_line[curr_char_index - 1] = new ColorChar((byte)char_to_color, Settings.PrimaryColor);
                }
            }
            // Add the char with no color
            else
            {
                // 0 is the null value (no color specified)
                _char = new ColorChar((byte)c, 0);
            }
        }

        AnsiConsole.Cursor.Hide();

        // Insert char
        if (AtEndOfLine())
        {
            // Just add the character to the end of the line if the cursor is at the end of the line,
            // regardless of the mode (insert or overwrite)

            // Before adding the char, check for the dash + space indent thing.
            // If a dash then a space is typed and the line prior contains text (isn't empty), the dash will automatically be tabbed
            if (c == ' ' && !direct_insert && curr_line.Count == 1 && curr_line[0] == '-' && curr_line_index >= 1 && lines[curr_line_index - 1].Count != 0)
            {
                // _char is a space here
                curr_line = new List<ColorChar>()
                {
                    _char, _char, _char, _char, new ColorChar((byte)'-', 0), _char
                };
                curr_char_index = 5;
            }
            else
            {
                // Add char normally
                curr_line.Add(_char);
                if (_char.Color == null)
                {
                    Console.Write(_char.Char);
                    update_display = false;
                }
            }
        }
        else
        {
            if (insertModeOn)
            {
                // Add the char to the end of the line
                if (curr_char_index == curr_line.Count - 1)
                {
                    curr_line.Add(_char);

                    if (_char.Color == null)
                    {
                        Console.Write(_char.Char);
                    }
                    else
                    {
                        AnsiConsole.Markup($"[{_char.Color.Value}]{_char.Char}[/]");
                    }
                }
                else
                {
                    // Insert the char at the current position
                    curr_line.Insert(curr_char_index, _char);
                    
                    // Logic for rewrite the line as the char was inserted
                    StringBuilder markup = new(capacity: (curr_line.Count - curr_char_index - 1) * 5);

                    if (_char.Color == null)
                    {
                        markup.Append(_char.Char);
                    }
                    else
                    {
                        markup.Append($"[{_char.Color.Value}]{_char.Char}[/]");
                    }

                    for (int i = curr_char_index + 1; i < curr_line.Count; i++)
                    {
                        ColorChar _c = curr_line[i];
                        if (_c.Color == null)
                            markup.Append(HandleSquareBrackets(_c.Char));
                        else
                            markup.Append($"[{_c.Color.Value}]{_c.Char}[/]");
                    }

                    AnsiConsole.Markup(markup.ToString());
                }

                update_display = false;
            }
            else
            {
                // Overwrite the char at the current positon
                curr_line[curr_char_index] = _char;

                if (_char.Color == null)
                {
                    Console.Write(_char.Char);
                    update_display = false;
                }
            }
        }

        // True if a vocab definitiion was auto-colored as a result of this char press
        bool vocab_definition_was_auto_colored = false;

        // If the char to insert is a space, check for a possible vocab definition (FOR Settings.AutoColorVocabDefinitions)
        // Vocab definitions are detected when there is a colon followed by a space in the line prior to the 2/5-point of the line 
        // The : cannot be the first character
        if (c == ' ' && Settings.AutoColorVocabDefinitions && curr_char_index >= 3 && lines[curr_line_index][curr_char_index - 1] == ':' && curr_char_index <= BUFFER_WIDTH * 2/5)
        {
            int tmp = curr_char_index;

            // Color the word up to the colon
            for (int i = 0; i < curr_char_index - 1; i++)
            {
                curr_line[i] = new ColorChar((byte)curr_line[i].Char, Settings.PrimaryColor);
            }

            curr_char_index = tmp;
            vocab_definition_was_auto_colored = true;
        }
        // If the char to insert is a space, check for a possible vocab definition (FOR Settings.AutoColorVocabDefinitions)
        // Alternate vocab definition is: "word - definition". Note the ' - ' in the middle
        else if (c == ' ' && Settings.AutoColorVocabDefinitions && curr_char_index >= 3 && lines[curr_line_index][curr_char_index - 2] == ' ' && lines[curr_line_index][curr_char_index - 1] == '-' && curr_char_index <= BUFFER_WIDTH * 2/5)
        {
            int tmp = curr_char_index;

            // Color the word up to the dash
            for (int i = 0; i < curr_char_index - 2; i++)
            {
                curr_line[i] = new ColorChar((byte)curr_line[i].Char, Settings.PrimaryColor);
            }

            curr_char_index = tmp;
            vocab_definition_was_auto_colored = true;
        }

        Set_unsaved_changes();
        curr_char_index++;
        if (reset_auto_capitalization_info && auto_capitalization_slash_auto_color_info.last_change_was_an_auto_capitalization_or_auto_color)
            auto_capitalization_slash_auto_color_info = ((0, 0), false);
        UpdateCursorPosInEditor();
        AnsiConsole.Cursor.Show();
        return update_display || was_auto_capitalized || vocab_definition_was_auto_colored || auto_moved_to_next_line;
    }

    /// <summary>
    /// Get the amount of spaces in <paramref name="line"/>
    /// </summary>
    /// <param name="line">A copy of a line in <see cref="lines"/></param>
    /// <returns>The amount of spaces in <paramref name="line"/></returns>
    private int GetSpacesCountInLine(List<ColorChar> line)
    {
        int spaces_count = 0;

        for (int i = 0; i < line.Count; i++)
        {
            if (line[i].Char == ' ') spaces_count++;
        }

        return spaces_count;
    }

    private long ms_since_last_change = 0;

    /// <summary>
    /// Returns true if an auto save should occur, which should happen when 1500ms have passed since the last char has been typed.
    /// If <see cref="auto_save_give_extra_time"/> is <see langword="true"/>, then the user is given 7500ms before the auto save occurs.
    /// </summary>
    public bool CheckIfAutoSaveRequired()
    {
        int interval = auto_save_give_extra_time ? 7500 : 1500;
        bool auto_save = ms_since_last_change != 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ms_since_last_change > interval;
        
        if (auto_save)
        {
            ms_since_last_change = 0;
        }

        return auto_save;
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
    /// <returns>Returns true if display should be updated. Only returns false when a character is deleted normally. Otherwise, if there's a special condition, will return true.</returns>
    public bool DeleteCharWithBackspace()
    {
        if (auto_capitalization_slash_auto_color_info.last_change_was_an_auto_capitalization_or_auto_color)
        {
            int y = auto_capitalization_slash_auto_color_info.Item1.line;
            int x = auto_capitalization_slash_auto_color_info.Item1.col;
            lines[y][x] = new ColorChar((byte) char.ToLower(lines[y][x].Char), 0);
            auto_capitalization_slash_auto_color_info.last_change_was_an_auto_capitalization_or_auto_color = false;
            return true;
        }

        if (SomeCharsAreHighlighted())
        {
            DeleteHighlightedChars();
            return true;
        }

        if (AtBeginningOfLine())
        {
            if (OnFirstLine()) return false;
            if (curr_line.Count + lines[curr_line_index - 1].Count > BUFFER_WIDTH) return false;
            int prev_line_length = GetLineLength(curr_line_index - 1);
            lines[curr_line_index - 1].AddRange(curr_line);
            lines.RemoveAt(curr_line_index);
            curr_line_index--;
            curr_char_index = prev_line_length;
            return true;
        }
        // Not at beginning of line - just remove the char, but first check conditions for deleting tabs and "    - "
        else
        {
            // Check if line is equal to "    - " and the cursor is at the end of the line. If true, delete the whole line
            if (curr_line.Count == 6 && curr_line[0] == ' ' && curr_line[1] == ' ' && curr_line[2] == ' ' && curr_line[3] == ' ' && curr_line[4] == '-' && curr_line[5] == ' ')
            {
                curr_line = new();
                curr_char_index = 0;
            }
            // Check if the entire line is spaces. If so, clear proceeding spaces
            else if (AtEndOfLine() && curr_line.All(c => c == ' '))
            {
                // Clear spaces
                curr_line = new();
                curr_char_index = 0;
            }
            // Check if the cursor is at the end of the line and the previous four chars make a tab. If true, delete the tab
            else if (curr_line.Count >= 4 && curr_char_index >= 4 && curr_line[curr_char_index - 1] == ' ' && curr_line[curr_char_index - 2] == ' ' && curr_line[curr_char_index - 3] == ' ' && curr_line[curr_char_index - 4] == ' ')
            {
                curr_line.RemoveRange(curr_char_index - 4, 4);
                curr_char_index -= 4;
            }
            // Delete the char normally
            else
            {
                curr_line.RemoveAt(curr_char_index - 1);
                curr_char_index--;
                AnsiConsole.Cursor.Hide();
                // If char is at the end of line, do the \b \b thing to delete the char
                // Otherwise, the line needs to be rewritten to account for the deleted char, assuming the deleted char was somewhere in the middle of the line
                
                if (AtEndOfLine())
                {
                    Console.Write("\b \b");
                }
                else
                {
                    if (Console.CursorLeft > 0) Console.CursorLeft--;
                    Console.Write(new string(curr_line.Select(c => c.Char).ToArray()).Substring(curr_char_index) + " ");
                    UpdateCursorPosInEditor();
                }

                AnsiConsole.Cursor.Show();
                Set_unsaved_changes();
                return false;
            }

            Set_unsaved_changes();
            return true;
        }
    }

    /// <summary>
    /// Delete a single character with the delete key.
    /// Deletes the char to the right of the cursor.
    /// </summary>
    public bool DeleteCharWithDeleteKey()
    {
        if (SomeCharsAreHighlighted())
        {
            DeleteHighlightedChars();
            return true;
        }

        Set_unsaved_changes();

        if (AtEndOfLine())
        {
            if (OnLastLine()) return false;
            curr_line.AddRange(lines[curr_line_index + 1]);
            lines.RemoveAt(curr_line_index + 1);
            return false;
        }
        else
        {
            curr_line.RemoveAt(curr_char_index);
            Set_unsaved_changes();
            return true;
        }
    }

    /// <summary>
    /// Delete a word with Ctrl+Backspace.
    /// Deletes the word to the left of the cursor.
    /// </summary>
    public void DeleteWordWithBackspace()
    {
        if (SomeCharsAreHighlighted())
        {
            DeleteHighlightedChars();
            return;
        }

        if (AtBeginningOfLine())
        {
            // If the cursor is at the beginning of the first line,
            // there is nothing to delete on the current line.
            // Instead, delete the current line and append it to the previous line

            if (OnFirstLine()) return;
            if (curr_line.Count + lines[curr_line_index - 1].Count > BUFFER_WIDTH) return;
            int prev_line_length = GetLineLength(curr_line_index - 1);
            lines[curr_line_index - 1].AddRange(curr_line);
            lines.RemoveAt(curr_line_index);
            curr_line_index--;
            curr_char_index = prev_line_length;
        }
        else
        {
            int start_of_word = FindIndexOf_StartOfPreviousWord();
            // Leave the space at the end of the word
            if (start_of_word != 0) start_of_word++;
            // Delete word
            curr_line.RemoveRange(start_of_word, curr_char_index - start_of_word);
            curr_char_index = start_of_word;
        }

        Set_unsaved_changes();
    }
    
    /// <summary>
    /// Delete a word with Ctrl+Delete.
    /// Deletes the word to the right of the cursor.
    /// </summary>
    public void DeleteWordWithDeleteKey()
    {
        if (SomeCharsAreHighlighted())
        {
            DeleteHighlightedChars();
            return;
        }

        if (AtEndOfLine())
        {
            // If the cursor is at the end of the last line,
            // there is nothing to delete on the current line.
            // Instead, delete the current line and append it to the next line

            if (OnLastLine()) return;
            curr_line.AddRange(lines[curr_line_index + 1]);
            lines.RemoveAt(curr_line_index + 1);
        }
        else
        {
            int end_of_word = FindIndexOf_EndOfNextWord();
            curr_line.RemoveRange(curr_char_index, end_of_word - curr_char_index);
            // curr_char_index does not have to be modified because the word to the RIGHT of the cursor is deleted
        }

        Set_unsaved_changes();
    }

    private bool auto_save_give_extra_time = false;
    private void Set_unsaved_changes(bool give_extra_time = false)
    {
        ms_since_last_change = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        auto_save_give_extra_time = give_extra_time;
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

        int end_of_word = curr_char_index;

        // Loop I: Clear duplicate spaces at the beginning of the word (left side of the word)
        while (end_of_word < curr_line.Count - 1 && curr_line[end_of_word] == ' ')
        {
            end_of_word++;
        }

        // Loop II: Clear the word (all text up until the next space)
        while (end_of_word < curr_line.Count - 1 && curr_line[end_of_word] != ' ')
        {
            end_of_word++;
        }

        // Loop III: Clear duplicate spaces at the end of the word (right side of the word)
        while (end_of_word < curr_line.Count && curr_line[end_of_word] == ' ')
        {
            end_of_word++;
        }

        // Loop III will stop deleting just one char before the end of the line always, because if it's at the end of the line,
        // the last character can't be a space. Therefore, if the end of the word is at the end of the line, account for that.

        // However, if the word is a single char, like the word 'a', it shouldn't do that. So, also check if the 2nd-last char is a space before accounting for Loop III
        if (end_of_word == curr_line.Count - 1 && end_of_word >= 1 && curr_line[end_of_word - 1] != ' ') end_of_word++;

        // If there is only one char to the end with no space following it, the end of word will equal the curr_char_index, meaning that
        // it there is no word. However, if the end_of_word is prior to the end of the line, then there is a word and it just wasn't caught
        if (end_of_word == curr_char_index && end_of_word < curr_line.Count) end_of_word = curr_line.Count;

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

        int start_of_word = curr_char_index - 1;

        // Loop I: Clear duplicate spaces at the end of the word (right side of the word)
        while (start_of_word > 0 && curr_line[start_of_word] == ' ')
        {
            start_of_word--;
        }

        // Loop II: Clear the word (all text up until the next space)
        while (start_of_word > 0 && curr_line[start_of_word] != ' ')
        {
            start_of_word--;
        }

        // Loop III: Clear duplicate spaces at the beginning of the word (left side of the word)
        while (start_of_word > 0 && curr_line[start_of_word] == ' ')
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
            curr_line_index--;
            curr_char_index = GetCharsInLine();
        }
        else
        {
            curr_char_index = FindIndexOf_StartOfPreviousWord();
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
            curr_line_index++;
            curr_char_index = 0;
        }
        else
        {
            curr_char_index = FindIndexOf_EndOfNextWord();
        }
    }

    public void Undo()
    {
        if (history.IsEmpty) return;

        // Save the current state to the redo stack with a deep copy
        List<List<ColorChar>> noteCopy = lines.Select(line => new List<ColorChar>(line)).ToList();
        redoStack.Push(
            (note_content: noteCopy, (cursor_line_num: curr_line_index, cursor_pos_in_line: curr_char_index))
        );

        // Get the last state from the history stack
        ( List<List<ColorChar>> note_content,
          (int cursor_line_num, int cursor_pos_in_line) ) lastState = history.Pop();
        lines = lastState.note_content;
        curr_line_index = lastState.Item2.cursor_line_num;
        curr_char_index = lastState.Item2.cursor_pos_in_line;
        Set_unsaved_changes(give_extra_time: true);
    }

    public void Redo()
    {
        if (redoStack.Count == 0) return;

        // Save the current state to the history stack with a deep copy
        List<List<ColorChar>> noteCopy = lines.Select(line => new List<ColorChar>(line)).ToList();
        history.Push(
            (note_content: noteCopy, (cursor_line_num: curr_line_index, cursor_pos_in_line: curr_char_index))
        );

        // Get the last state from the redo stack
        (List<List<ColorChar>> note_content,
          (int cursor_line_num, int cursor_pos_in_line)) lastState = redoStack.Pop();
        lines = lastState.note_content;
        curr_line_index = lastState.Item2.cursor_line_num;
        curr_char_index = lastState.Item2.cursor_pos_in_line;
        Set_unsaved_changes(give_extra_time: true);
    }

    /// <summary>
    /// Return the amount of characters in the given <paramref name="_line_num"/>
    /// </summary>
    private int GetLineLength(int _line_num)
    {
        return lines[_line_num].Count;
    }

    /// <summary>
    /// Returns true if the cursor is at the beginning of the current line
    /// </summary>
    /// <returns></returns>
    private bool AtBeginningOfLine()
    {
        return curr_char_index == 0;
    }

    /// <summary>
    /// Returns true if the cursor is at the end of the current line (does not necessarily mean the cursor is at the end of the buffer)
    /// </summary>
    private bool AtEndOfLine()
    {
        // Does not use .Count - 1 because the cursor goes after the last char
        return curr_char_index == curr_line.Count;
    }

    /// <summary>
    /// Returns true if the cursor is somewhere in the first line
    private bool OnFirstLine()
    {
        return curr_line_index == 0;
    }

    /// <summary>
    /// Returns true if the cursor is somewhere in the last line
    /// </summary>
    public bool OnLastLine()
    {
        return curr_line_index == lines.Count - 1;
    }

    /// <summary>
    /// Returns true if the current line cannot hold any more characters.
    /// </summary>
    private bool LineIsFull()
    {
        return curr_line.Count == BUFFER_WIDTH;
    }

    private bool LineIsEmpty()
    {
        return curr_line.Count == 0;
    }

    /// <summary>
    /// Returns the number of characters in the current line.
    /// Used for checking if the cursor is at the end of the line or moving the cursor to the end of the line.
    /// </summary>
    /// <returns></returns>
    private int GetCharsInLine()
    {
        return curr_line.Count;
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

        unsaved_changes = false;
        ms_since_last_change = 0;
        SaveState();

        // Get bytes
        List<byte> bytes = new();
        for (int i = 0; i < this.lines.Count; i++)
        {
            foreach (ColorChar c in this.lines[i])
            {
                (byte char_byte, byte color_byte) = c.GetBytes();
                bytes.Add(char_byte);
                bytes.Add(color_byte);
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
            return new Panel(GetDisplayMarkup()).Header($"[yellow] {Path.GetFileName(note_path)} [/]")
             .Expand()
             .RoundedBorder();
        }
    }

    public void MoveCursorUp()
    {
        if (OnFirstLine())
        {
            curr_char_index = 0;
            return;
        }

        curr_line_index--;
        if (curr_char_index > curr_line.Count)
        {
            curr_char_index = curr_line.Count;
        }
    }

    public void MoveCursorDown()
    {
        if (OnLastLine())
        {
            int end_of_line = curr_line.Count;
            if (curr_char_index < end_of_line)
            {
                curr_char_index = end_of_line;
            }
        }
        else
        {
            curr_line_index++;
            if (curr_char_index > curr_line.Count)
            {
                curr_char_index = GetCharsInLine();
            }
        }
    }

    public void MoveCursorLeft()
    {
        if (curr_char_index == 0)
        {
            if (OnFirstLine()) return;
            curr_line_index--;
            curr_char_index = GetCharsInLine();
        }
        else
        {
            curr_char_index--;
        }
    }

    public void MoveCursorRight()
    {
        if (curr_char_index == GetCharsInLine())
        {
            if (OnLastLine()) return;
            curr_line_index++;
            curr_char_index = 0;
        }
        else
        {
            curr_char_index++;
        }
    }

    public void MoveCursorToStartOfLine()
    {
        curr_char_index = 0;
    }

    public void MoveCursorToEndOfLine()
    {
        curr_char_index = curr_line.Count;
    }

    public void MoveCursorToStartOfEditor()
    {
        curr_line_index = 0;
        curr_char_index = 0;
    }

    public void MoveCursorToEndOfEditor()
    {
        curr_line_index = lines.Count - 1;
        curr_char_index = curr_line.Count;
    }

    public void MoveLineUp()
    {
        if (OnFirstLine()) return;

        List<ColorChar> temp = curr_line;
        curr_line = lines[curr_line_index - 1];
        lines[curr_line_index - 1] = temp;

        curr_line_index--;
        Set_unsaved_changes();
    }

    public void MoveLineDown()
    {
        if (OnLastLine()) return;

        List<ColorChar> temp = curr_line;
        curr_line = lines[curr_line_index + 1];
        lines[curr_line_index + 1] = temp;

        curr_line_index++;
        Set_unsaved_changes();
    }

    private Markup GetDisplayMarkup()
    {
        StringBuilder s = new(capacity: BUFFER_HEIGHT * BUFFER_WIDTH * 2);
        for (int i = 0; i < this.lines.Count; i++)
        {
            string line_number = (i + 1).ToString("D2");

            // Space to make the line number stick to the right border
            s.Append($"[yellow]{line_number} | [/]");

            // this.lines[i] is the line
            for (int j = 0; j < this.lines[i].Count; j++)
            {
                ColorChar color_char = this.lines[i][j];
                string _char = HandleSquareBrackets(color_char.Char);

                if (color_char.IsHighlighted())
                {
                    if (color_char.Color == null)
                        s.Append($"[black on yellow]{_char}[/]");
                    else
                        s.Append($"[{color_char.Color.Value} on yellow]{_char}[/]");
                }
                else if (color_char.Color == null)
                {
                    s.Append(_char);
                }
                else
                {
                    s.Append($"[{color_char.Color.Value}]{_char}[/]");
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

    /// <summary>
    /// Used to keep track of the color for a single char with ctrl+shift+(b|u|i)
    /// </summary>
    bool colorActiveForOneChar = false;

    /// <summary>
    /// Toggle the primary color
    /// </summary>
    public void TogglePrimaryColor()
    {
        secondary_color_on = false;
        primary_color_on = !primary_color_on;
        tertiary_color_on = false;
    }

    /// <summary>
    /// Set the primary color for one char only
    /// </summary>
    public void SetPrimaryColorForOneChar()
    {
        primary_color_on = true;
        secondary_color_on = false;
        tertiary_color_on = false;
        colorActiveForOneChar = true;
    }

    /// <summary>
    /// Toggle the secondary color
    /// </summary>
    public void ToggleSecondaryColor()
    {
        primary_color_on = false;
        secondary_color_on = !secondary_color_on;
        tertiary_color_on = false;
    }

    public void SetSecondaryColorForOneChar()
    {
        primary_color_on = false;
        secondary_color_on = true;
        tertiary_color_on = false;
        colorActiveForOneChar = true;
    }

    /// <summary>
    /// Toggle the tertiary color
    /// </summary>
    public void ToggleTertiaryColor()
    {
        primary_color_on = false;
        secondary_color_on = false;
        tertiary_color_on = !tertiary_color_on;
    }

    /// <summary>
    /// Set the tertiary color for one char only
    /// </summary>
    public void SetTertiaryColorForOneChar()
    {
        primary_color_on = false;
        secondary_color_on = false;
        tertiary_color_on = true;
        colorActiveForOneChar = true;
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
        return (curr_line_index, curr_char_index);
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
            curr_line_index = lines.Count - 1;
        else
            curr_line_index = line;

        // Go to end of line
        curr_char_index = curr_line.Count;
    }

    private string HandleSquareBrackets(char c)
    {
        if (c == '[') return "[[";
        if (c == ']') return "]]";
        return c.ToString();
    }

    public void InsertTab()
    {
        // Insert tab normally

        int size = 4;
        bool tab_fits = curr_line.Count <= BUFFER_WIDTH - size;

        // Check early return conditions
        if (insertModeOn)
        {
            if (!tab_fits) return;
        }
        else
        {
            if (!tab_fits && curr_char_index > BUFFER_WIDTH - size) return;
        }

        curr_line.InsertRange(curr_char_index, new ColorChar[] { SPACE, SPACE, SPACE, SPACE });
        curr_char_index += size;
        Set_unsaved_changes();
    }

    /// <summary>
    /// Remove a tab from the beginning of the current line if possible
    /// </summary>
    public void Unindent()
    {
        if (curr_line.Count >= 4 && curr_line[0] == ' ' && curr_line[1] == ' ' && curr_line[2] == ' ' && curr_line[3] == ' ')
        {
            curr_line.RemoveRange(0, 4);
            curr_char_index = Math.Max(0, curr_char_index - 4);
            Set_unsaved_changes();
        }
    }

    private static Dictionary<ConsoleKey, string> tree_ctrl_functions = new()
    {
        { ConsoleKey.Q, "Quit NoteWorthy" },
        { ConsoleKey.O, "Open current directory in file explorer" },
        { ConsoleKey.UpArrow, "Preview the previous note" },
        { ConsoleKey.DownArrow, "Preview the next note" },
        { ConsoleKey.W, "Close the editor" },
        { ConsoleKey.N, "Create new note" },
        { ConsoleKey.M, "Create new folder" },
        { ConsoleKey.R, "Reload the tree" },
        { ConsoleKey.H, "Toggle this help panel (press to go back)" },
        { ConsoleKey.D, "Delete the selected tree item" },
        { ConsoleKey.D8, "Open the settings file (+shift to reload it)" },
        { ConsoleKey.D1, "Toggle tree visibility" },
    };

    private static Dictionary<ConsoleKey, string> tree_regular_functions = new()
    {
        { ConsoleKey.UpArrow, "Move selection up" },
        { ConsoleKey.DownArrow, "Move selection down" },
        { ConsoleKey.Home, "Move selection to top tree" },
        { ConsoleKey.End, "Move selection to bottom tree" },
        { ConsoleKey.Escape, "Go to parent dir" },
        { ConsoleKey.Enter, "Open the selected note" },
        { ConsoleKey.Spacebar, "Preview the selected note" },
        { ConsoleKey.Tab, "Switch focus to the editor" },
        { ConsoleKey.H, "Toggle this help panel (press to go back)" },
        { ConsoleKey.F2, "Rename the selected item" },
        { ConsoleKey.N, "Create new note" },
        { ConsoleKey.M, "Create new folder" },
        { ConsoleKey.Delete, "Delete selected tree item" },
        { ConsoleKey.Backspace, "Delete selected tree item" }
    };

    private static Dictionary<ConsoleKey, string> editor_ctrl_functions = new()
    {
        { ConsoleKey.Q, "Quit NoteWorthy" },
        { ConsoleKey.L, "Print dashed-line (if line empty)" },
        { ConsoleKey.S, "Save note" },
        { ConsoleKey.R, "Reload note" },
        { ConsoleKey.D, "Delete current line" },
        { ConsoleKey.W, "Close editor" },
        { ConsoleKey.Z, "Undo" },
        { ConsoleKey.Y, "Redo" },
        { ConsoleKey.V, "Might not work as expected. Use: Alt+V instead" },
        { ConsoleKey.H, "Toggle this help panel (press to go back)" },
        { ConsoleKey.End, "Move to end of note" },
        { ConsoleKey.Home, "Move to start of note" },
        { ConsoleKey.LeftArrow, "Move to prev word (+shift to select)" },
        { ConsoleKey.RightArrow, "Move to next word (+shift to select)" },
        { ConsoleKey.C, "Copy selected text" },
        { ConsoleKey.A, "Select all text" },
        { ConsoleKey.K, "Toggle insert mode" },
        { ConsoleKey.Backspace, "Delete word" },
        { ConsoleKey.Delete, "Delete word (to the right of cursor)" },
        { ConsoleKey.N, "Create new note" },
        { ConsoleKey.M, "Create new folder" },
        { ConsoleKey.O, "Open current directory in file explorer" },
        { ConsoleKey.D8, "Open the settings file (+shift to reload it)" },
        { ConsoleKey.UpArrow, "Preview the previous note" },
        { ConsoleKey.DownArrow, "Preview the next note" },
        { ConsoleKey.B, "Toggle primary color (+shift for solo char)" },
        { ConsoleKey.U, "Toggle secondary color (+shift for solo char)" },
        { ConsoleKey.I, "Toggle tertiary color (+shift for solo char)" },
        { ConsoleKey.D1, "Toggle tree visibility" },
        { ConsoleKey.G, "Go to line" },
        { ConsoleKey.Spacebar, "Set all colors to off" },
        { ConsoleKey.J, "Open AI window" }
    };

    private static Dictionary<ConsoleKey, string> editor_regular_functions = new()
    {
        { ConsoleKey.Escape, "Focus tree / clear selected text" },
        { ConsoleKey.Enter, "Insert new line" },
        { ConsoleKey.UpArrow, "Move cursor up (+shift to move line)" },
        { ConsoleKey.DownArrow, "Move cursor down (+shift to move line)" },
        { ConsoleKey.LeftArrow, "Move cursor left (+shift to select)" },
        { ConsoleKey.RightArrow, "Nove cursor right (+shift to select)" },
        { ConsoleKey.End, "Move to end of line (+shift to select)" },
        { ConsoleKey.Home, "Move to start of line (+shift to select)" },
        { ConsoleKey.Insert, "Toggle insert mode" },
        { ConsoleKey.Backspace, "Delete character" },
        { ConsoleKey.Delete, "Delete character (to the right)" },
        { ConsoleKey.F2, "Rename note" },
        { ConsoleKey.Tab, "Insert tab (+shift to unindent)" }
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
                string color = kv.Key == lastKeyPressInfo.Key || kv.Key == ConsoleKey.H ? "aqua" : "yellow";
                return acc + $"[{color}]{ctrlString}{kv.Key}[/] - {kv.Value}\n";
            })
        ).Expand().RoundedBorder().Header("[aqua]" + panelHeader + "[/]").BorderColor(Spectre.Console.Color.Aqua);
    }

    public void InsertDashedLine()
    {
        // Line must be completely empty to insert the dashed line, as Ctrl+L is used for subtitles and must take up the whole line
        if (!LineIsEmpty()) return;

        // Check for possible subtitle
        if (Settings.AutoFormatSubtitles && curr_line_index >= 1 && lines[curr_line_index - 1].Count != 0)
        {
            List<ColorChar> subtitle_line = lines[curr_line_index - 1];
            int spaces_to_add = (BUFFER_WIDTH - subtitle_line.Count) / 2;
            subtitle_line.InsertRange(0, Enumerable.Repeat(SPACE, spaces_to_add));
            for (int i = 0; i < subtitle_line.Count; i++)
            {
                if (subtitle_line[i].Color == null)
                {
                    subtitle_line[i] = new ColorChar((byte)subtitle_line[i].Char, Settings.PrimaryColor);
                }
            }
        }

        ColorChar dash = new ColorChar((byte)'-', 0);
        curr_line.AddRange(Enumerable.Repeat(dash, BUFFER_WIDTH));
        curr_char_index = BUFFER_WIDTH;
        
        // Move cursor to the next line if possible
        if (curr_line_index < BUFFER_HEIGHT - 1)
        {
            if (curr_line_index < lines.Count - 1)
            {
                curr_line_index++;
            }
            else
            {
                lines.Add(new List<ColorChar>());
                curr_line_index++;
            }

            curr_char_index = 0;
        }

        Set_unsaved_changes();
    }

    /// <summary>
    /// Set all the colors (primary, secondary, tertiary) to off
    /// </summary>
    public void SetAllColorsToOff()
    {
        primary_color_on = false;
        secondary_color_on = false;
        tertiary_color_on = false;
    }

    #region Highlighting (selecting text)

    public void HighlightNextWord()
    {
        if (AtEndOfLine())
        {
            if (OnLastLine()) return;
            curr_line_index++;
            curr_char_index = 0;
        }
        else
        {
            int index = FindIndexOf_EndOfNextWord();
            
            // Edge case for "blah blah {" where the curly brace wasn't getting picked up when the cursor was at that last space right before
            // the brace due to FindIndexOf_EndOfNextWord() returning the same position
            //if (index == curr_char_index && curr_char_index == GetCharsInLine() - 1)
            //{
            //    curr_line[GetCharsInLine() - 1].ToggleHighlighting();
            //    if (OnLastLine()) return;
            //    curr_line_index++;
            //    curr_char_index = 0;
            //    return;
            //}

            // Toggle highlighting
            for (int i = curr_char_index; i < index; i++)
            {
                curr_line[i].ToggleHighlighting();
            }
            curr_char_index = index;
        }
    }

    public void HighlightPreviousWord()
    {
        if (AtBeginningOfLine())
        {
            if (OnFirstLine()) return;
            curr_line_index--;
            curr_char_index = GetCharsInLine();
        }
        else
        {
            int index = FindIndexOf_StartOfPreviousWord();
            for (int i = index; i < curr_char_index; i++)
            {
                curr_line[i].ToggleHighlighting();
            }
            curr_char_index = index;
        }
    }

    /// <summary>
    /// Returns true if at least one character is highlighted
    /// </summary>
    public bool SomeCharsAreHighlighted()
    {
        for (int i = 0; i < lines.Count; i++)
        {
            for (int j = 0; j < lines[i].Count; j++)
            {
                if (lines[i][j].IsHighlighted()) return true;
            }
        }

        return false;
    }

    public void UnhighlightAllChars()
    {
        for (int i = 0; i < lines.Count; i++)
        {
            for (int j = 0; j < lines[i].Count; j++)
            {
                if (lines[i][j].IsHighlighted()) lines[i][j].ToggleHighlighting();
            }
        }
    }

    [STAThread]
    public void CopyHighlightedText()
    {
        StringBuilder highlighted_text = new(capacity: BUFFER_HEIGHT * BUFFER_WIDTH);

        for (int i = 0; i < lines.Count; i++)
        {
            bool line_had_at_least_one_highlighted_char = false;
            for (int j = 0; j < lines[i].Count; j++)
            {
                if (lines[i][j].IsHighlighted())
                {
                    highlighted_text.Append(lines[i][j].Char);
                    lines[i][j].ToggleHighlighting();
                    line_had_at_least_one_highlighted_char = true;
                }
            }

            if (line_had_at_least_one_highlighted_char || lines[i].Count == 0) highlighted_text.Append('\n');
        }

        ClipboardService.SetText(highlighted_text.ToString().Trim());
    }

    public void HighlightWholeNote()
    {
        for (int i = 0; i < lines.Count; i++)
        {
            for (int j = 0; j < lines[i].Count; j++)
            {
                if (!lines[i][j].IsHighlighted()) lines[i][j].ToggleHighlighting();
            }
        }

        curr_line_index = lines.Count - 1;
        curr_char_index = curr_line.Count;
    }

    public void HighlightPreviousChar()
    {
        if (curr_char_index == 0)
        {
            if (OnFirstLine()) return;
            curr_line_index--;
            curr_char_index = GetCharsInLine();
        }
        else
        {
            curr_line[curr_char_index - 1].ToggleHighlighting();
            curr_char_index--;
        }
    }

    public void HighlightNextChar()
    {
        if (curr_char_index == GetCharsInLine())
        {
            if (OnLastLine()) return;
            curr_line_index++;
            curr_char_index = 0;
        }
        else
        {
            curr_line[curr_char_index].ToggleHighlighting();
            curr_char_index++;
        }
    }

    public void HighlightToEndOfLine()
    {
        for (int i = curr_char_index; i < curr_line.Count; i++)
        {
            curr_line[i].ToggleHighlighting();
        }

        curr_char_index = curr_line.Count;
    }

    public void HighlightToStartOfLine()
    {
        for (int i = 0; i < curr_char_index; i++)
        {
            curr_line[i].ToggleHighlighting();
        }

        curr_char_index = 0;
    }

    private void DeleteHighlightedChars()
    {
        (int line, int col)? first_highlighted_char = GetFirstHighlightedCharPos();
        (int line, int col)? last_highlighted_char = GetLastHighlightedCharPos();
        if (first_highlighted_char == null || last_highlighted_char == null) return;

        for (int i = first_highlighted_char.Value.line; i < last_highlighted_char.Value.line; i++)
        {
            for (int j = (i == first_highlighted_char.Value.line) ? first_highlighted_char.Value.col : 0; j < lines[i].Count; j++)
            {
                if (lines[i][j].IsHighlighted())
                {
                    lines[i].RemoveAt(j);
                    j--;
                }
            }

            if (lines[i].Count == 0)
            {
                if (i == lines.Count - 1) break;
                lines.RemoveAt(i);
                i--;
            }
        }

        curr_line_index = first_highlighted_char.Value.line;
        curr_char_index = first_highlighted_char.Value.col;
    }

    /// <summary>
    /// Get the position of the first highlighted character in the note lines[i][j] where (i, j) is returned
    /// </summary>
    private (int, int)? GetFirstHighlightedCharPos()
    {
        for (int i = 0; i < lines.Count; i++)
        {
            for (int j = 0; j < lines[i].Count; j++)
            {
                if (lines[i][j].IsHighlighted()) return (i, j);
            }
        }

        return null;
    }

    /// <summary>
    /// Get the position of the last highlighted character ion the note lines[i][j] where (i, j) is returned
    /// </summary>
    /// <returns></returns>
    private (int, int)? GetLastHighlightedCharPos()
    {
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            for (int j = lines[i].Count - 1; j >= 0; j--)
            {
                if (lines[i][j].IsHighlighted()) return (i, j);
            }
        }
        return null;
    }

    /// <summary>
    /// Change the color of every highlighted character to the given <paramref name="color"/>
    /// </summary>
    /// <param name="color">Color value from either Settings.PrimaryColor, Settings.SecondaryColor, or Settings.TertiaryColor</param>
    public void ColorHighlightedChars(byte color)
    {
        (int y, int x) last_highlighted_char = (0, 0);
        for (int i = 0; i < lines.Count; i++)
        {
            for (int j = 0; j < lines[i].Count; j++)
            {
                // Sets the char to the given `color` and un-highlights it
                if (lines[i][j].IsHighlighted())
                {
                    lines[i][j] = new ColorChar((byte)lines[i][j].Char, color, is_highlighted: false);
                    last_highlighted_char = (i, j);
                }
            }
        }

        // Go to end of what was previously selected
        curr_line_index = last_highlighted_char.y;
        curr_char_index = last_highlighted_char.x + 1;

        Set_unsaved_changes();
    }

    /// <summary>
    /// Make every highlighted char default color. Used when the selection is all one color and the user presses Ctrl+Shift+(B|U|I)
    /// </summary>
    public void RemoveColorFromHighlightedChars()
    {
        (int y, int x) last_highlighted_char = (0, 0);
        for (int i = 0; i < lines.Count; i++)
        {
            for (int j = 0; j < lines[i].Count; j++)
            {
                // Sets the char back to the default color and un-highlights it
                if (lines[i][j].IsHighlighted())
                {
                    lines[i][j] = new ColorChar((byte)lines[i][j].Char, 0, is_highlighted: false);
                    last_highlighted_char = (i, j);
                }
            }
        }

        // Go to end of what was previously selected
        curr_line_index = last_highlighted_char.y;
        curr_char_index = last_highlighted_char.x + 1;

        Set_unsaved_changes();
    }

    /// <summary>
    /// Returns true if every selected character is painted with the given <paramref name="color"/>. Used for checking if Ctrl+Shift+(B|U|I) should highlight or un-highlight the chars
    /// </summary>
    /// <param name="color">Color value from either Settings.PrimaryColor, Settings.SecondaryColor, or Settings.TertiaryColor</param>
    public bool SelectionIsColor(byte color)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            for (int j = 0; j < lines[i].Count; j++)
            {
                if (!lines[i][j].IsHighlighted()) continue;

                if (lines[i][j].GetBytes().color_byte != color) return false;
            }
        }

        return true;
    }

    #endregion
}
