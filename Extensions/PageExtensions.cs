using BoneLib.BoneMenu;

namespace BoneSnap.Extensions;

public static class PageExtensions
{
    public static void RefreshTitle(this Page? page) => page.Refresh();

    /// <summary>
    /// Redraws the page if it's open, as BoneMenu doesn't pick up element/title changes on its own.
    /// </summary>
    public static void Refresh(this Page? page)
    {
        if (BoneLib.BoneMenu.Menu.CurrentPage == page)
            BoneLib.BoneMenu.Menu.OpenPage(page);
    }
}
