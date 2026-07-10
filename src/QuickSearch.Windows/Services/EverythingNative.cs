using System.Runtime.InteropServices;

namespace QuickSearch.Windows;

internal static partial class EverythingNative
{
    internal const uint RequestFileName = 0x00000001;
    internal const uint RequestPath = 0x00000002;

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_SetSearchW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial void SetSearch(string search);

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_SetRequestFlags")]
    internal static partial void SetRequestFlags(uint flags);

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_SetMax")]
    internal static partial void SetMax(uint maximumResults);

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_QueryW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Query([MarshalAs(UnmanagedType.Bool)] bool wait);

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_GetNumResults")]
    internal static partial uint GetNumResults();

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_GetResultFileNameW")]
    internal static partial nint GetResultFileName(uint index);

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_GetResultPathW")]
    internal static partial nint GetResultPath(uint index);

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_IsDBLoaded")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsDatabaseLoaded();

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_GetLastError")]
    internal static partial uint GetLastError();
}
