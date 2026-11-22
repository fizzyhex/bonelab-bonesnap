using System.Collections;
using BoneSnap.SDK;
using MelonLoader;

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
            
            yield return null; // Wait a frame while hooks apply before copying the texture, otherwise our
                               // copy may be a frame behind (and show the SnapHUD).
            
            ScreenCopy.RequestInto(screenTexture);

            var screenshotTexture = ReadPixels(screenTexture);
            SnapHUD.GetInstance().FreezePreview(screenTexture);

            result.Capture = ReadRawCapture(screenshotTexture, captureParams.Quality);
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
