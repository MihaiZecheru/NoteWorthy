using Spectre.Console;

namespace NoteWorthy;

internal class NoteTree
{
    public static readonly int DISPLAY_WIDTH = 32;
    private readonly List<TreeItem> treeItems;
    /// <summary>
    /// The parent of the currently selected TreeItem.
    /// Set to null if the selected TreeItem is the root directory.
    /// </summary>
    private TreeItem? current_parent_treeItem = null;

    /// <summary>
    /// The index of the first TreeItem to display. Used for when there's too many TreeItems to display at once.
    /// </summary>
    private int starting_display_index = 0;

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
        string[] files = Directory.GetFiles(directoryPath);
        string[] directories = Directory.GetDirectories(directoryPath);

        IEnumerable<TreeItem> _files = files.Select((string file) =>
            new TreeItem(file, parent)
        );
        IEnumerable<TreeItem> _directories = directories.Select(directory =>
        {
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
        List<TreeItem> _treeItems = GetTreeItemsForDisplay();

        if (_treeItems.Count == 0)
        {
            return new Panel("Folder empty")
                .Header("[yellow] " + (current_parent_treeItem == null ? "Root" : current_parent_treeItem.Name) + " [/]")
                .Expand()
                .RoundedBorder();
        }

        if (_treeItems.Count > Console.BufferHeight - 2)
        {
            _treeItems = _treeItems.Skip(starting_display_index).ToList();
        }

        TreeItem selected_tree_item = GetSelectedTreeItem();
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
    private List<TreeItem> GetTreeItemsForDisplay()
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
        current_parent_treeItem = treeItem;
        starting_display_index = 0;
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
        if (starting_display_index == 0)
        {
            MoveSelectionToBottom();
        }
        else
        {
            starting_display_index--;
            Set_RequiresUpdate();
        }
    }

    public void MoveSelectionDown()
    {
        if (starting_display_index == GetTreeItemsForDisplay().Count - 1)
        {
            MoveSelectionToTop();
        }
        else
        {
            starting_display_index++;
            Set_RequiresUpdate();
        }
    }

    public void MoveSelectionToTop()
    {
        starting_display_index = 0;
        Set_RequiresUpdate();
    }

    public void MoveSelectionToBottom()
    {
        starting_display_index = GetTreeItemsForDisplay().Count - 1;
        Set_RequiresUpdate();
    }

    public TreeItem GetSelectedTreeItem()
    {
        return GetTreeItemsForDisplay()[starting_display_index];
    }
}
