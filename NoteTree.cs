using Spectre.Console;

namespace NoteWorthy;

internal class NoteTree
{
    /// <summary>
    /// The width of the entire panel
    /// </summary>
    public static readonly int DISPLAY_WIDTH = 32;

    /// <summary>
    /// The width of the text buffer that will be displayed inside the panel.
    /// </summary>
    public static readonly int BUFFER_WIDTH = DISPLAY_WIDTH - 4;

    /// <summary>
    /// The height of the TreeItems panel.
    /// -3 for the footer panel
    /// </summary>
    public static readonly int DISPLAY_HEIGHT = Console.BufferHeight - 3;

    /// <summary>
    /// The height of the buffer within the TreeItems panel
    /// -2 for the top and bottom borders.
    /// </summary>
    private static readonly int BUFFER_HEIGHT = DISPLAY_HEIGHT - 2;

    private readonly List<TreeItem> treeItems;
    /// <summary>
    /// The parent of the currently selected TreeItem.
    /// Set to null if the selected TreeItem is the root directory.
    /// </summary>
    private TreeItem? current_parent_treeItem = null;

    /// <summary>
    /// The index of the selected TreeItem
    /// </summary>
    private int selected_item_index = 0;

    /// <summary>
    /// True when the display should be updated.
    /// </summary>
    private bool requires_update = true;

    public NoteTree()
    {
        // Load the notes from the notes directory
        treeItems = LoadTreeItems(Program.NOTES_DIR_PATH, null);
    }

    /// <summary>
    /// Get the notes from the notes directory and add them to the NoteTree as TreeItems.
    /// </summary>
    private static List<TreeItem> LoadTreeItems(string directoryPath, TreeItem? parent)
    {
        IEnumerable<string> files = Directory.GetFiles(directoryPath).Where((string file) => file.EndsWith(".nw"));
        string[] directories = Directory.GetDirectories(directoryPath);

        IEnumerable<TreeItem> _files = files.Select((string file) =>
        {
            int len = Path.GetFileName(file)!.Length;
            if (len > BUFFER_WIDTH)
            {
                throw new FileLoadException($"File name is too long. Must be less than {BUFFER_WIDTH} characters long. Is {len} long. Path: {file}");
            }
            return new TreeItem(file, parent);
        });
        IEnumerable<TreeItem> _directories = directories.Select(directory =>
        {
            int len = Path.GetDirectoryName(directory)!.Length;
            if (len > BUFFER_WIDTH)
            {
                throw new FileLoadException($"Directory name is too long. Must be less than {BUFFER_WIDTH} characters long. Is {len} long. Path: {directory}");
            }

            var treeItem = new TreeItem(directory, parent);
            treeItem.SetDirectory(LoadTreeItems(directory, treeItem));
            return treeItem;
        });

        return _directories.Concat(_files).ToList();
    }

    /// <summary>
    /// Return a <see cref="Spectre.Console.Panel"/> representing the NoteTree 
    /// which will be displayed on the left side of the screen
    /// </summary>
    public Panel GenerateDisplayPanel()
    {
        List<TreeItem> _treeItems = GetParentTreeItemChildren();

        if (_treeItems.Count == 0)
        {
            return new Panel("Folder empty")
                .Header("[yellow] " + (current_parent_treeItem == null ? "Root" : current_parent_treeItem.Name) + " [/]")
                .Expand()
                .RoundedBorder();
        }

        if (_treeItems.Count > Console.BufferHeight - 2)
        {
            _treeItems = _treeItems.Skip(selected_item_index).ToList();
        }

        TreeItem selected_tree_item = GetSelectedTreeItem()!;
        requires_update = false;
        string names_string = string.Join("\n", _treeItems.Select((TreeItem x) =>
            (x == selected_tree_item ? "[yellow]" : "[white]") + x.Name + (x.IsDir ? "/" : "") + "[/]"
        ));

        return new Panel(names_string)
            .Header("[yellow] " + (current_parent_treeItem == null ? "Root"  : current_parent_treeItem.Name) + " [/]")
            .Expand()
            .RoundedBorder();
    }

    /// <summary>
    /// Get the current tree items that are to be displayed.
    /// 
    /// If the current parent is null, then the root directory is being displayed.
    /// </summary>
    private List<TreeItem> GetParentTreeItemChildren()
    {
        if (current_parent_treeItem == null)
        {
            return treeItems;
        }
        return current_parent_treeItem.Children;
    }

