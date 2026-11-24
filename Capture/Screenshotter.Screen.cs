using System.Collections;
using BoneSnap.SDK;
using MelonLoader;
using UnityEngine;

namespace BoneSnap.Capture;

/// <summary>
/// Captures the window output.
/// </summary>
internal partial class Screenshotter
{
    public void PrepareScreenCapture(float waitTime, CaptureParams captureParams)
    {
        SnapHUD.GetInstance().SwapState(new CountdownState(waitTime, previewScreen: true));
        StartCountdown(waitTime, captureParams, CaptureScreen);
    }

    /// <summary>
    /// Copies the game window from a frame rendered with the capture hooks applied.
    /// </summary>
    /// <remarks>
    /// Screen output ignores <see cref="CaptureParams.Width"/> and <see cref="CaptureParams.Height"/>.
    /// </remarks>
    private static IEnumerator CaptureScreen(CaptureParams captureParams, CaptureResult result)
    {
        MelonLogger.Msg("Capturing...");

        var captureHooks = new CaptureHooks.CaptureHooks();
        var screenTexture = ScreenCopy.CreateScreenTexture();

        try
        {
            captureHooks.PreCapture(captureParams);
            
            // Copy once this frame has rendered with the hooks applied. Copying from Update would read the
            // previous frame (which shows the SnapHUD).
            yield return new WaitForEndOfFrame();

            ScreenCopy.RequestInto(screenTexture);

            var uprightTexture = RenderTexture.GetTemporary(screenTexture.width, screenTexture.height, 0);
            Texture2D screenshotTexture;

            try
            {
                ScreenCopy.BlitWindowCopy(screenTexture, uprightTexture);
                screenshotTexture = ReadPixels(uprightTexture);
                SnapHUD.GetInstance().FreezePreview(uprightTexture);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(uprightTexture);
            }

            result.Captures = [ReadRawCapture(screenshotTexture, captureParams.Quality)];
            UnityEngine.Object.Destroy(screenshotTexture);

            // Clipboard support is disabled because MelonLoader no longer
            // bundles System.Windows.Forms :(

            //Clipboard.SetImage(
            //    (Bitmap)((new ImageConverter()).ConvertFrom(bytes))
            //);
        }
        finally
        {
            captureHooks.PostCapture(captureParams);
            UnityEngine.Object.Destroy(screenTexture);
        }
    }
}
