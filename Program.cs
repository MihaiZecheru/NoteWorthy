using Spectre.Console;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NoteWorthy;

class Program
{
    public static Spectre.Console.Style DefaultStyle = new Style(Color.White, Color.Grey46);
    public static string NOTES_DIR_PATH = Path.Combine(Directory.GetCurrentDirectory(), "notes");

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
        Allow_CtrlC_AsInput();
        Allow_CtrlS_Shortcut();
        SetBlockCursor();
        InitializeNotesDirectory();
        StartResizeEventListener();
        Mainloop();
    }

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    private static void Allow_CtrlC_AsInput()
    {
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;

            // If the note tree is focused, ctrl+c will copy the selected file            
            if (!editorFocused)
            {
                bool success = noteTree.CopySelectedFile();
                if (!success) return;
                // Send Ctrl+R to refresh the tree in the main thread
                keybd_event(0x11, 0, 0x0000, UIntPtr.Zero);
                keybd_event(0x52, 0, 0x0000, UIntPtr.Zero);
                keybd_event(0x52, 0, 0x0002, UIntPtr.Zero);
                keybd_event(0x11, 0, 0x0002, UIntPtr.Zero);
            }
        };
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

    private static void StartResizeEventListener()
    {
        Task.Run(() =>
        {
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;

            while (true)
            {
                if (width != Console.WindowWidth || height != Console.WindowHeight)
                {
                    width = Console.WindowWidth;
                    height = Console.WindowHeight;
                    noteTree.Set_RequiresUpdate();
                    noteEditor.UpdateBuffers();
                    noteEditorRequiresUpdate = true;
                }
            }
        });
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

            if (noteTree.IsVisible())
            {
                display_layout.GetLayout("NoteTree").Visible();
            }
            else
            {
                display_layout.GetLayout("NoteTree").Invisible();
            }
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
            AnsiConsole.Cursor.Hide();
            noteEditorRequiresUpdate = false;
            Console.SetCursorPosition(0, 0);
            AnsiConsole.Write(display_layout);

            if (editorFocused)
            {
                noteEditor.UpdateCursorPosInEditor();
                AnsiConsole.Cursor.Show();
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
                // Ctrl+Q - Quit \ close application
                case ConsoleKey.Q:
                    Environment.Exit(0);
                    break;

                // Ctrl+L - Toggle focus to the editor
                case ConsoleKey.L:
                    if (noteEditor.GetNotePath() == null) break;
                    if (noteEditor.IsTypingDisabled()) break;
                    editorFocused = true;
                    AnsiConsole.Cursor.Show();
                    Set_NoteEditorRequiresUpdate();
                    noteTree.Set_RequiresUpdate();
                    break;

                // Ctrl+O - Open the currently selected dir in the file explorer
                case ConsoleKey.O:
                    string path = noteTree.GetSelectedTreeItem()?.Parent == null ? NOTES_DIR_PATH : noteTree.GetSelectedTreeItem()!.Parent!.FilePath;
                    Process.Start("explorer.exe", path);
                    break;

                // Ctrl + Up Arrow - Move up one, just like normal Up Arrow
                case ConsoleKey.UpArrow:
                case ConsoleKey.PageUp:
                    noteTree.MoveSelectionUp();
                    break;

                // Ctrl + Down Arrow - Move down one, just like normal Down Arrow
                case ConsoleKey.DownArrow:
                case ConsoleKey.PageDown:
                    noteTree.MoveSelectionDown();
                    break;

                // Ctrl+W - Close the note in the editor if there's a note, otherwise close the app
                case ConsoleKey.W:
                    if (noteEditor.GetNotePath() == null)
                    {
                        // If the user presses Ctrl+W when there is no note, close the app
                        Environment.Exit(0);
                    }
                    else
                    {
                        noteEditor = new NoteEditor(null);
                        noteTree.SetVisible();
                        Set_NoteEditorRequiresUpdate();
                        // Move focus to the tree
                        editorFocused = false;
                        noteTree.Set_RequiresUpdate();
                    }
                    break;

                // Ctrl+N - Create new file
                case ConsoleKey.N:
                    HandleCreateNewFile();
                    break;

                // Ctrl+M - Create new folder
                case ConsoleKey.M:
                    HandleCreateNewFolder();
                    break;

                // Ctrl+R - Reload the tree
                case ConsoleKey.R:
                    noteTree = new NoteTree();
                    noteEditor = new NoteEditor(null);
                    noteTree.SetVisible();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Ctrl+D - Delete the selected tree item
                case ConsoleKey.D:
                    HandleDeleteTreeItem();
                    break;

                // Ctrl+8 - Open the settings file
                case ConsoleKey.D8:
                    if (!Settings.SettingsFileExists())
                    {
                        Settings.CreateDefaultSettingsFile();
                    }
                    Settings.OpenSettingsFile();
                    break;

                // Ctrl+1 - Toggle the noteTree widget
                case ConsoleKey.D1:
                    if (noteEditor.GetNotePath() == null) break;
                    noteTree.ToggleVisibility();
                    break;
            }
        }
        // For functionality with regular keypresses
        else
        {
            TreeItem? selected_tree_item = noteTree.GetSelectedTreeItem();
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

                // Enter | Space - open the note in the editor.
                // If space is pressed, keep the tree focused. If enter is pressed, focus the editor.
                case ConsoleKey.Enter:
                case ConsoleKey.Spacebar:
                    if (selected_tree_item == null) break;

                    if (selected_tree_item.IsDir)
                    {
                        noteTree.NavigateToTreeItem(selected_tree_item);
                    }
                    else
                    {
                        if (noteEditor.HasUnsavedChanges())
                        {
                            bool save_changes = AskToSaveUnsavedChanges("Opening note... but first:");
                            if (save_changes) noteEditor.Save();
                        }

                        try
                        {
                            noteEditor = new NoteEditor(selected_tree_item.FilePath);
                        }
                        catch (FileNotFoundException)
                        {
                            HandleFileNotFoundException(selected_tree_item.FilePath);
                            break;
                        }

                        // Keep the tree focus if the space key was pressed.
                        // Focus the editor if enter was pressed.
                        if (keyInfo.Key == ConsoleKey.Enter)
                        {
                            editorFocused = true;
                            AnsiConsole.Cursor.Show();
                        }

                        if (noteEditor.IsTypingDisabled())
                        {
                            editorFocused = false;
                            AnsiConsole.Cursor.Hide();
                        }

                        Set_NoteEditorRequiresUpdate();
                        noteTree.Set_RequiresUpdate();
                    }

                    Set_NoteEditorRequiresUpdate();
                    break;

                // F2 - rename the selected item
                case ConsoleKey.F2:
                    RenameSelectedTreeItem();
                    noteTree.Set_RequiresUpdate();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // N - Create new file (Ctrl+N also does this)
                case ConsoleKey.N:
                    HandleCreateNewFile();
                    break;

                // M - Create new folder (Ctrl+M also does this)
                case ConsoleKey.M:
                    HandleCreateNewFolder();
                    break;

                // Delete | Backspace - Delete the selected tree item (Ctrl+D also does this)
                case ConsoleKey.Delete:
                case ConsoleKey.Backspace:
                    HandleDeleteTreeItem();
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
                // Ctrl+Q - Quit \ close application
                case ConsoleKey.Q:
                    if (noteEditor.GetNotePath() != null && noteEditor.HasUnsavedChanges())
                    {
                        bool save_changes = AskToSaveUnsavedChanges("Closing NoteWorthy... but first:");
                        if (save_changes) noteEditor.Save();
                    }
                    Environment.Exit(0);
                    break;

                // Ctrl+L - Toggle focus to the tree
                case ConsoleKey.L:
                    editorFocused = false;
                    AnsiConsole.Cursor.Hide();
                    Set_NoteEditorRequiresUpdate();
                    noteTree.Set_RequiresUpdate();
                    break;

                // Ctrl+S - Save note
                case ConsoleKey.S:
                    noteEditor.Save();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Ctrl+R - Reload note
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
                    try
                    {
                        noteEditor = new NoteEditor(path);
                    }
                    catch (FileNotFoundException)
                    {
                        HandleFileNotFoundException(path);
                        break;
                    }

                    Set_NoteEditorRequiresUpdate();
                    break;

                // Ctrl+D - Delete line
                case ConsoleKey.D:
                    noteEditor.DeleteLine();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Ctrl+W - Close currently displayed note
                case ConsoleKey.W:
                    if (noteEditor.HasUnsavedChanges())
                    {
                        bool save_changes = AskToSaveUnsavedChanges("Closing note... but first:");
                        if (save_changes) noteEditor.Save();
                    }

                    noteEditor = new NoteEditor(null);
                    Set_NoteEditorRequiresUpdate();
                    // Move focus to the tree
                    editorFocused = false;
                    noteTree.Set_RequiresUpdate();
                    noteTree.SetVisible();
                    break;

                // Ctrl+Z - Undo
                case ConsoleKey.Z:
                    noteEditor.Undo();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Ctrl+Y - Redo
                case ConsoleKey.Y:
                    noteEditor.Redo();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Ctrl+End - Navigate to end of file
                case ConsoleKey.End:
                    noteEditor.MoveCursorToEndOfEditor();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Ctrl+Home | Navigate to start of file
                case ConsoleKey.Home:
                    noteEditor.MoveCursorToStartOfEditor();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Ctrl+RightArrow - Navigate one word to the right
                case ConsoleKey.RightArrow:
                    noteEditor.NavigateToNextWord();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Ctrl+LeftArrow - Navigate one word to the left
                case ConsoleKey.LeftArrow:
                    noteEditor.NavigateToPreviousWord();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Ctrl+K - Toggle insert mode
                case ConsoleKey.K:
                    noteEditor.ToggleInsertMode();
                    break;

                // Ctrl+Backspace - Delete word with backspace
                case ConsoleKey.Backspace:
                    noteEditor.DeleteWordWithBackspace();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Ctrl+Delete - delete word with delete key
                case ConsoleKey.Delete:
                    noteEditor.DeleteWordWithDeleteKey();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Ctrl+N - Create new file
                case ConsoleKey.N:
                    HandleCreateNewFile();
                    break;

                // Ctrl+M - Create new folder
                case ConsoleKey.M:
                    HandleCreateNewFolder();
                    break;

                // Ctrl+O - Open the currently selected dir in the file explorer
                case ConsoleKey.O:
                    string file_path = noteTree.GetSelectedTreeItem()?.Parent == null ? NOTES_DIR_PATH : noteTree.GetSelectedTreeItem()!.Parent!.FilePath;
                    Process.Start("explorer.exe", file_path);
                    break;

                // Ctrl+8 - Open the settings file
                case ConsoleKey.D8:
                    Process.Start("notepad.exe", "settings.json");
                    break;

                // Ctrl+DownArrow - Navigate to the next note (downwards)
                case ConsoleKey.DownArrow:
                case ConsoleKey.PageDown:
                    // success == true if the next tree item was able to be selected
                    bool success = noteTree.SelectNextFileTreeItem();
                    if (!success) break;
                    if (noteEditor.HasUnsavedChanges())
                    {
                        Program.AskToSaveUnsavedChanges("Navigating to next file, but first...");
                    }

                    noteEditor = new NoteEditor(noteTree.GetSelectedTreeItem()!.FilePath);

                    // editor is focused by default
                    if (noteEditor.IsTypingDisabled())
                    {
                        editorFocused = false;
                    }

                    Set_NoteEditorRequiresUpdate();
                    noteTree.Set_RequiresUpdate();
                    break;

                // Ctrl+UpArrow - Navigate to previous note (upwards)
                case ConsoleKey.UpArrow:
                case ConsoleKey.PageUp:
                    // success == true if the selected tree item was changed to the next tree item
                    bool _success = noteTree.SelectPreviousFileTreeItem();
                    if (!_success) break;

                    if (noteEditor.HasUnsavedChanges())
                    {
                        Program.AskToSaveUnsavedChanges("Navigating to previous file, but first...");
                    }

                    noteEditor = new NoteEditor(noteTree.GetSelectedTreeItem()!.FilePath);

                    // editor is focused by default
                    if (noteEditor.IsTypingDisabled())
                    {
                        editorFocused = false;
                    }

                    Set_NoteEditorRequiresUpdate();
                    noteTree.Set_RequiresUpdate();
                    break;

                // Ctrl+B - Toggle primary color
                case ConsoleKey.B:
                    noteEditor.TogglePrimaryColor();
                    break;

                // Ctrl+I - Toggle secondary color
                case ConsoleKey.I:
                    noteEditor.ToggleSecondaryColor();
                    break;

                // Ctrl+U - Toggle tertiary color
                case ConsoleKey.U:
                    noteEditor.ToggleTertiaryColor();
                    break;

                // Ctrl+1 - Toggle the noteTree widget
                case ConsoleKey.D1:
                    if (noteEditor.GetNotePath() == null) break;
                    noteTree.ToggleVisibility();
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
                    Set_NoteEditorRequiresUpdate();
                    noteTree.Set_RequiresUpdate();
                    break;

                // Enter - add new line at current position
                case ConsoleKey.Enter:
                    noteEditor.InsertLine();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // UpArrow - Navigate the cursor up in the editor
                // Shift+UpArrow - Move line up
                case ConsoleKey.UpArrow:
                    if (keyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift))
                        noteEditor.MoveLineUp();
                    else
                        noteEditor.MoveCursorUp();

                    Set_NoteEditorRequiresUpdate();
                    break;

                // DownArrow - Navigate the cursor down in the editor
                // Shift+DownArrow - Move line down
                case ConsoleKey.DownArrow:
                    if (keyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift))
                        noteEditor.MoveLineDown();
                    else
                        noteEditor.MoveCursorDown();
                    
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Navigate the cursor left in the editor
                case ConsoleKey.LeftArrow:
                    noteEditor.MoveCursorLeft();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Navigate the cursor right in the editor
                case ConsoleKey.RightArrow:
                    noteEditor.MoveCursorRight();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Navigate to end of line
                case ConsoleKey.End:
                    noteEditor.MoveCursorToEndOfLine();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Navigate to start of line
                case ConsoleKey.Home:
                    noteEditor.MoveCursorToStartOfLine();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Insert - toggle insert mode
                case ConsoleKey.Insert:
                    noteEditor.ToggleInsertMode();
                    break;

                // Backspace - Delete char at cursor position with backspace
                case ConsoleKey.Backspace:
                    noteEditor.DeleteCharWithBackspace();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // Delete - Delete char at cursor position with delete key
                case ConsoleKey.Delete:
                    noteEditor.DeleteCharWithDeleteKey();
                    Set_NoteEditorRequiresUpdate();
                    break;

                // F2 - rename the current note
                case ConsoleKey.F2:
                    RenameSelectedTreeItem();
                    noteTree.Set_RequiresUpdate();
                    Set_NoteEditorRequiresUpdate();
                    // focus the tree in case user wants to keep renaming stuff
                    editorFocused = false;
                    break;

                // Print char to the editor if it is a non-control char
                default:
                    // Only print if the key is a non-control char
                    if (!char.IsControl(keyInfo.KeyChar))
                    {
                        noteEditor.InsertChar(keyInfo.KeyChar);
                        Set_NoteEditorRequiresUpdate();
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
        TreeItem? selected_item = noteTree.GetSelectedTreeItem();
        if (selected_item == null) return;
        Console.Clear();

        string new_name = AnsiConsole.Prompt(
            new TextPrompt<string>($"Rename [yellow]{selected_item.Name}[/]: ")
            .Validate((string s) =>
            {
                if (selected_item.IsDir)
                {
                    // -4 to account for panel border and padding
                    return s.Length > 0 && s.Length <= NoteTree.DISPLAY_WIDTH - 4;
                }
                else
                {
                    // -4 to account for panel border and padding
                    // -3 to account for the .nw that will be added if it's not already there
                    return s.Length > 0 && s.Length <= NoteTree.DISPLAY_WIDTH - 4 - (s.EndsWith(".nw") ? 0 : 3);
                }
            }, $"The name must be less than {NoteTree.DISPLAY_WIDTH - 4 + 1} characters, including file extension")
        ).Trim();
        if (new_name == null || new_name?.Length == 0)
        {
            return;
        }

        if (selected_item.IsDir)
        {
            // Rename in file system
            Directory.Move(selected_item.FilePath, Path.Combine(Path.GetDirectoryName(selected_item.FilePath)!, new_name!));
        }
        else
        {
            if (!new_name!.EndsWith(".nw")) new_name += ".nw";
            // Rename in file system
            File.Move(selected_item.FilePath, Path.Combine(Path.GetDirectoryName(selected_item.FilePath)!, new_name));
        }

        // Remake tree in order to reflect changes
        // This was required because the GetSelectedTreeItem was returning a deep copy, not a reference
        // Therefore, the changes weren't being reflected in the tree, and it was not possible to get a deep copy to how GetSelectedTreeItem works.
        // Do not try to refact this.
        noteTree = new NoteTree();
        noteEditor = new NoteEditor(null);
        noteTree.SetVisible();
        Set_NoteEditorRequiresUpdate();
        editorFocused = false; // Focus the tree in case user wants to keep renaming stuff
        AnsiConsole.Cursor.Hide();
    }

    /// <summary>
    /// Ask the user if they would like to save their unsaved note (only call if the note has unsaved changes)
    /// </summary>
    /// <returns>True if the user wants to save the note, false if they don't</returns></returns>
    /// <param name="msg">Message to display at the beginning of the string.
    /// Ex: "Reload Note", to let them know what's gonna happen after they deal with the message</param>
    public static bool AskToSaveUnsavedChanges(string msg)
    {
        AnsiConsole.Cursor.Hide();
        Console.Clear();
        AnsiConsole.Write(
            new Markup($"{msg}\n\nYou have unsaved changes.\nPress '[yellow]Y[/]' to save changes.\nPress '[yellow]N[/]' to discard changes.")
        );

        while (true)
        {
            ConsoleKeyInfo keyInfo = Console.ReadKey(true);
            Set_NoteEditorRequiresUpdate();
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

    /// <summary>
    /// For Ctrl+N functionality
    /// </summary>
    private static void HandleCreateNewFile()
    {
        bool cursor_visible = Console.CursorVisible;
        AnsiConsole.Cursor.Show();
        // Ctrl+N - Create new file
        string? file_path = noteTree.CreateFile();
        Set_NoteEditorRequiresUpdate();
        noteTree.Set_RequiresUpdate();

        if (file_path == null) return;

        // Focus the newly-made file
        editorFocused = true;
        noteEditor = new NoteEditor(file_path);
        noteTree.NavigateToTreeItemInCurrentDirByPath(file_path);
        if (!cursor_visible) AnsiConsole.Cursor.Hide();
    }

    /// <summary>
    /// For Ctrl+M functionality
    /// </summary>
    private static void HandleCreateNewFolder()
    {
        bool cursor_visible = Console.CursorVisible;
        AnsiConsole.Cursor.Show();
        string? folder_path = noteTree.CreateFolder();
        Set_NoteEditorRequiresUpdate();
        noteTree.Set_RequiresUpdate();

        if (folder_path == null) return;

        // Focus the tree in case user wants to navigate the newly-made folder
        editorFocused = false;
        noteTree.NavigateToTreeItemInCurrentDirByPath(folder_path);
        if (!cursor_visible) AnsiConsole.Cursor.Hide();
    }

    private static void HandleDeleteTreeItem()
    {
        TreeItem? selected_tree_item = noteTree.GetSelectedTreeItem();   
        if (selected_tree_item == null) return;

        Console.Clear();
        AnsiConsole.Write(new Markup($"You are about to delete [yellow]{selected_tree_item.Name}[/]\n\n" +
            $"Press '[yellow]Y[/]' to delete.\nPress '[yellow]N[/]' to cancel."));

        while (true)
        {
            ConsoleKeyInfo keyInfo = Console.ReadKey(true);
            if (keyInfo.Key == ConsoleKey.Y)
            {
                noteTree.DeleteSelectedTreeItem();
                break;
            }
            else if (keyInfo.Key == ConsoleKey.N)
            {
                break;
            }
        }

        Set_NoteEditorRequiresUpdate();
        noteTree.Set_RequiresUpdate();
    }

    private static void HandleFileNotFoundException(string path)
    {
        string name = Path.GetFileName(path);
        AnsiConsole.Cursor.Hide();
        Console.Clear();
        AnsiConsole.Markup($"The file '[yellow]{name}[/]' no longer exists.\n\nPress any key to continue...");

        // Reload the tree
        noteTree = new NoteTree();
        noteEditor = new NoteEditor(null);
        noteTree.SetVisible();

        Console.ReadKey(true);
        noteTree.Set_RequiresUpdate();
        Set_NoteEditorRequiresUpdate();
        editorFocused = false;
    }

    private static void Set_NoteEditorRequiresUpdate()
    {
        noteEditorRequiresUpdate = true;
    }

    public static bool NoteTreeVisible()
    {
        return noteTree.IsVisible();
    }

    public static void UnfocusEditor()
    {
        editorFocused = false;
    }
}