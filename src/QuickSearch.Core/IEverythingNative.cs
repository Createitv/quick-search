namespace QuickSearch.Core;

public interface IEverythingNative
{
    bool IsDatabaseLoaded();

    void SetSearch(string search);

    void SetRequestFlags(uint flags);

    void SetMax(uint maximumResults);

    bool Query(bool wait);

    uint GetNumResults();

    string? GetResultFileName(uint index);

    string? GetResultPath(uint index);

    bool IsFolderResult(uint index);

    uint GetLastError();
}
