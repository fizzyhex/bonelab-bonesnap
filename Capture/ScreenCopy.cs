using UnityEngine;

namespace BoneSnap.Capture;

/// <summary>
/// Copies the game window into render textures. Used by screen captures and the screen preview.
/// </summary>
internal static class ScreenCopy
{
    public static void RequestInto(RenderTexture target) => ScreenCapture.CaptureScreenshotIntoRenderTexture(target);

    public static RenderTexture CreateScreenTexture() => new(Screen.width, Screen.height, 0);

    public static bool IsScreenSized(RenderTexture texture) => texture.width == Screen.width && texture.height == Screen.height;

    /// <summary>
    /// Blits a window copy into <paramref name="destination"/> upright.
    /// </summary>
    /// <remarks>
    /// The graphics UV conventions vary per platform. Quest uses Vulkan instead of D3D, for example.
    /// See https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/systeminfo/graphicsuvstartsattop
    /// </remarks>
    public static void BlitUpright(Texture source, RenderTexture destination)
    {
        if (SystemInfo.graphicsUVStartsAtTop)
            Graphics.Blit(source, destination, new Vector2(1, -1), new Vector2(0, 1));
        else
            Graphics.Blit(source, destination);
    }
}
