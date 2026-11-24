using UnityEngine;

namespace BoneSnap.Assets;

internal static class AssetManager
{
    private const string AudioResourcePrefix = "BoneSnap.Audio.";

    public static AudioClip? CaptureTimerAudio;
    public static AudioClip? CaptureCompleteAudio;
    public static AudioClip? CaptureStartAudio;

    private static void LoadAudio()
    {
        CaptureStartAudio = WavLoader.LoadEmbedded($"{AudioResourcePrefix}capture_start.wav");
        CaptureCompleteAudio = WavLoader.LoadEmbedded($"{AudioResourcePrefix}capture_complete.wav");
        CaptureTimerAudio = WavLoader.LoadEmbedded($"{AudioResourcePrefix}capture_timer.wav");
    }

    public static void Initialize()
    {
        LoadAudio();
    }
}
