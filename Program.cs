using Spectre.Console;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NoteWorthy;

class Program
{
    public static Spectre.Console.Style DefaultStyle = new Style(Color.White, Color.Grey46);

    /// <summary>
    /// The path to the root 'notes' directory which contains all the notes
    /// </summary>
    public static string NOTES_DIR_PATH = Path.Combine(Directory.GetCurrentDirectory(), "notes");

    private static NoteTree noteTree = new NoteTree();
    private static NoteEditor noteEditor = new NoteEditor(null);

    /// <summary>
    /// The layout of the display. Gets printed every time an update is required.
    /// </summary>
    private static Layout display_layout = GenerateEmptyLayout();

    /// <summary>
    /// Set to true when a new note is loaded (and therefore a new NoteEditor is created) in order to render the new note.
    /// </summary>
    private static bool noteEditorRequiresUpdate = true;
    
    /// <summary>
    /// Set to true when the editor (the right side) is focused. Set to false when the tree (the left side) is focused.
    /// </summary>
    private static bool editorFocused = false;

    /// <summary>
    /// Set to true when the tree footer requires an update
    /// </summary>
    private static bool treeFooterRequiresUpdate = true;

    // For allowing ctrl+s as a shortcut
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);
    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, int dwMode);

    public static void Main()
    {
        Make_CtrlC_CopySelectedTreeItem();
        Allow_CtrlS_Shortcut();
        SetBlockCursor();
        InitializeNotesDirectory();
        StartResizeEventListener();
        Mainloop();
    }

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    private static void Make_CtrlC_CopySelectedTreeItem()
    {
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;

            // If the note tree is focused, ctrl+c will copy the selected file            
            if (!editorFocused)
            {
                bool success = noteTree.CopySelectedTreeItem();
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
                    if (Console.WindowHeight < 10)
                    {
                        Console.Clear();
                        while (Console.WindowHeight < 10)
                        {
                            Console.SetCursorPosition(0, 0);
                            Console.WriteLine("Screen height too small.");
                        }
                    }

                    width = Console.WindowWidth;
                    height = Console.WindowHeight;
                    Console.Clear();

                    // Update buffers to reflect new screen size and regenerate layout component based on the new buffers
                    noteEditor.UpdateBuffers();
                    NoteTree.UpdateBuffers();
                    display_layout = GenerateEmptyLayout();

                    // Update display
                    Set_NoteEditorRequiresUpdate();
                    noteTree.Set_RequiresUpdate();
                    SetTreeFooterRequiresUpdate();
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
        var treeItemsLayout = display_layout.GetLayout("TreeItems");
        var treeFooterLayout = display_layout.GetLayout("TreeFooter");
        var noteEditorLayout = display_layout.GetLayout("NoteEditor");

        bool note_tree_requires_update = noteTree.Get_RequiresUpdate();

        // Note tree
        if (note_tree_requires_update)
        {
            var panel = noteTree.GenerateDisplayPanel();

            // The tree must be focused if the editor isn't
            if (!editorFocused)
            {
                panel.BorderColor(Color.Aqua);
            }

            // Will only rewrite if there's been a change to them
            treeItemsLayout.Update(panel);

            if (noteTree.IsVisible() )
            {
                treeFooterLayout.Visible();
                treeItemsLayout.Visible();
            }
            else
            {
                treeFooterLayout.Invisible();
                treeItemsLayout.Invisible();
            }
        }

        // Note editor
        if (noteEditorRequiresUpdate)
        {
            var panel = noteEditor.GenerateDisplayPanel();

            if (editorFocused)
            {
                panel.BorderColor(Color.Aqua);
            }

            noteEditorLayout.Update(panel);
        }

        // Tree footer
        if (treeFooterRequiresUpdate)
        {
            var panel = GenerateTreeFooterPanel();
            treeFooterLayout.Update(panel);
        }

        // If any of the panels require an update, then update the display
        if (note_tree_requires_update || noteEditorRequiresUpdate || treeFooterRequiresUpdate)
        {
            AnsiConsole.Cursor.Hide();
            noteEditorRequiresUpdate = false;
            treeFooterRequiresUpdate = false;
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
                    ExitApplication();
                    break;

                // Ctrl+L - Toggle focus to the editor
                case ConsoleKey.L:
                    if (noteEditor.GetNotePath() == null) break;
                    if (noteEditor.IsTypingDisabled()) break;
                    editorFocused = true;
                    AnsiConsole.Cursor.Show();
                    noteTree.Set_RequiresUpdate();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+O - Open the currently selected dir in the file explorer
                case ConsoleKey.O:
                    string path = noteTree.GetSelectedTreeItem()?.Parent == null ? NOTES_DIR_PATH : noteTree.GetSelectedTreeItem()!.Parent!.FilePath;
                    Process.Start("explorer.exe", path);
                    break;

                // Ctrl+Up Arrow - Move up one and open the file. Same as Ctrl+UpArrow in the editor
                case ConsoleKey.UpArrow:
                case ConsoleKey.PageUp:
                    HandleCtrlUpArrow();
                    break;

                // Ctrl+Down Arrow - Move down one and open the file. Same as Ctrl+DownArrow in the editor
                case ConsoleKey.DownArrow:
                case ConsoleKey.PageDown:
                    HandleCtrlDownArrow();
                    break;

                // Ctrl+W - Close the note in the editor if there's a note, otherwise close the app
                case ConsoleKey.W:
                    if (noteEditor.GetNotePath() == null)
                    {
                        // If the user presses Ctrl+W when there is no note, close the app
                        ExitApplication();
                    }
                    else
                    {
                        noteEditor = new NoteEditor(null);
                        noteTree.SetVisible();
                        Set_NoteEditorRequiresUpdate();
                        // Move focus to the tree
                        editorFocused = false;
                        noteTree.Set_RequiresUpdate();
                        SetTreeFooterRequiresUpdate();
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
                    SetTreeFooterRequiresUpdate();
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
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+H - Toggle the help panel
                case ConsoleKey.H:
                    noteEditor.ToggleHelpPanel();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;
            }
        }
        // For functionality with regular keypresses
        else
        {
            TreeItem? selected_tree_item = noteTree.GetSelectedTreeItem();
            switch (keyInfo.Key)
            {
                // Tab - Toggle focus to the editor
                case ConsoleKey.Tab:
                    if (noteEditor.GetNotePath() == null) break;
                    if (noteEditor.IsTypingDisabled()) break;
                    editorFocused = true;
                    AnsiConsole.Cursor.Show();
                    noteTree.Set_RequiresUpdate();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Up Arrow - Move selection up
                case ConsoleKey.UpArrow:
                case ConsoleKey.PageUp:
                    noteTree.MoveSelectionUp();
                    break;

                // Down Arrow - Move selection down
                case ConsoleKey.DownArrow:
                case ConsoleKey.PageDown:
                    noteTree.MoveSelectionDown();
                    break;

                // Home - Move selection to top
                case ConsoleKey.Home:
                    noteTree.MoveSelectionToTop();
                    break;

                // End - Move selection to bottom
                case ConsoleKey.End:
                    noteTree.MoveSelectionToBottom();
                    break;

                // Escape | B | Tab - Go to parent directory (go back)
                case ConsoleKey.Escape:
                case ConsoleKey.B:
                    noteTree.GoToParentIfPossible();
                    break;

                // Enter | Space - Open the note in the editor.
                // If space is pressed, keep the tree focused. If enter is pressed, focus the editor.
                case ConsoleKey.Enter:
                case ConsoleKey.Spacebar:
                    if (selected_tree_item == null) break;

                    if (selected_tree_item.IsDir)
                    {
                        noteTree.NavigateToTreeItemDirectory(selected_tree_item);
                    }
                    else
                    {
                        if (noteEditor.HasUnsavedChanges())
                        {
                            bool save_changes = AskToSaveUnsavedChanges("[yellow]Opening note[/]... but first:");
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

                        SetTreeFooterRequiresUpdate();
                        Set_NoteEditorRequiresUpdate();
                        noteTree.Set_RequiresUpdate();
                    }

                    Set_NoteEditorRequiresUpdate();
                    break;

                // F2 - Rename the selected item
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
                    ExitApplication();
                    break;

                // Ctrl+L - Toggle focus to the tree
                case ConsoleKey.L:
                    editorFocused = false;
                    AnsiConsole.Cursor.Hide();
                    Set_NoteEditorRequiresUpdate();
                    noteTree.Set_RequiresUpdate();
                    SetTreeFooterRequiresUpdate();
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
                        bool save_changes = AskToSaveUnsavedChanges("[yellow]Reloading note...[/] but first:");
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
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+D - Delete line
                case ConsoleKey.D:
                    noteEditor.DeleteLine();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+W - Close currently displayed note
                case ConsoleKey.W:
                    if (noteEditor.HasUnsavedChanges())
                    {
                        bool save_changes = AskToSaveUnsavedChanges("[yellow]Closing note... [/]but first:");
                        if (save_changes) noteEditor.Save();
                    }

                    noteEditor = new NoteEditor(null);
                    Set_NoteEditorRequiresUpdate();
                    // Move focus to the tree
                    editorFocused = false;
                    noteTree.Set_RequiresUpdate();
                    noteTree.SetVisible();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+Z - Undo
                case ConsoleKey.Z:
                    noteEditor.Undo();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+Y - Redo
                case ConsoleKey.Y:
                    noteEditor.Redo();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+End - Navigate to end of file
                case ConsoleKey.End:
                    noteEditor.MoveCursorToEndOfEditor();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+Home | Navigate to start of file
                case ConsoleKey.Home:
                    noteEditor.MoveCursorToStartOfEditor();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+RightArrow - Navigate one word to the right
                case ConsoleKey.RightArrow:
                    noteEditor.NavigateToNextWord();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+LeftArrow - Navigate one word to the left
                case ConsoleKey.LeftArrow:
                    noteEditor.NavigateToPreviousWord();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+K - Toggle insert mode
                case ConsoleKey.K:
                    noteEditor.ToggleInsertMode();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+Backspace - Delete word with backspace
                case ConsoleKey.Backspace:
                    noteEditor.DeleteWordWithBackspace();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+Delete - delete word with delete key
                case ConsoleKey.Delete:
                    noteEditor.DeleteWordWithDeleteKey();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+N - Create new file
                case ConsoleKey.N:
                    HandleCreateNewFile();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+M - Create new folder
                case ConsoleKey.M:
                    HandleCreateNewFolder();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+O - Open the currently selected dir in the file explorer
                case ConsoleKey.O:
                    string file_path = noteTree.GetSelectedTreeItem()?.Parent == null ? NOTES_DIR_PATH : noteTree.GetSelectedTreeItem()!.Parent!.FilePath;
                    Process.Start("explorer.exe", file_path);
                    break;

                // Ctrl+8 - Open the settings file
                case ConsoleKey.D8:
                    Process.Start("notepad.exe", "settings.txt");
                    break;

                // Ctrl+DownArrow - Navigate to the next note (downwards)
                case ConsoleKey.DownArrow:
                case ConsoleKey.PageDown:
                    HandleCtrlDownArrow();
                    break;

                // Ctrl+UpArrow - Navigate to previous note (upwards)
                case ConsoleKey.UpArrow:
                case ConsoleKey.PageUp:
                    HandleCtrlUpArrow();
                    break;

                // Ctrl+B - Toggle primary color
                case ConsoleKey.B:
                    noteEditor.TogglePrimaryColor();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+U - Toggle secondary color
                case ConsoleKey.U:
                    noteEditor.ToggleSecondaryColor();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+I - Toggle tertiary color
                case ConsoleKey.I:
                    noteEditor.ToggleTertiaryColor();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+1 - Toggle the noteTree widget
                case ConsoleKey.D1:
                    if (noteEditor.GetNotePath() == null) break;
                    noteTree.ToggleVisibility();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+G - Go to line
                case ConsoleKey.G:
                    int? navigate_to = GetLineToNavigateTo();
                    if (navigate_to != null)
                    {
                        noteEditor.GoToLine((int)navigate_to);
                    }

                    Set_NoteEditorRequiresUpdate();
                    noteTree.Set_RequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Ctrl+H - Toggle the help panel
                case ConsoleKey.H:
                    noteEditor.ToggleHelpPanel();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;
            }
        }
        // For functionality with regular keypresses
        else
        {
            switch (keyInfo.Key)
            {
                // Escape - Unfocus the editor, therefore focusing the tree
                case ConsoleKey.Escape:
                    editorFocused = false;
                    AnsiConsole.Cursor.Hide();
                    Set_NoteEditorRequiresUpdate();
                    noteTree.Set_RequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Enter - Add new line at current position
                case ConsoleKey.Enter:
                    noteEditor.InsertLine();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // UpArrow - Navigate the cursor up in the editor
                // Shift+UpArrow - Move line up
                case ConsoleKey.UpArrow:
                    if (keyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift))
                        noteEditor.MoveLineUp();
                    else
                        noteEditor.MoveCursorUp();

                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // DownArrow - Navigate the cursor down in the editor
                // Shift+DownArrow - Move line down
                case ConsoleKey.DownArrow:
                    if (keyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift))
                        noteEditor.MoveLineDown();
                    else
                        noteEditor.MoveCursorDown();
                    
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // LeftArrow - Navigate the cursor left in the editor
                case ConsoleKey.LeftArrow:
                    noteEditor.MoveCursorLeft();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // RightArrow - Navigate the cursor right in the editor
                case ConsoleKey.RightArrow:
                    noteEditor.MoveCursorRight();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // End - Navigate to end of line
                case ConsoleKey.End:
                    noteEditor.MoveCursorToEndOfLine();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Home - Navigate to start of line
                case ConsoleKey.Home:
                    noteEditor.MoveCursorToStartOfLine();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Insert - Toggle insert mode
                case ConsoleKey.Insert:
                    noteEditor.ToggleInsertMode();
                    break;

                // Backspace - Delete char at cursor position with backspace
                case ConsoleKey.Backspace:
                    noteEditor.DeleteCharWithBackspace();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Delete - Delete char at cursor position with delete key
                case ConsoleKey.Delete:
                    noteEditor.DeleteCharWithDeleteKey();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // F2 - Rename the current note
                case ConsoleKey.F2:
                    RenameSelectedTreeItem();
                    noteTree.Set_RequiresUpdate();
                    Set_NoteEditorRequiresUpdate();
                    // focus the tree in case user wants to keep renaming stuff
                    editorFocused = false;
                    break;

                // Tab - Insert Settings.TabSize amount of spaces
                case ConsoleKey.Tab:
                    noteEditor.InsertTab();
                    Set_NoteEditorRequiresUpdate();
                    SetTreeFooterRequiresUpdate();
                    break;

                // Print char to the editor if it is a non-control char
                default:
                    // Only print if the key is a non-control char
                    if (!char.IsControl(keyInfo.KeyChar))
                    {
                        noteEditor.InsertChar(keyInfo.KeyChar);
                        Set_NoteEditorRequiresUpdate();
                    }
                    SetTreeFooterRequiresUpdate();
                    break;
            }
        }
    }

    /// <summary>
    /// Rename the tree item that is currently selected, whether it's a dir or a file.
    /// </summary>
    private static void RenameSelectedTreeItem()
    {
        if (noteEditor.HasUnsavedChanges())
        {
            AskToSaveUnsavedChanges("[yellow]Renaming note...[/] but first");
        }

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

        if (selected_tree_item.FilePath == noteEditor.GetNotePath())
        {
            noteEditor = new NoteEditor(null);
            editorFocused = false;
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

    private static string GetMarkup(string color)
    {
        return Spectre.Console.Color.FromInt32(byte.Parse(Settings.GetSetting(color)!)).ToMarkup();
    }

    private static Panel GenerateTreeFooterPanel()
    {
        // Looks kinda messy cus I optimized it for performance

        bool insertModeEnabled = noteEditor.IsInsertModeEnabled();
        bool primaryColorEnabled = noteEditor.IsPrimaryColorEnabled();
        bool secondaryColorEnabled = noteEditor.IsSecondaryColorEnabled();
        bool tertiaryColorEnabled = noteEditor.IsTertiaryColorEnabled();

        // Set the color of the mode text based on the insert mode setting
        // If the mode is "insert" and the default setting is "insert", use primary.
        // If the mode is "overwrite" and the default setting is "overwrite", use primary.
        // Otherwise, use secondary.
        string mode_color = insertModeEnabled == (Settings.GetSetting("write_mode") == "insert") ?
            GetMarkup("primary_color") :
            GetMarkup("secondary_color");

        StringBuilder str = new();

        // Write mode & colors
        if (insertModeEnabled)
        {
            str.Append($"[{mode_color}]Insert Mode[/]            ");
        }
        else
        {
            str.Append($"[{mode_color}]Overwrite Mode[/]         ");
        }

        if (primaryColorEnabled)
        {
            str.Append($"[{GetMarkup("primary_color")}]B[/] ");
        }
        else
        {
            str.Append("[white]B[/] ");
        }

        if (secondaryColorEnabled)
        {
            str.Append($"[{GetMarkup("secondary_color")}]U[/] ");
        }
        else
        {
            str.Append("[white]U[/] ");
        }

        if (tertiaryColorEnabled)
        {
            str.Append($"[{GetMarkup("tertiary_color")}]I[/]");
        }
        else
        {
            str.Append("[white]I[/]");
        }

        str.Append('\n');

        // Cursor position and line/char count

        // Show placeholder values T(x, x) P(x, x)
        if (noteEditor.GetNotePath() == null)
        {
            // Placeholder values. 14 spaces are used to make the P(x, x) stick to the right border
            str.Append($"T(x, x){new string(' ', 14)}P(x, x)");
        }
        // Show the T(l, c) total and the P(l, c) position
        else if (editorFocused)
        {
            // Get position
            (int line, int col) = noteEditor.GetCursorPosition();
            line++; col++;
            
            // Get total line and char count
            int lineCount = noteEditor.GetLineCount();
            int charCount = noteEditor.GetNoteCharCount();

            // Space count: 14 spaces would make P(x, x) stick to the right border. +4 - digit - digit - digit - digit will adjust the
            // spaces so that the P(x, x) sticks to the right border when x has more digits. the +4 accounts for each num being at least one digit
            int space_count = 14 + 4 - GetDigitCount(line) - GetDigitCount(col) - GetDigitCount(lineCount) - GetDigitCount(charCount);
            str.Append($"T({lineCount}, {charCount}){new string(' ', space_count)}P({line}, {col})");
        }
        else // if editor not focused, but there is a note, that means the editor is in preview mode. show only the total lines and total char count
        {
            int lineCount = noteEditor.GetLineCount();
            int charCount = noteEditor.GetNoteCharCount();
            int space_count = 14 + 2 - GetDigitCount(lineCount) - GetDigitCount(charCount);
            str.Append($"T({lineCount}, {charCount}){new string(' ', space_count)}P(x, x)");
        }

        return new Panel(str.ToString()).Expand().RoundedBorder();
    }

    public static void SetTreeFooterRequiresUpdate()
    {
        treeFooterRequiresUpdate = true;
    }

    private static int GetDigitCount(int number)
    {
        return number == 0 ? 1 : (int)Math.Floor(Math.Log10(number) + 1);
    }

    private static void HandleCtrlUpArrow()
    {
        // success == true if the selected tree item was changed to the next tree item
        bool _success = noteTree.SelectPreviousFileTreeItem();
        if (!_success) return;

        if (noteEditor.HasUnsavedChanges())
        {
            Program.AskToSaveUnsavedChanges("Navigating to previous file, but first...");
        }

        noteEditor = new NoteEditor(noteTree.GetSelectedTreeItem()!.FilePath);
        editorFocused = false;
        Set_NoteEditorRequiresUpdate();
        noteTree.Set_RequiresUpdate();
        SetTreeFooterRequiresUpdate();
    }

    private static void HandleCtrlDownArrow()
    {
        // success == true if the next tree item was able to be selected
        bool success = noteTree.SelectNextFileTreeItem();
        if (!success) return;
        if (noteEditor.HasUnsavedChanges())
        {
            Program.AskToSaveUnsavedChanges("Navigating to next file, but first...");
        }

        noteEditor = new NoteEditor(noteTree.GetSelectedTreeItem()!.FilePath);
        editorFocused = false;
        Set_NoteEditorRequiresUpdate();
        noteTree.Set_RequiresUpdate();
        SetTreeFooterRequiresUpdate();
    }

    private static int? GetLineToNavigateTo()
    {
        AnsiConsole.Cursor.Hide();
        string prompt = " | Line:    |";
        // +2 to account for the two digits the user can enter. -1 for the space at the front
        string dotted_line = ' ' + new string('-', prompt.Length - 1);

        // Draw dotted line above prompt
        // Puts the cursor in the top left of the panel, minus horizontal space for the dotted line
        // Console.BufferWidth - 3 is top left of the panel
        // - dotted_line.Length to account for the dotted line that will be written
        Console.SetCursorPosition(Console.BufferWidth - 3 - dotted_line.Length, 1);
        AnsiConsole.Markup($"[yellow]{dotted_line} [/]");

        // Write the prompt
        // Puts the cursor one lower than the top left of the panel, minus horizontal space for the prompt
        // Console.BufferWidth - 3 is top left of the panel
        // - prompt.Length to account for the prompt that will be written
        Console.SetCursorPosition(Console.BufferWidth - 3 - prompt.Length, 2);
        AnsiConsole.Markup($"[yellow]{prompt} [/]");

        // Draw dotted line underneath prompt
        // // Puts the cursor two lines lower than the top left of the panel, minus horizontal space for the dotted line
        // Console.BufferWidth - 3 is top left of the panel
        // - dotted_line.Length to account for the dotted line that will be written
        Console.SetCursorPosition(Console.BufferWidth - 3 - dotted_line.Length, 3);
        AnsiConsole.Markup($"[yellow]{dotted_line} [/]");
        // the additional -4 is to move it to the center of this ascii box, giving space for the user's two-char input

        // Write two blue zeros in the middle of the ascii box
        Console.SetCursorPosition(Console.BufferWidth - 3 - 4, 2);
        AnsiConsole.Markup("[dodgerblue2]00[/]");
        Console.SetCursorPosition(Console.BufferWidth - 3 - 4, 2);

        // Get the line num from the user
        string input = "";
        while (true)
        {
            if (!Console.KeyAvailable) continue;
            ConsoleKeyInfo keyInfo = Console.ReadKey(true);

            // Q will quit the Ctrl+G, returning null
            if (keyInfo.Key == ConsoleKey.Q)
            {
                AnsiConsole.Cursor.Show();
                return null;
            }

            // Enter will return the input
            if (keyInfo.Key == ConsoleKey.Enter) break;

            // Clear input
            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                while (input.Length > 0)
                {
                    input = input.Remove(input.Length - 1);
                    AnsiConsole.Markup("\b[dodgerblue2]0[/]\b");
                }

                continue;
            }

            // Do not allow non-digits
            if (!char.IsAsciiDigit(keyInfo.KeyChar)) continue;

            // Do not allow 0 to be the first digit
            if (keyInfo.Key == ConsoleKey.D0 && input.Length == 0) continue;

            // A maximum of two digits can be entered
            if (input.Length == 2) continue;

            // Add the digit to the input
            input += keyInfo.KeyChar;
            Console.SetCursorPosition(Console.BufferWidth - 3 - 4, 2);
            AnsiConsole.Markup("[dodgerblue2]" + (input.Length == 1 ? "0" : "") + input + "[/]");
        }

        AnsiConsole.Cursor.Show();
        return input.Length == 0 ? null : int.Parse(input);
    }

    private static void ExitApplication()
    {
        if (noteEditor.GetNotePath() != null && noteEditor.HasUnsavedChanges())
        {
            bool save_changes = AskToSaveUnsavedChanges("[yellow]Closing NoteWorthy... [/]but first:");
            if (save_changes) noteEditor.Save();
        }

        Console.Clear();
        Environment.Exit(0);
    }

    /// <summary>
    /// Return the empty layout.
    /// Call each time the screen size update to set the proper sizes for the layout.
    /// </summary>
    private static Layout GenerateEmptyLayout()
    {
        return new Layout()
        .SplitColumns(
            new Layout("TreeItemsAndFooter")
                .Size(NoteTree.DISPLAY_WIDTH)
                .SplitRows(
                    new Layout("TreeItems").Size(NoteTree.DISPLAY_HEIGHT),
                    new Layout("TreeFooter")
                ),
            new Layout("NoteEditor")
        );
    }
}