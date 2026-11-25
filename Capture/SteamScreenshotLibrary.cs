using System.Collections;
using System.Runtime.CompilerServices;
using BoneSnap.Multiplayer;
using HarmonyLib;
using LabFusion.Data;
using LabFusion.Network;
using MelonLoader;
using Steamworks;

namespace BoneSnap.Capture;

/// <summary>
/// Adds captures to the player's Steam screenshot library using Fusion's Steamworks wrapper.
/// </summary>
internal static class SteamScreenshotLibrary
{
    public static bool Add(string path, int width, int height)
    {
        if (!FusionSync.IsAvailable)
        {
            MelonLogger.Warning("'Save to Steam' needs Fusion installed, skipping.");
            return false;
        }

        try
        {
            return FusionSteamBridge.Add(path, width, height);
        }
        catch (Exception e)
        {
            MelonLogger.Error($"Save to Steam failed: {e}");
            return false;
        }
    }
}

internal static class FusionSteamBridge
{
    private const uint BonelabAppId = 1592190;

    private static bool _ownsSession;
    private static bool _installed;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Add(string path, int width, int height)
    {
        if (!SteamAPILoader.HasSteamAPI)
        {
            MelonLogger.Warning("Fusion couldn't load steam_api64.dll, skipping Save to Steam.");
            return false;
        }

        Install();

        if (!SteamClient.IsValid && !TryOpenSession())
            return false;

        if (SteamScreenshots.AddScreenshot(path, null, width, height) != null)
            return true;

        MelonLogger.Warning($"Steam refused the screenshot: {path}");
        return false;
    }

    private static void Install()
    {
        if (_installed)
            return;

        _installed = true;

        SteamScreenshots.OnScreenshotFailed += result =>
            MelonLogger.Warning($"Steam failed to save the screenshot ({result}).");

        var logIn = AccessTools.Method(typeof(SteamNetworkLayer), nameof(SteamNetworkLayer.LogIn));
        new HarmonyLib.Harmony("BoneSnap.SteamScreenshots").Patch(logIn,
            prefix: new HarmonyMethod(typeof(FusionSteamBridge), nameof(BeforeFusionLogIn)));
    }

    private static bool TryOpenSession()
    {
        try
        {
            SteamClient.Init(BonelabAppId, asyncCallbacks: false);

            // shutting down causes game crashes due to Steam's own threads, so we have to hold a reference
            if (!System.Runtime.InteropServices.NativeLibrary.TryLoad("steamclient64.dll", out _))
                MelonLogger.Warning("Couldn't pin steamclient64.dll");
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"Couldn't initialise Steamworks, skipping Save to Steam. Is Steam running? ({e.Message})");
            return false;
        }

        _ownsSession = true;
        MelonCoroutines.Start(KeepAlive());
        return true;
    }

    private static IEnumerator KeepAlive()
    {
        while (_ownsSession && SteamClient.IsValid)
        {
            SteamClient.RunCallbacks();
            yield return null;
        }
    }

    private static void BeforeFusionLogIn()
    {
        if (!_ownsSession)
            return;

        _ownsSession = false;
        SteamClient.Shutdown();
    }
}
