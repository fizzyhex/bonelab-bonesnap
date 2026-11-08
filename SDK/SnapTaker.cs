using UnityEngine;
#if MELONLOADER
using MelonLoader;
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
