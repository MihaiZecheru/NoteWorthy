namespace NoteWorthy;

internal class TreeItem
{
    /// <summary>
    /// The path of the file or directory.
    /// </summary>
    public string FilePath { get; set; }

    /// <summary>
    /// The name of the TreeItem / file.
    /// </summary>
    public string Name { get => Path.GetFileName(FilePath); }

    /// <summary>
    /// True if the TreeItem represents a directory,
    /// Flase if it represents a file.
    /// </summary>
    public bool IsDir { get; set; }

    /// <summary>
    /// The children of the TreeItem.
    /// The TreeItem will only have children if it is a directory.
    /// </summary>
    public List<TreeItem> Children { get; set; } = new();

    /// <summary>
    /// The parent of the TreeItem.
    /// This will be null if the TreeItem is the root directory.
    /// </summary>
    public TreeItem? Parent { get; set; }

    /// <summary>
    /// Will create a TreeItem that represents a file. 
    /// Call <see cref="SetDirectory(List{TreeItem})"/> to make it a directory.
    /// </summary>
    /// <param name="path"></param>
    public TreeItem(string path, TreeItem? parent)
    {
        FilePath = path;
        Parent = parent;
        IsDir = false;
    }

    /// <summary>
    /// Make the TreeItem a directory. 
    /// This must be called if the TreeItem is a directory as TreeItems represent files by default.
    /// </summary>
    /// <param name="children">The children of this TreeItem</param>
    public void SetDirectory(List<TreeItem> children)
    {
        IsDir = true;
        Children = children;
    }

    public void AddChild(TreeItem child)
    {
        if (!IsDir) throw new Exception("Cannot add a child to a file.");
        Children.Add(child);
    }

    public void RemoveChild(TreeItem child)
    {
        if (!IsDir) throw new Exception("Cannot remove a child from a file.");
        Children.Remove(child);
    }
}
