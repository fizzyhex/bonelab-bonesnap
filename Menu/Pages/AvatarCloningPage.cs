using BoneLib.BoneMenu;
using BoneSnap.Cloning;
using BoneSnap.Extensions;
using BoneSnap.Multiplayer;
using BoneSnap.Personalisation;
using UnityEngine;

namespace BoneSnap.Menu.Pages;

internal static class AvatarCloningPage
{
    private static Action? _render;
    private static Page? _manageClonesPage;
    private static int _focusedClone = -1;

    public static void Render(Page cloningPage, BoneSnapPreferences preferences)
    {
        cloningPage.RemoveAll();

        cloningPage.CreateFunction("Clone Avatar", AccentColor.Current, () =>
            AvatarClones.CloneLocalAfterDelay(preferences.CloneDelay.Value, preferences.CloneIncludeHeldItems.Value));

        var delay = cloningPage.CreateFloat("Clone Delay", Color.white, preferences.CloneDelay.Value, .5f, 0f, 60f, value =>
        {
            preferences.CloneDelay.Value = value;
            preferences.ManualSave();
        });

        cloningPage.CreateBool("Include Held Items", Color.white, preferences.CloneIncludeHeldItems.Value, value =>
        {
            preferences.CloneIncludeHeldItems.Value = value;
            preferences.ManualSave();
        });

        if (FusionSync.IsAvailable)
        {
            var fusionSync = cloningPage.CreateBool("Fusion Sync", Color.white, AvatarClones.FusionSyncEnabled,
                value => AvatarClones.FusionSyncEnabled = value);

            fusionSync.SetTooltip("Allows other players in the lobby to see your clones. " +
                                  "This needs to be re-enabled each time you launch the game.");
        }

        var clones = AvatarClones.Local;

        if (clones.Count == 0)
        {
            if (_manageClonesPage != null && BoneLib.BoneMenu.Menu.CurrentPage == _manageClonesPage)
                BoneLib.BoneMenu.Menu.OpenPage(cloningPage);
            else
                cloningPage.Refresh();

            return;
        }

        cloningPage.Refresh();

        var managePage = _manageClonesPage = BoneMenu.GetOrCreatePage(cloningPage, "Manage Clones", $"Manage Clones ({clones.Count})");
        managePage.RemoveAll();

        managePage.CreateConfirmFunction("Delete All", Color.red, AvatarClones.DeleteAll,
            confirmText: "Confirm?");

        if (clones.Count > 8)
        {
            // Use subpages to reduce stutter when BoneMenu refreshes a page with many
            // entries.
            RenderSubPageCloneEntries();
        }
        else
        {
            RenderInlineCloneEntries();
        }

        return;

        void RenderSubPageCloneEntries()
        {
            var clonePage = managePage.CreatePage("Some Clone", Color.white, 0, false);

            for (var index = 0; index < clones.Count; index++)
            {
                var cloneIndex = index;
                var cloneName = $"Clone {index + 1}";

                managePage.CreateFunction($"{cloneName} {BoneMenu.PageGlyph}", Color.white, () =>
                {
                    clonePage.RemoveAll();
                    RenderCloneOptions(clonePage, clones[cloneIndex]);
                    clonePage.Name = cloneName;
                    BoneLib.BoneMenu.Menu.OpenPage(clonePage);
                });
            }
        }

        void RenderInlineCloneEntries()
        {
            var isFocusSet = _focusedClone != -1;

            for (var index = 0; index < clones.Count; index++)
            {
                var isFocused = _focusedClone == index;
                var clone = clones[index];

                var cloneIndex = index;
                managePage.CreateFunction($"Clone {index + 1}", isFocused ? AccentColor.Current : isFocusSet ? Color.gray : Color.white, () =>
                {
                    if (isFocused)
                    {
                        _focusedClone = -1;
                        CloneHighlighter.Hide(clone);
                    }
                    else
                    {
                        CloneHighlighter.Show(clone);
                        _focusedClone = cloneIndex;
                    }

                    Render(cloningPage, preferences);
                    cloningPage.Refresh();
                });

                if (isFocused)
                {
                    RenderCloneOptions(managePage, clone);
                }
            }

            managePage.Refresh();
        }
    }

    private static void RenderCloneOptions(Page cloneOptionsPage, AvatarClone clone)
    {
        cloneOptionsPage.CreateConfirmFunction($"Delete", Color.white,
            onConfirmed: () =>
            {
                _focusedClone = -1;
                AvatarClones.Delete(clone);
            },
            confirmText: "Confirm?");

        if (clone.HoldingItems)
        {
            cloneOptionsPage.CreateDisplayEnum("Visibility", Color.white, clone.Visibility,
                [
                    (AvatarVisibility.Show, "Avatar & Items"),
                    (AvatarVisibility.ItemsOnly, "Items"),
                    (AvatarVisibility.Hide, "Hidden")
                ],
                visibility =>
                {
                    AvatarClones.SetVisibility(clone, visibility);
                });
        }
        else
        {
            cloneOptionsPage.CreateFunction(clone.Visibility == AvatarVisibility.Show ? "Hide" : "Show", Color.white, () =>
            {
                AvatarClones.SetVisibility(clone, clone.Visibility == AvatarVisibility.Show ? AvatarVisibility.Hide : AvatarVisibility.Show);
            });
        }
    }

    /// <summary>
    /// Keeps the clone count and Manage Clones page in sync as clones are created/deleted.
    /// </summary>
    public static void Watch(Action render)
    {
        var subscribe = _render == null;
        _render = render;

        if (subscribe)
            AvatarClones.Changed += () => _render?.Invoke();
    }
}
