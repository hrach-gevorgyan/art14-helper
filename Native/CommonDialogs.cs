using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>Message boxes and the Explorer open/save dialogs.</summary>
internal static unsafe partial class CommonDialogs
{
    internal enum Icon : uint
    {
        Error = 0x10,
        Warning = 0x30,
        Information = 0x40
    }

    /// <summary>A message box; with <paramref name="yesNo"/> returns true for Yes, otherwise true when closed.</summary>
    internal static bool Message(IntPtr owner, string text, string title, Icon icon, bool yesNo = false)
        => MessageBoxW(owner, text, title, (uint)icon | (yesNo ? 0x4u : 0u) | 0x10000) == (yesNo ? 6 : 1);   // MB_YESNO, MB_SETFOREGROUND, IDYES / IDOK

    /// <summary>OPENFILENAMEW</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FileQuestion
    {
        public uint Size;
        public IntPtr Owner, Instance;
        public char* Filter, CustomFilter;
        public uint CustomFilterLength, FilterIndex;
        public char* File;
        public uint FileLength;
        public char* FileTitle;
        public uint FileTitleLength;
        public char* InitialFolder, Title;
        public uint Flags;
        public ushort FileOffset, ExtensionOffset;
        public char* DefaultExtension;
        public nint CustomData;
        public IntPtr Hook;
        public char* Template;
        public IntPtr Reserved;
        public uint ReservedNumber, FlagsEx;
    }

    private const uint OverwritePrompt = 0x2, HideReadOnly = 0x4, KeepFolder = 0x8, PathMustExist = 0x800,
        FileMustExist = 0x1000, Explorer = 0x80000;   // OFN_*

    /// <summary>
    /// The open or save dialog. <paramref name="filter"/> pairs a description with its
    /// patterns ("Zip archive (*.zip)|*.zip|…"). Returns the chosen path, or null.
    /// </summary>
    internal static string? AskForFile(IntPtr owner, bool save, string title, string filter, string? suggested = null, string? extension = null)
    {
        var buffer = new char[1024];
        suggested?.AsSpan(0, Math.Min(suggested.Length, buffer.Length - 1)).CopyTo(buffer);
        var pairs = filter.Replace('|', '\0') + "\0\0";
        fixed (char* file = buffer)
        fixed (char* filters = pairs)
        fixed (char* caption = title)
        fixed (char* defaultExtension = extension)
        {
            var question = new FileQuestion
            {
                Size = (uint)sizeof(FileQuestion),
                Owner = owner,
                Filter = filters,
                FilterIndex = 1,
                File = file,
                FileLength = (uint)buffer.Length,
                Title = caption,
                DefaultExtension = defaultExtension,
                Flags = Explorer | KeepFolder | HideReadOnly | PathMustExist | (save ? OverwritePrompt : FileMustExist)
            };
            var chosen = save ? GetSaveFileNameW(&question) : GetOpenFileNameW(&question);
            return chosen != 0 ? new string(file) : null;
        }
    }

    [LibraryImport(Libraries.User32, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr owner, string text, string title, uint type);

    [LibraryImport(Libraries.Comdlg32)]
    private static partial int GetOpenFileNameW(FileQuestion* question);

    [LibraryImport(Libraries.Comdlg32)]
    private static partial int GetSaveFileNameW(FileQuestion* question);
}
