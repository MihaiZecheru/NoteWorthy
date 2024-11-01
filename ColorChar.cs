using Spectre.Console;
using System.ComponentModel;
using System;

namespace NoteWorthy;

internal class ColorChar 
{
    /// <summary>
    /// The char itself
    /// </summary>
    public char Char { get; set; }

    /// <summary>
    /// The color of the char. Leave null to use whatever color is currently set.
    /// </summary>
    public Spectre.Console.Color? Color { get; set; }

    /// <summary>
    /// The byte representing the <see cref="Color"> of the char
    /// </summary>
    private byte color_byte;

    /// <summary>
    /// Notes are stored in byte format. The file is read two bytes at a time. The first is the char itself, and the second is the color.
    /// The color is in byte format, so it is converted to a Spectre.Console.Color via the <see cref="ByteToColor(byte)"/> method.
    /// </summary>
    /// <param name="c">Byte representing the character</param>
    /// <param name="color">Byte representing the character's color</param>
    public ColorChar(byte c, byte color)
    {
        // Char
        if (c < 0 || c > 127) throw new ArgumentException("Invalid character value. Must be between 0 & 127 inclusive.");
        Char = (char)c;

        // Color
        color_byte = color;
        Color = ColorChar.ByteToColor(color);
    }

    /// <param name="c">Character</param>
    /// <param name="color">String representation of the color.
    /// Must be one of: blue | red | green | yellow | purple | light_purple | cyan | white | black | gray | orange</param>
    public ColorChar(byte c, string? color)
    {
        // Char
        if (c < 0 || c > 127) throw new ArgumentException("Invalid character value. Must be between 0 & 127 inclusive.");
        Char = (char)c;

        // Color
        Color = color switch
        {
            null => null,
            "blue" => Spectre.Console.Color.Blue,
            "red" => Spectre.Console.Color.Red,
            "green" => Spectre.Console.Color.Green,
            "yellow" => Spectre.Console.Color.Yellow,
            "purple" => Spectre.Console.Color.Purple,
            "light_purple" => Spectre.Console.Color.Plum2,
            "cyan" => Spectre.Console.Color.Cyan1,
            "white" => Spectre.Console.Color.White,
            "black" => Spectre.Console.Color.Black,
            "gray" => Spectre.Console.Color.Grey42,
            "orange" => Spectre.Console.Color.Orange1,
            _ => throw new ArgumentException("Invalid color value"),
        };
        color_byte = ColorChar.ColorToByte(Color);
    }

    /// <param name="c">Character</param>
    /// <param name="color">Color of the character</param>
    /// <exception cref="ArgumentException"></exception>
    public ColorChar(byte c, Spectre.Console.Color? color)
    {
        // Char
        if (c < 0 || c > 127) throw new ArgumentException("Invalid character value. Must be between 0 & 127 inclusive.");
        Char = (char)c;

        // Color
        Color = color;
        color_byte = ColorChar.ColorToByte(color);
    }

    public static bool operator ==(ColorChar left, char right)
    {
        return left.Char == right;
    }

    public static bool operator !=(ColorChar left, char right)
    {
        return left.Char != right;
    }

    /// <summary>
    /// Get the char and color bytes, formatted in a tuple
    /// </summary>
    /// <returns>Tuple of char byte then color_byte</returns>
    public (byte, byte) GetBytes()
    {
        return ((byte)Char, color_byte);
    }

    private static Spectre.Console.Color? ByteToColor(byte color)
    {
        return color switch
        {
            0 => null,
            1 => Spectre.Console.Color.Black,
            2 => Spectre.Console.Color.White,
            3 => Spectre.Console.Color.Red,
            4 => Spectre.Console.Color.Green,
            5 => Spectre.Console.Color.Yellow,
            6 => Spectre.Console.Color.Blue,
            7 => Spectre.Console.Color.Purple,
            8 => Spectre.Console.Color.Plum2,
            9 => Spectre.Console.Color.Grey42,
            10 => Spectre.Console.Color.Orange1,
            11 => Spectre.Console.Color.Cyan1,
            _ => throw new ArgumentException("Invalid color value"),
        };
    }

    private static byte ColorToByte(Spectre.Console.Color? color)
    {
        // Had to use if statements because switch wasn't working.
        if (color == null) return 0;
        else if (color == Spectre.Console.Color.Black) return 1;
        else if (color == Spectre.Console.Color.White) return 2;
        else if (color == Spectre.Console.Color.Red) return 3;
        else if (color == Spectre.Console.Color.Green) return 4;
        else if (color == Spectre.Console.Color.Yellow) return 5;
        else if (color == Spectre.Console.Color.Blue) return 6;
        else if (color == Spectre.Console.Color.Purple) return 7;
        else if (color == Spectre.Console.Color.Plum2) return 8;
        else if (color == Spectre.Console.Color.Grey42) return 9;
        else if (color == Spectre.Console.Color.Orange1) return 10;
        else if (color == Spectre.Console.Color.Cyan1) return 11;
        else throw new ArgumentException("Invalid color value");
    }
}