    /// <summary>
    /// Navigate to a TreeItem in the NoteTree. 
    /// </summary>
    /// <param name="treeItem">Set to <see langword="null" /> to navigate to the root directory.</param>
    public void NavigateToTreeItem(TreeItem? treeItem)
    {
        if (treeItem != null && !treeItem.IsDir) throw new Exception("Cannot navigate to a file.");
        current_parent_treeItem = treeItem;
        selected_item_index = 0;
        Set_RequiresUpdate();
    }

    /// <summary>
    /// Navigate to the parent item if there is a parent.
    /// </summary>
    public void GoToParentIfPossible()
    {
        if (current_parent_treeItem == null) return;
        NavigateToTreeItem(current_parent_treeItem.Parent);
    }

    /// <summary>
    /// Checks if the display needs to be updated.
    /// </summary>
    public bool Get_RequiresUpdate()
    {
        return requires_update;
    }

    /// <summary>
    /// Specify that an update is required.
    /// </summary>
    public void Set_RequiresUpdate()
    {
        requires_update = true;
    }

    public void MoveSelectionUp()
    {
        if (selected_item_index == 0)
        {
            MoveSelectionToBottom();
        }
        else
        {
            selected_item_index--;
            Set_RequiresUpdate();
        }
    }

    public void MoveSelectionDown()
    {
        if (selected_item_index == GetParentTreeItemChildren().Count - 1)
        {
            MoveSelectionToTop();
        }
        else
        {
            selected_item_index++;
            Set_RequiresUpdate();
        }
    }

    public void MoveSelectionToTop()
    {
        selected_item_index = 0;
        Set_RequiresUpdate();
    }

    public void MoveSelectionToBottom()
    {
        selected_item_index = GetParentTreeItemChildren().Count - 1;
        Set_RequiresUpdate();
    }

    public TreeItem? GetSelectedTreeItem()
    {
        List<TreeItem> items = GetParentTreeItemChildren();
        if (items.Count == 0) return null;
        return GetParentTreeItemChildren()[selected_item_index];
    }

    /// <summary>
    /// Prompt the user for a new folder name and create the folder inside of the current directory.
    /// </summary>
    /// <returns>Returns the path of the newly created file, or <see langword="null"/> if the user cancelled</returns>
    public string? CreateFolder()
    {
        Console.Clear();
        AnsiConsole.Write(new Markup("[yellow]Create a new folder[/]. Type a period '[yellow].[/]' to cancel.\n\n"));

        string folder_name = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter the name of the folder:")
                .Validate(name => !string.IsNullOrWhiteSpace(name) && !name.Contains('/'), "Folder name cannot be empty or contain '/'")
        ).Trim();

        if (folder_name == ".") return null;
        string folder_path = Path.Combine(current_parent_treeItem?.FilePath ?? Program.NOTES_DIR_PATH, folder_name);

