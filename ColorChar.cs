namespace NoteWorthy;

internal class ColorChar 
{
    /// <summary>
    /// The char itself
    /// </summary>
    public char Char { get; set; }

    /// <summary>
    /// The color of the char.
    /// If no color is specified, this is null.
    /// </summary>
    public Spectre.Console.Color? Color { get; set; }

    /// <summary>
    /// The byte representing the <see cref="Color"> of the char.
    /// If no color is specified, the color_byte will be 0.
    /// </summary>
    private byte color_byte;

    /// <summary>
    /// True if the character is currently highlighted in the editor
    /// </summary>
    private bool is_highlighted = false;

    /// <summary>
    /// Notes are stored in byte format. The file is read two bytes at a time. The first is the char itself, and the second is the color.
    /// The color is in byte format, so it is converted to a Spectre.Console.Color via the <see cref="ByteToColor(byte)"/> method.
    /// </summary>
    /// <param name="c">Byte representing the character</param>
    /// <param name="color_b">Byte representing the character's color. null for the default color (white 15 #ffffff)</param>
    /// <param name="is_highlighted">True if the character is currently highlighted in the editor</param>
    public ColorChar(byte c, byte color_b, bool is_highlighted = false)
    {
        // Char
        if (c < 0 || c > 127) throw new ArgumentException("Invalid character value. Must be between 0 & 127 inclusive.");
        Char = (char)c;

        // Color
        color_byte = color_b;
        Color = ColorChar.ByteToColor(color_b);

        // Highlight
        this.is_highlighted = is_highlighted;
    }

    public static bool operator == (ColorChar left, char right)
    {
        return left.Char == right;
    }

    public static bool operator != (ColorChar left, char right)
    {
        return left.Char != right;
    }

    /// <summary>
    /// Get the char and color bytes, formatted in a tuple
    /// </summary>
    /// <returns>Tuple of char byte then color_byte</returns>
    public (byte char_byte, byte color_byte) GetBytes()
    {
        return ((byte)Char, color_byte);
    }

    /// <summary>
    /// <see cref="is_highlighted"/> getter
    /// </summary>
    public bool IsHighlighted()
    {
        return is_highlighted;
    }

    /// <summary>
    /// Toggle <see cref="is_highlighted"/>
    /// </summary>
    public void ToggleHighlighting()
    {
        is_highlighted = !is_highlighted;
    }

    /// <summary>
    /// Turn a byte into a color. The color cannot be 0, (which would correspond to black).
    /// 0 is the absence of a specific color, so it's the default terminal color.
    /// Give a value other than 0 to give the char specify a color.
    /// </summary>
    private static Spectre.Console.Color? ByteToColor(byte color)
    {
        // 0 is the no-color-specified option
        if (color == 0) return null;
        return Spectre.Console.Color.FromInt32(color);
    }
}
