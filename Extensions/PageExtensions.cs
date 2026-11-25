using BoneLib.BoneMenu;

namespace BoneSnap.Extensions;

public static class PageExtensions
{
    public static void RefreshTitle(this Page? page) => page.Refresh();

    public static void Refresh(this Page? page)
    {
        if (BoneLib.BoneMenu.Menu.CurrentPage == page)
            BoneLib.BoneMenu.Menu.OpenPage(page);
    }
}
