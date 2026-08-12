using System.Runtime.InteropServices;
using QuickSearch.Core;

namespace QuickSearch.Windows;

internal sealed partial class EverythingNativeAdapter : IEverythingNative
{
    public bool IsDatabaseLoaded() => EverythingIsDatabaseLoaded();

    public void SetSearch(string search) => EverythingSetSearch(search);

    public void SetRequestFlags(uint flags) => EverythingSetRequestFlags(flags);

    public void SetMax(uint maximumResults) => EverythingSetMax(maximumResults);

    public bool Query(bool wait) => EverythingQuery(wait);

    public uint GetNumResults() => EverythingGetNumResults();

    public string? GetResultFileName(uint index) =>
        Marshal.PtrToStringUni(EverythingGetResultFileName(index));

    public string? GetResultPath(uint index) =>
        Marshal.PtrToStringUni(EverythingGetResultPath(index));

    public bool IsFolderResult(uint index) => EverythingIsFolderResult(index);

    public uint GetLastError() => EverythingGetLastError();

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_SetSearchW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial void EverythingSetSearch(string search);

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_SetRequestFlags")]
    private static partial void EverythingSetRequestFlags(uint flags);

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_SetMax")]
    private static partial void EverythingSetMax(uint maximumResults);

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_QueryW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EverythingQuery([MarshalAs(UnmanagedType.Bool)] bool wait);

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_GetNumResults")]
    private static partial uint EverythingGetNumResults();

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_GetResultFileNameW")]
    private static partial nint EverythingGetResultFileName(uint index);

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_GetResultPathW")]
    private static partial nint EverythingGetResultPath(uint index);

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_IsFolderResult")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EverythingIsFolderResult(uint index);

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_IsDBLoaded")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EverythingIsDatabaseLoaded();

    [LibraryImport("Everything64.dll", EntryPoint = "Everything_GetLastError")]
    private static partial uint EverythingGetLastError();
}
