using BoneLib.BoneMenu;
using UnityEngine;

namespace BoneSnap.Menu;

/// <summary>
/// Custom BoneMenu components, built on top of the stock elements.
/// </summary>
internal static class Components
{
    /// <summary>
    /// Non-interactive text, for explanations and alike.
    /// </summary>
    public static FunctionElement CreateLabel(this Page page, string text)
    {
        var label = page.CreateFunction(text, Color.gray, null);
        label.SetProperty(ElementProperties.NoBorder);
        return label;
    }
    
    /// <summary>
    /// Styled version of <see cref="CreateLabel"/>
    /// </summary>
    public static FunctionElement CreateWarningLabel(this Page page, string text)
    {
        var label = page.CreateFunction(text, new Color(255, 183, 69), null);
        label.SetProperty(ElementProperties.NoBorder);
        return label;
    }
    
    /// <summary>
    /// Non-interactive text, for headings.
    /// </summary>
    public static FunctionElement CreateHeader(this Page page, string text, Color color=default)
    {
        var label = page.CreateFunction(text, color, null);
        label.SetProperty(ElementProperties.NoBorder);
        return label;
    }
    
    /// <summary>
    /// Just a space. :D
    /// </summary>
    public static FunctionElement CreateSpacer(this Page page)
    {
        var label = page.CreateFunction(" ", Color.gray, null);
        label.SetProperty(ElementProperties.NoBorder);
        return label;
    }

    /// <summary>
    /// A function that cycles through <paramref name="options"/> in order, shown as "Name: Label".
    /// Used over enum elements, as they only show the raw value names.
    /// </summary>
    public static FunctionElement CreateDisplayEnum<T>(this Page page, string name, Color color, T value,
        IReadOnlyList<(T Value, string Label)> options, Action<T> onChanged) where T : struct, Enum
    {
        var current = value;

        FunctionElement? element = null;
        element = page.CreateFunction(GetName(), color, () =>
        {
            // Unknown values restart from the first option
            current = options[(IndexOf(current) + 1) % options.Count].Value;
            element!.ElementName = GetName();
            onChanged(current);
        });

        return element;

        int IndexOf(T target)
        {
            for (var i = 0; i < options.Count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(options[i].Value, target))
                    return i;
            }

            return -1;
        }

        string GetName()
        {
            var index = IndexOf(current);
            return $"{name}: {(index >= 0 ? options[index].Label : current.ToString())}";
        }
    }
}
