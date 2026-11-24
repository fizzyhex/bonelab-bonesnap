using System.Collections;
using BoneLib.BoneMenu;
using BoneSnap.Personalisation;
using MelonLoader;
using UnityEngine;

namespace BoneSnap.Menu;

/// <summary>
/// A couple handy BoneMenu components, built on top of the stock elements.
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
    
    public static FunctionElement CreateHeader(this Page page, string text, Color color)
    {
        var label = page.CreateFunction($"<color=grey>|</color> {text}", color, null);
        label.SetProperty(ElementProperties.NoBorder);
        return label;
    }
    
    public static FunctionElement CreateHeader(this Page page, string text) => page.CreateHeader(text, Color.white);

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
    /// Creates a button that needs to be pressed twice before the action is called.
    /// </summary>
    /// <param name="onArmed">Invoked when the confirmation period starts.</param>
    /// <param name="onReset">Invoked when the confirmation period ends, before <paramref name="onConfirmed"/> if
    /// confirmed.</param>
    public static FunctionElement CreateConfirmFunction(this Page page, string name, Color color, Action onConfirmed,
        Action? onArmed = null, Action? onReset = null, float timeout = 4f, string confirmText="Confirm?")
    {
        var armed = false;
        var armedVersion = 0;

        FunctionElement? element = null;
        element = page.CreateFunction(name, color, () =>
        {
            if (armed)
            {
                Disarm();
                onConfirmed();
                return;
            }

            armed = true;
            var version = ++armedVersion;
            element!.ElementName = confirmText;
            element.ElementColor = AccentColor.Current;
            onArmed?.Invoke();

            MelonCoroutines.Start(DisarmAfter(version));
        });

        return element;

        void Disarm()
        {
            armed = false;
            armedVersion++;
            element!.ElementName = name;
            element.ElementColor = color;
            onReset?.Invoke();
        }

        IEnumerator DisarmAfter(int version)
        {
            var end = Time.unscaledTime + timeout;

            while (Time.unscaledTime < end)
                yield return null;

            if (armed && version == armedVersion)
                Disarm();
        }
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
