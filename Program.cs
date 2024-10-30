using Spectre.Console;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NoteWorthy;

class Program
{
    public static Spectre.Console.Style DefaultStyle = new Style(Color.White, Color.Grey46);
    public static string NOTES_DIR_PATH = Directory.GetCurrentDirectory() + "\\notes";

    private static NoteTree noteTree = new NoteTree();
    private static NoteEditor noteEditor = new NoteEditor(null);
    private static Layout display_layout = new Layout()
        .SplitColumns(
            new Layout("NoteTree").Size(NoteTree.DISPLAY_WIDTH),
            new Layout("NoteEditor")
        );
    /// <summary>
    /// Set to true when a new note is loaded (and therefore a new NoteEditor is created) in order to render the new note.
    /// </summary>
    private static bool noteEditorRequiresUpdate = true;
    /// <summary>
    /// Set to true when the editor (the right side) is focused. Set to false when the tree (the left side) is focused.
    /// </summary>
    private static bool editorFocused = false;

    // For allowing ctrl+s as a shortcut
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);
    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, int dwMode);

    public static void Main()
    {
        Allow_CtrlS_Shortcut();
        SetBlockCursor();
        InitializeNotesDirectory();
        Mainloop();
    }

    private static void SetBlockCursor()
    {
        Console.Write("\u001b[2 q");
        Console.SetCursorPosition(0, 0);
    }

    private static void Allow_CtrlS_Shortcut()
    {
        IntPtr consoleHandle = GetStdHandle(-10);
        SetConsoleMode(consoleHandle, 0x0001);
    }

    /// <summary>
    /// Create the notes directory if it doesn't exist.
    /// The notes directory is where all the saved notes go.
    /// </summary>
    private static void InitializeNotesDirectory()
    {
        if (!Directory.Exists(NOTES_DIR_PATH))
        {
            Directory.CreateDirectory(NOTES_DIR_PATH);
        }
    }

    private static void Mainloop()
    {
        while (true)
        {
            HandleRendering();

            if (!Console.KeyAvailable) continue;
            ConsoleKeyInfo keyInfo = Console.ReadKey(true);

            if (editorFocused)
            {
                HandleEditorInput(keyInfo);
            }
            else
            {
                HandleTreeInput(keyInfo);
            }
        }
    }

    /// <summary>
    /// Rerender the screen if it is necessary
    /// </summary>
    private static void HandleRendering()
    {
        bool note_tree_requires_update = noteTree.Get_RequiresUpdate();
        if (note_tree_requires_update)
        {
            var panel = noteTree.GenerateDisplayPanel();

            // The tree must be focused if the editor isn't
            if (!editorFocused)
            {
                panel.BorderColor(Color.Aqua);
            }

            // Will only rewrite if there's been a change to them
            display_layout.GetLayout("NoteTree").Update(panel);
        }

        if (noteEditorRequiresUpdate)
        {
            var panel = noteEditor.GenerateDisplayPanel();

            if (editorFocused)
            {
                panel.BorderColor(Color.Aqua);
            }

            display_layout.GetLayout("NoteEditor").Update(panel);
        }

        if (note_tree_requires_update || noteEditorRequiresUpdate)
        {
            noteEditorRequiresUpdate = false;
            AnsiConsole.Cursor.Hide();
            Console.SetCursorPosition(0, 0);
            AnsiConsole.Write(display_layout);

            if (editorFocused)
            {
                AnsiConsole.Cursor.Show();
                noteEditor.UpdateCursorPosInEditor();
            }
        }
    }

    private static void HandleTreeInput(ConsoleKeyInfo keyInfo)
    {
        // For shortcuts with the control key
        if (keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            switch (keyInfo.Key)
            {
                // Ctrl+L - toggle focus to the editor
                case ConsoleKey.L:
                    editorFocused = true;
                    AnsiConsole.Cursor.Show();
                    noteEditorRequiresUpdate = true;
                    noteTree.Set_RequiresUpdate();
                    break;

                // Ctrl+O - open the currently selected dir in the file explorer
                case ConsoleKey.O:
                    string path = noteTree.GetSelectedTreeItem().Parent == null ? NOTES_DIR_PATH : noteTree.GetSelectedTreeItem().Parent!.FilePath;
                    Process.Start("explorer.exe", path);
                    break;

                // Ctrl + Up Arrow - Go to top of tree
                case ConsoleKey.UpArrow:
                case ConsoleKey.PageUp:
                    noteTree.MoveSelectionToTop();
                    break;

                // Ctrl + Down Arrow - Go to bottom of tree
                case ConsoleKey.DownArrow:
                case ConsoleKey.PageDown:
                    noteTree.MoveSelectionToBottom();
                    break;
            }
        }
        // For functionality with regular keypresses
        else
        {
            TreeItem selected_tree_item = noteTree.GetSelectedTreeItem();
            switch (keyInfo.Key)
            {
                // Up Arrow - move selection up
                case ConsoleKey.UpArrow:
                case ConsoleKey.PageUp:
                    noteTree.MoveSelectionUp();
                    break;

                // Down Arrow - move selection down
                case ConsoleKey.DownArrow:
                case ConsoleKey.PageDown:
                    noteTree.MoveSelectionDown();
                    break;

                // Home - move selection to top
                case ConsoleKey.Home:
                    noteTree.MoveSelectionToTop();
                    break;

                // End - move selection to bottom
                case ConsoleKey.End:
                    noteTree.MoveSelectionToBottom();
                    break;

                // Escape | B - Go to parent directory (go back)
                case ConsoleKey.Escape:
                case ConsoleKey.B:
                    noteTree.GoToParentIfPossible();
                    break;

                // Enter | Space - open the note in the editor
                case ConsoleKey.Enter:
                case ConsoleKey.Spacebar:
                    if (selected_tree_item.IsDir)
                    {
                        noteTree.NavigateToTreeItem(selected_tree_item);
                    }
                    else
                    {
                        noteEditor = new NoteEditor(selected_tree_item.FilePath);
                        editorFocused = true;
                        AnsiConsole.Cursor.Show();
                        noteEditorRequiresUpdate = true;
                        noteTree.Set_RequiresUpdate();
                    }

                    noteEditorRequiresUpdate = true;
                    break;

                // F2 - rename the selected item
                case ConsoleKey.F2:
                    RenameSelectedTreeItem();
                    noteTree.Set_RequiresUpdate();
                    noteEditorRequiresUpdate = true;
                    break;
            }
        }
    }

    private static void HandleEditorInput(ConsoleKeyInfo keyInfo)
    {
        // For shortcuts with the control key
        if (keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            switch (keyInfo.Key)
            {
                // Ctrl+L - toggle focus to the tree
                case ConsoleKey.L:
                    editorFocused = false;
                    AnsiConsole.Cursor.Hide();
                    noteEditorRequiresUpdate = true;
                    noteTree.Set_RequiresUpdate();
                    break;

                // Ctrl+S - save note
                case ConsoleKey.S:
                    noteEditor.Save();
                    noteEditorRequiresUpdate = true;
                    break;

                // Ctrl+R - reload note
                case ConsoleKey.R:
                    // Ask if they want a reload while there are unsaved changes
                    if (noteEditor.HasUnsavedChanges())
                    {
                        bool save_changes = AskToSaveUnsavedChanges("Reloading note... but first:");
                        if (save_changes) noteEditor.Save();
                    }

                    // Can't reload if there's no note open
                    string? path = noteEditor.GetNotePath();
                    if (path == null) break;
                    
                    // Reload the note
                    noteEditor = new NoteEditor(path);
                    noteEditorRequiresUpdate = true;
                    break;

                // Ctrl+D - delete line
                case ConsoleKey.D:
                    noteEditor.DeleteLine();
                    noteEditorRequiresUpdate = true;
                    break;

                // Ctrl+W - Close currently displayed note
                case ConsoleKey.W:
                    if (noteEditor.HasUnsavedChanges())
                    {
                        bool save_changes = AskToSaveUnsavedChanges("Closing note... but first:");
                        if (save_changes) noteEditor.Save();
                    }

                    noteEditor = new NoteEditor(null);
                    noteEditorRequiresUpdate = true;
                    // Move focus to the tree
                    editorFocused = false;
                    noteTree.Set_RequiresUpdate();
                    break;

                // Ctrl+Z - Undo
                case ConsoleKey.Z:
                    noteEditor.Undo();
                    noteEditorRequiresUpdate = true;
                    break;

                // Ctrl+Y - Redo
                case ConsoleKey.Y:
                    noteEditor.Redo();
                    noteEditorRequiresUpdate = true;
                    break;

                // Ctrl+End | Ctrl+DownArrow - Navigate to end of file
                case ConsoleKey.End:
                case ConsoleKey.DownArrow:
                case ConsoleKey.PageDown:
                    noteEditor.MoveCursorToEndOfEditor();
                    noteEditorRequiresUpdate = true;
                    break;

                // Ctrl+Home | Ctrl+UpArrow | Navigate to start of file
                case ConsoleKey.Home:
                case ConsoleKey.UpArrow:
                case ConsoleKey.PageUp:
                    noteEditor.MoveCursorToStartOfEditor();
                    noteEditorRequiresUpdate = true;
                    break;

                // Ctrl+RightArrow - Navigate one word to the right
                case ConsoleKey.RightArrow:
                    noteEditor.NavigateToNextWord();
                    noteEditorRequiresUpdate = true;
                    break;

                // Ctrl+LeftArrow - Navigate one word to the left
                case ConsoleKey.LeftArrow:
                    noteEditor.NavigateToPreviousWord();
                    noteEditorRequiresUpdate = true;
                    break;

                // Ctrl+I - Toggle insert mode
                case ConsoleKey.I:
                    noteEditor.ToggleInsertMode();
                    break;

                // Ctrl+Backspace - Delete word with backspace
                case ConsoleKey.Backspace:
                    noteEditor.DeleteWordWithBackspace();
                    noteEditorRequiresUpdate = true;
                    break;

                // Ctrl+Delete - delete word with delete key
                case ConsoleKey.Delete:
                    noteEditor.DeleteWordWithDeleteKey();
                    noteEditorRequiresUpdate = true;
                    break;

            }
        }
        // For functionality with regular keypresses
        else
        {
            switch (keyInfo.Key)
            {
                // Unfocus the editor, therefore focusing the tree
                case ConsoleKey.Escape:
                    editorFocused = false;
                    AnsiConsole.Cursor.Hide();
                    noteEditorRequiresUpdate = true;
                    noteTree.Set_RequiresUpdate();
                    break;

                // Enter - add new line at current position
                case ConsoleKey.Enter:
                    noteEditor.InsertLine();
                    noteEditorRequiresUpdate = true;
                    break;

                // Navigate the cursor up in the editor
                case ConsoleKey.UpArrow:
                    noteEditor.MoveCursorUp();
                    noteEditorRequiresUpdate = true;
                    break;

                // Navigate the cursor down in the editor
                case ConsoleKey.DownArrow:
                    noteEditor.MoveCursorDown();
                    noteEditorRequiresUpdate = true;
                    break;

                // Navigate the cursor left in the editor
                case ConsoleKey.LeftArrow:
                    noteEditor.MoveCursorLeft();
                    noteEditorRequiresUpdate = true;
                    break;

                // Navigate the cursor right in the editor
                case ConsoleKey.RightArrow:
                    noteEditor.MoveCursorRight();
                    noteEditorRequiresUpdate = true;
                    break;

                // Navigate to end of line
                case ConsoleKey.End:
                    noteEditor.MoveCursorToEndOfLine();
                    noteEditorRequiresUpdate = true;
                    break;

                // Navigate to start of line
                case ConsoleKey.Home:
                    noteEditor.MoveCursorToStartOfLine();
                    noteEditorRequiresUpdate = true;
                    break;

                // Insert - toggle insert mode
                case ConsoleKey.Insert:
                    noteEditor.ToggleInsertMode();
                    break;

                // Backspace - Delete char at cursor position with backspace
                case ConsoleKey.Backspace:
                    noteEditor.DeleteCharWithBackspace();
                    noteEditorRequiresUpdate = true;
                    break;

                // Delete - Delete char at cursor position with delete key
                case ConsoleKey.Delete:
                    noteEditor.DeleteCharWithDeleteKey();
                    noteEditorRequiresUpdate = true;
                    break;

                // F2 - rename the current note
                case ConsoleKey.F2:
                    RenameSelectedTreeItem();
                    noteTree.Set_RequiresUpdate();
                    noteEditorRequiresUpdate = true;
                    // focus the tree in case user wants to keep renaming stuff
                    editorFocused = false;
                    break;

                // Print char to the editor if it is a non-control char
                default:
                    // Only print if the key is a non-control char
                    if (!char.IsControl(keyInfo.KeyChar))
                    {
                        noteEditor.InsertChar(keyInfo.KeyChar);
                        noteEditorRequiresUpdate = true;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Rename the tree item that is currently selected, whether it's a dir or a file.
    /// </summary>
    private static void RenameSelectedTreeItem()
    {
        AnsiConsole.Cursor.Show();
        TreeItem selected_item = noteTree.GetSelectedTreeItem();
        Console.Clear();

        string new_name = AnsiConsole.Prompt(new TextPrompt<string>($"Rename {selected_item.Name}: ")).Trim();
        if (new_name == null || new_name?.Length == 0)
        {
            return;
        }

        if (selected_item.IsDir)
        {
            // Rename in file system
            Directory.Move(selected_item.FilePath, Path.Combine(Path.GetDirectoryName(selected_item.FilePath)!, new_name));
        }
        else
        {
            if (!new_name.EndsWith(".nw")) new_name += ".nw";
            // Rename in file system
            File.Move(selected_item.FilePath, Path.Combine(Path.GetDirectoryName(selected_item.FilePath)!, new_name));
        }

        // Remake tree in order to reflect changes
        // This was required because the GetSelectedTreeItem was returning a deep copy, not a reference
        // Therefore, the changes weren't being reflected in the tree, and it was not possible to get a deep copy to how GetSelectedTreeItem works.
        // Do not try to refact this.
        noteTree = new NoteTree();
        noteEditor = new NoteEditor(null);
        noteEditorRequiresUpdate = true;
        editorFocused = false; // Focus the tree in case user wants to keep renaming stuff
        AnsiConsole.Cursor.Hide();
    }

    /// <summary>
    /// Ask the user if they would like to save their unsaved note (only call if the note has unsaved changes)
    /// </summary>
    /// <returns>True if the user wants to save the note, false if they don't</returns></returns>
    /// <param name="msg">Message to display at the beginning of the string.
    /// Ex: "Reload Note", to let them know what's gonna happen after they deal with the message</param>
    private static bool AskToSaveUnsavedChanges(string msg)
    {
        AnsiConsole.Cursor.Hide();
        Console.Clear();
        AnsiConsole.Write(
            new Text($"{msg}\n\nYou have unsaved changes.\nPress 'Y' to save the note.\nPress 'N' to discard changes.")
        );

        while (true)
        {
            ConsoleKeyInfo keyInfo = Console.ReadKey(true);
            noteEditorRequiresUpdate = true;
            noteTree.Set_RequiresUpdate();

            if (keyInfo.Key == ConsoleKey.Y)
            {
                AnsiConsole.Cursor.Show();
                return true;
            }
            else if (keyInfo.Key == ConsoleKey.N)
            {
                AnsiConsole.Cursor.Show();
                return false;
            }
        }
    }
}