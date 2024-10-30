namespace NoteWorthy;

/// <summary>
/// A stack that has a maximum size. If the stack is full and a new item is added, the oldest item is removed.
/// </summary>
/// <typeparam name="T"></typeparam>
class LimitedStack<T>
{
    private readonly int maxSize;
    private readonly LinkedList<T> stack;

    public LimitedStack(int maxSize)
    {
        this.maxSize = maxSize;
        stack = new LinkedList<T>();
    }

    public void Push(T item)
    {
        // If the stack is full, remove the oldest item (first in the LinkedList)
        if (stack.Count >= maxSize)
        {
            stack.RemoveFirst();
        }

        stack.AddLast(item); // Add new item to the end (top of the stack)
    }

    public T Pop()
    {
        if (stack.Count == 0)
        {
            throw new InvalidOperationException("The stack is empty.");
        }

        T item = stack.Last.Value;
        stack.RemoveLast(); // Remove the top item
        return item;
    }

    public int Count => stack.Count;

    public bool IsEmpty => stack.Count == 0;
}
