namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal static class RecordedSessionPageSelection
{
    public static int ClampSelectedPageIndex(int pageIndex, int pageCount)
    {
        if (pageCount <= 0)
        {
            return 0;
        }

        if (pageIndex < 0)
        {
            return 0;
        }

        return pageIndex >= pageCount
            ? pageCount - 1
            : pageIndex;
    }
}
