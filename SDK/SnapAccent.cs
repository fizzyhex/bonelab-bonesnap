#nullable enable
using UnityEngine;
using UnityEngine.UI;
#if MELONLOADER
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Fields;
using MelonLoader;
#endif

namespace BoneSnap.SDK
{
#if MELONLOADER
    [RegisterTypeInIl2Cpp]
#endif
    public class SnapAccent : MonoBehaviour
    {
        public enum Role
        {
            Solid,
            Surface,
            Backdrop,
            Soft,
            Contrast
        }

#if MELONLOADER
        public SnapAccent(IntPtr intPtr) : base(intPtr) { }

        public Il2CppValueField<int>? role;

        private static readonly Color Dark = new(.016f, .02f, .025f);

        private bool _captured;
        private Color _graphicColor;

        [HideFromIl2Cpp]
        internal void Apply(Color accent)
        {
            var graphic = GetComponent<Graphic>();

            if (graphic == null)
                return;

            if (!_captured)
            {
                _captured = true;
                _graphicColor = graphic.color;
            }

            var color = (Role)(role?.Get() ?? 0) switch
            {
                Role.Surface => Color.Lerp(Dark, accent, .08f),
                Role.Backdrop => Color.Lerp(Dark, accent, .04f),
                Role.Soft => Color.Lerp(Color.white, accent, .2f),
                Role.Contrast => Luminance(accent) > .2f ? Color.Lerp(Dark, accent, .15f) : Color.white,
                _ => accent
            };

            color.a = _graphicColor.a;
            graphic.color = color;
        }

        [HideFromIl2Cpp]
        private static float Luminance(Color color) => .2126f * color.linear.r + .7152f * color.linear.g + .0722f * color.linear.b;

        [HideFromIl2Cpp]
        internal void Restore()
        {
            var graphic = GetComponent<Graphic>();

            if (_captured && graphic != null)
                graphic.color = _graphicColor;
        }
#else
        [Tooltip("Controls which shade of the user's colour preference to use")]
        public Role role;
#endif
    }
}
