using UnityEngine;
#if MELONLOADER
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Fields;
using Il2CppTMPro;
using MelonLoader;
#else
using TMPro;
#endif

namespace BoneSnap.SDK
{
#if MELONLOADER
    [RegisterTypeInIl2Cpp]
#endif
    public class SnapTaker : MonoBehaviour
    {
#if MELONLOADER
        public SnapTaker(IntPtr intPtr) : base(intPtr) { }
        
        private static readonly HashSet<SnapTaker> Active = new();
        public Il2CppReferenceField<TMP_Text>? resolutionDisplay;

        public void OnEnable()
        {
            Active.Add(this);
            RefreshResolutionDisplay();
        }

        public void OnDisable()
        {
            Active.Remove(this);
        }

        /// <summary>
        /// Updates every enabled snap taker's <see cref="resolutionDisplay"/> to match the current preferences.
        /// </summary>
        [HideFromIl2Cpp]
        internal static void RefreshAllResolutionDisplays()
        {
            foreach (var snapTaker in Active)
                snapTaker.RefreshResolutionDisplay();
        }

        [HideFromIl2Cpp]
        private void RefreshResolutionDisplay()
        {
            var display = resolutionDisplay?.Get();

            if (display == null)
                return;

            var (width, height) = CaptureParams.GetResolution(Main.Prefs);
            display.text = $"{width}x{height}";
        }
#else
        [Tooltip("Optional - Text Mesh to display BoneSnap's camera resolution on")]
        public TMP_Text resolutionDisplay;
#endif
        /// <summary>
        /// Takes a snap through the provided camera - using its FOV, transforms, and other related settings;
        /// but using BoneSnap's configurations (such as Render Scale / Capture Quality).
        /// </summary>
        public void Snap(Camera camera)
        {
#if MELONLOADER
            if (camera == null)
            {
                MelonLogger.Warning($"{nameof(SnapTaker)} on '{gameObject.name}' failed as it provided a null Camera");
                return;
            }

            Main.Screenshotter.TrySdkCapture(camera, Main.Prefs);
#endif
        }
    }
}