        Directory.CreateDirectory(folder_path);
        var treeItem = new TreeItem(folder_path, current_parent_treeItem);
        treeItem.SetDirectory(new List<TreeItem>());
        if (current_parent_treeItem == null)
            treeItems.Add(treeItem);
        else current_parent_treeItem!.AddChild(treeItem);
        return folder_path;
    }

    /// <summary>
    /// Prompt the user for a new file name and create the file inside of the current directory.
    /// </summary>
    /// <returns>The path of the newly-created file. <see langword="null"/> if the user cancelled.</returns>
    public string? CreateFile()
    {
        Console.Clear();
        AnsiConsole.Write(new Markup("[yellow]Create a new file[/]. Type a period '[yellow].[/]' to cancel.\n\n"));

        List<TreeItem> treeItemsInDir = GetParentTreeItemChildren();
        string file_name = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter the name of the file:")
                .Validate(name => !string.IsNullOrWhiteSpace(name) && !name.Contains('/') && !name.Contains('\\'), "Folder name cannot be empty or contain '/' or '\\")
                .Validate(name =>
                {
                    for (int i = 0; i < treeItemsInDir.Count; i++)
                    {
                        if (treeItemsInDir[i].Name == name || treeItemsInDir[i].Name == name + ".nw") return false;
                    }

                    return true;
                }, "A file with that name already exists in this directory.")
        ).Trim();

        if (file_name == ".") return null;
        if (!file_name.EndsWith(".nw"))
            file_name += ".nw";

        string file_path = Path.Combine(current_parent_treeItem?.FilePath ?? Program.NOTES_DIR_PATH, file_name);
        File.Create(file_path).Close();
        if (current_parent_treeItem == null)
            treeItems.Add(new TreeItem(file_path, current_parent_treeItem));
        else current_parent_treeItem!.AddChild(new TreeItem(file_path, current_parent_treeItem));
        return file_path;
    }

    /// <summary>
    /// Set the currently selected tree item to the TreeItem with the given path
    /// </summary>
    /// <param name="path">The path to the tree item in the file system</param>
    public void NavigateToTreeItemInCurrentDirByPath(string path)
    {
        List<TreeItem> nodes = GetParentTreeItemChildren();
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].FilePath == path)
            {
                selected_item_index = i;
                Set_RequiresUpdate();
                return;
            }
        }
    }

    public void DeleteSelectedTreeItem()
    {
        TreeItem? selected_tree_item = GetSelectedTreeItem();
        if (selected_tree_item == null) return;

        if (selected_tree_item.IsDir)
        {
            Directory.Delete(selected_tree_item.FilePath, true);
        }
        else
        {
            File.Delete(selected_tree_item.FilePath);
        }

        if (current_parent_treeItem == null)
        {
            treeItems.Remove(selected_tree_item);
        }
        else
        {
            current_parent_treeItem.Children.Remove(selected_tree_item);
        }

        if (selected_item_index >= GetParentTreeItemChildren().Count)
        {
            selected_item_index = GetParentTreeItemChildren().Count - 1;
        }

        Set_RequiresUpdate();
    }

    /// <summary>
    /// Get the next note file in the current directory.
    /// </summary>
    /// <returns>True if the next tree item was able to be selected.
    /// Will be false if there was no selected tree item in the first place or if the currently selected tree item is a directory</returns>
    public bool SelectNextFileTreeItem()
    {
        // No file is selected, so you can't go to the next file
        if (GetSelectedTreeItem() == null || GetSelectedTreeItem()!.IsDir) return false;
        List<TreeItem> tree_items = GetParentTreeItemChildren().Where(x => !x.IsDir).ToList();
        
        // If the selected file is the last file, select the top file
        if (tree_items.IndexOf(GetSelectedTreeItem()!) == tree_items.Count - 1)
        {
            selected_item_index = GetParentTreeItemChildren().IndexOf(treeItems[0]);
            return true;
        }

        selected_item_index = tree_items.IndexOf(GetSelectedTreeItem()!) + 1;
        requires_update = true;
        return true;
    }

    public bool SelectPreviousFileTreeItem()
    {
        // No file is selected, so you can't go to the previous file
        if (GetSelectedTreeItem() == null || GetSelectedTreeItem()!.IsDir) return false;
        List<TreeItem> tree_items = GetParentTreeItemChildren().Where(x => !x.IsDir).ToList();

        // If the selected file is the first file, select the last file
        if (tree_items.IndexOf(GetSelectedTreeItem()!) == 0)
        {
            selected_item_index = GetParentTreeItemChildren().IndexOf(treeItems[tree_items.Count - 1]);
            return true;
        }

        selected_item_index = tree_items.IndexOf(GetSelectedTreeItem()!) - 1;
        requires_update = true;
        return true;
    }

    private bool is_visible = true;
    public void ToggleVisibility()
    {
        is_visible = !is_visible;
        requires_update = true;
    }

    public bool IsVisible()
    {
        return is_visible;
    }

    public void SetVisible()
    {
        is_visible = true;
    }

    /// <summary>
    /// Make a copy of the selected tree item if it is a file. 
    /// The copy will not be made if a file with the resulting name (name - copy.nw) already exists.
    /// </summary>
    /// <returns>True if a copy was made, false otherwise</returns>
    public bool CopySelectedFile()
    {
        TreeItem? t = GetSelectedTreeItem();
        if (t == null || t.IsDir) return false;

        string file_name = Path.GetFileName(t.FilePath);
        string file_name_no_ext = Path.GetFileNameWithoutExtension(t.FilePath);

        string new_name;
        if (file_name.Length <= BUFFER_WIDTH - " - Copy".Length)
        {
            new_name = file_name_no_ext + " - Copy.nw";
        }
        else
        {
            new_name = file_name.Substring(0, BUFFER_WIDTH - " ... - Copy.nw".Length) + " ... - Copy.nw";
        }

        string new_path = Path.Combine(Path.GetDirectoryName(t.FilePath)!, new_name);

        if (File.Exists(new_path))
        {
            Console.Beep();
            return false;
        }

        File.Copy(t.FilePath, new_path);
        TreeItem new_tree_item = new TreeItem(new_path, current_parent_treeItem);
        return true;
    }
}
