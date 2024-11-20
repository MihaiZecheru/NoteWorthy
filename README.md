# NoteWorthy

Lightweight note-taking app for the terminal.

What sets this note-taker apart from others is that this one has a fixed size for the notes. You cannot write more text than can fit in the editor screen.
This is to encourage brevity and to keep the notes short and to the point.

Users should make a folder for each topic and then put notes within that folder.
Then, they can use the Ctrl+UpArrow and Ctrl+DownArrow shortcuts to navigate through their notes in that folder.
This makes finding information easier and faster, and it helps keep the notes organized.
No scrolling in the editor allows you to see the entire note at once and not have to worry about other content being hidden much further down in the note.

**I recommend using the** `One Half Dark` **theme on wt.exe (windows terminal).**

![half dark theme](https://github.com/user-attachments/assets/d74c77bf-27cf-433f-b915-044df174912e)

# Shortcuts

The two most important shortcuts are Ctrl+UpArrow and Ctrl+DownArrow, which are used to navigate through the notes in the current folder.
This works on both the tree and editor. Here are the other shortcuts for both the tree and editor. Press Ctrl+H to see these shortcuts in the app.

## Tree
Ctrl+Q - Quit NoteWorthy
Ctrl+L - Toggle focus to the editor
Ctrl+O - Open current directory in file explorer
Ctrl+UpArrow - Preview the previous note
Ctrl+DownArrow - Preview the next note
Ctrl+W - Close the editor
Ctrl+N - Create new note
Ctrl+M - Create new folder
Ctrl+R - Reload the tree
Ctrl+D - Delete the selected tree item
Ctrl+8 - Open the settings file (+shift to reload it)
Ctrl+1 - Toggle tree visibility
Ctrl+H - Toggle this help panel

UpArrow - Move selection up
DownArrow - Move selection down
Home - Move selection to top of tree
End - Move selection to the bottom of tree
Escape - Go to parent directory
Enter - Open the selected note
Spacebar - Preview the selected note
Tab - Switch focus to the editor
H - Toggle this help panel
F2 - Rename the selected item
N - Create new Note
M - Create new Folder
Delete - Delete the selected tree file/folder
Backspace - Delete the selected tree file/folder

## Editor
Ctrl+Q - Quit NoteWorthy
Ctrl+L - Print dashed-line (if line empty)
Ctrl+S - Save the note
Ctrl+D - Delete current line
Ctrl+W - Close the editor
Ctrl+Z - Undo
Ctrl+Y - Redo
Ctrl+End - Move cursor to the end of note
Ctrl+Home - Move cursor to start of note
Ctrl+LeftArrow - Move to previous word (+shift to select)
Ctrl+RightArrow - Move to next word (+shift to select)
Ctrl+C - Copy selected text
Ctrl+A - Select the entire note
Ctrl+K - Toggle insert mode
Ctrl+Backspace - Delete word
Ctrl+Delete - Delete word (to the right of cursor)
Ctrl+N - Create new note
Ctrl+M - Create new folder
Ctrl+O - Open current directory in file explorer
Ctrl+8 - Open the settings file (+shift to reload it)
Ctrl+UpArrow - Preview the previous note
Ctrl+DownArrow - Preview the next note
Ctrl+B - Toggle primary color (+shift for activating the color for just one char)
Ctrl+U - Toggle secondary color (+shift for activating the color for just one char)
Ctrl+I - Toggle tertiary color (+shift for activating the color for just one char)
Ctrl+1 - Toggle tree visibility
Ctrl+G - Toggle line numbers
Ctrl+H - Toggle this help panel

Escape - Unfocus editor / focus tree
Enter - Insert new line
UpArrow - Move cursor up (+shift to select)
DownArrow - Move cursor down (alias: shift+enter) (+shift to select)
LeftArrow - Move cursor left (+shift to move line)
RightArrow - Move cursor right (+shift to move line)
End - Move cursor to the end of the line
Home - Move cursor to the beginning of the line
Insert - Toggle insert mode
Backspace - Delete character
Delete - Delete character (to the right)
F2 - Rename the current note
Tab - Insert tab

Notes:
A dash and a space on an empty line will auto-indent
If auto-capitalize is on and you don't want the first character to be capitalized, hold shift while typing the first char to make it lowercase