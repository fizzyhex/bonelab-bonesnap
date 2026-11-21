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
    /// <summary>
    /// Marks a UI graphic to follow BoneSnap's accent colour. Without BoneSnap installed, it keeps its authored colour.
    /// </summary>
#if MELONLOADER
    [RegisterTypeInIl2Cpp]
#endif
    public class SnapAccent : MonoBehaviour
    {
        public enum Role
        {
            /// <summary>The accent itself (tracks, knobs).</summary>
            Solid,
            /// <summary>A dark panel with a hint of the accent (slider rails).</summary>
            Surface,
            /// <summary>A large dark panel with an even fainter hint (backdrops).</summary>
            Backdrop,
            /// <summary>A near-white highlight with a hint of the accent (slider handles).</summary>
            Soft,
            /// <summary>Readable on top of the accent: dark ink on light accents, white on dark ones (key icons and labels).</summary>
            Contrast
        }

#if MELONLOADER
        public SnapAccent(IntPtr intPtr) : base(intPtr) { }

        public Il2CppValueField<int>? role;

        private static readonly Color Ink = new(.016f, .02f, .025f);

        private bool _captured;
        private Color _authoredColor;

        [HideFromIl2Cpp]
        internal void Apply(Color accent)
        {
            var graphic = GetComponent<Graphic>();

            if (graphic == null)
                return;

            if (!_captured)
            {
                _captured = true;
                _authoredColor = graphic.color;
            }

            var color = (Role)(role?.Get() ?? 0) switch
            {
                Role.Surface => Color.Lerp(Ink, accent, .08f),
                Role.Backdrop => Color.Lerp(Ink, accent, .04f),
                Role.Soft => Color.Lerp(Color.white, accent, .2f),
                // Where dark ink starts out-contrasting white
                Role.Contrast => Luminance(accent) > .2f ? Color.Lerp(Ink, accent, .15f) : Color.white,
                _ => accent
            };

            color.a = _authoredColor.a;
            graphic.color = color;
        }

        // Relative luminance, from gamma-space colour
        [HideFromIl2Cpp]
        private static float Luminance(Color color) => .2126f * color.linear.r + .7152f * color.linear.g + .0722f * color.linear.b;

        /// <summary>
        /// Puts the authored colour back, e.g. before a pooled camera is reused by someone else.
        /// </summary>
        [HideFromIl2Cpp]
        internal void Restore()
        {
            var graphic = GetComponent<Graphic>();

            if (_captured && graphic != null)
                graphic.color = _authoredColor;
        }
#else
        [Tooltip("How BoneSnap derives this graphic's colour from the player's accent")]
        public Role role;
#endif
    }
}
