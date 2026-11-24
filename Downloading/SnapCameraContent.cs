using System.Collections;
using System.IO.Compression;
using BoneLib.Notifications;
using BoneSnap.Utilities;
using Il2CppSLZ.Marrow.Forklift;
using Il2CppSLZ.Marrow.Forklift.Model;
using Il2CppSLZ.Marrow.Warehouse;
using MelonLoader;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BoneSnap.Downloading;

/// <summary>
/// Downloads the Snap Camera pallet from mod.io.
/// Mirrors how Fusion installs Fusion Content (ModIODownloader -> ModDownloadManager -> ModForklift)
/// </summary>
internal static class SnapCameraContent
{
    public const string PalletBarcode = "Fizzyhex.SnapCamera";
    public const int ModId = 6550170;

    private const string ApiPath = "https://api.mod.io/v1/games/";
    private const int GameId = 3809; // BONELAB

    private static string StagingPath => Application.persistentDataPath + "/BoneSnapStaging";
    private static string ModsPath => Application.persistentDataPath + "/Mods";

    public static bool IsDownloading { get; private set; }

    public static float Progress { get; private set; }

    /// <summary>
    /// Fired when a download starts, progresses, or ends.
    /// </summary>
    public static event Action? Changed;

    public static bool IsInstalled
    {
        get
        {
            var warehouse = AssetWarehouse.Instance;
            return warehouse != null && warehouse.HasPallet(new Barcode(PalletBarcode));
        }
    }

    public static void Download()
    {
        if (IsDownloading)
            return;

        IsDownloading = true;
        Progress = 0f;
        Changed?.Invoke();

        Notify("Downloading Snap Camera", "Downloading the Snap Camera from mod.io...", NotificationType.Information);
        MelonCoroutines.Start(CoDownload());
    }

    private static IEnumerator CoDownload()
    {
        string? token = null;
        yield return CoLoadToken(value => token = value);

        if (string.IsNullOrWhiteSpace(token))
        {
            Fail("Make sure you are logged into mod.io in Void G114 or BONELAB Hub!");
            yield break;
        }

        using var client = CreateClient(token!);

        // Request live file from mod.io
        var modTask = client.GetStringAsync($"{ApiPath}{GameId}/mods/{ModId}");

        while (!modTask.IsCompleted)
            yield return null;

        if (!modTask.IsCompletedSuccessfully)
        {
            MelonLogger.Error($"Failed getting Snap Camera mod data: {modTask.Exception}");
            Fail("Couldn't reach mod.io.");
            yield break;
        }

        var fileId = GetLiveFile(JObject.Parse(modTask.Result));

        if (!fileId.HasValue)
        {
            Fail($"The Snap Camera has no {GetPlatform()} download.");
            yield break;
        }

        Directory.CreateDirectory(StagingPath);
        var zipPath = $"{StagingPath}/m{ModId}f{fileId}.zip";

        var responseTask = client.GetAsync($"{ApiPath}{GameId}/mods/{ModId}/files/{fileId}/download", HttpCompletionOption.ResponseHeadersRead);

        while (!responseTask.IsCompleted)
            yield return null;

        if (!responseTask.IsCompletedSuccessfully || !responseTask.Result.IsSuccessStatusCode)
        {
            MelonLogger.Error($"Failed downloading the Snap Camera: {responseTask.Exception?.ToString() ?? responseTask.Result.StatusCode.ToString()}");
            Fail("The download failed.");
            yield break;
        }

        var content = responseTask.Result.Content;
        var length = content.Headers.ContentLength ?? 0;

        // Close the file before extracting it
        using (var stream = new FileStream(zipPath, FileMode.Create))
        {
            var copyTask = content.CopyToAsync(stream);

            while (!copyTask.IsCompleted)
            {
                if (length > 0)
                    SetProgress((float)stream.Length / length);

                yield return null;
            }

            if (!copyTask.IsCompletedSuccessfully)
            {
                MelonLogger.Error($"Failed writing the Snap Camera zip: {copyTask.Exception}");
                Fail("The download failed.");
                yield break;
            }
        }

        SetProgress(1f);

        string? palletJson = null;
        yield return CoInstall(zipPath, path => palletJson = path);
        File.Delete(zipPath);

        if (string.IsNullOrEmpty(palletJson))
        {
            Fail("The downloaded mod didn't contain a pallet.");
            yield break;
        }

        while (!AssetWarehouse.ready)
            yield return null;

        var loadTask = AssetWarehouse.Instance.LoadPalletFromFolderAsync(palletJson, true, null, CreateListing(fileId.Value));
        var loaded = false;
        loadTask.GetAwaiter().OnCompleted((Action)(() => loaded = true));

        while (!loaded)
            yield return null;

        IsDownloading = false;
        Changed?.Invoke();

        Notify("Snap Camera Installed", "Find it in your Spawn Gun!", NotificationType.Success);
    }

    /// <summary>
    /// Extracts the ZIP, moves the pallet into Mods, and passes
    /// the installed pallet.json path to the callback, or null if
    /// extraction fails or the pallet cannot be found.
    /// </summary>
    private static IEnumerator CoInstall(string zipPath, Action<string?> callback)
    {
        var extracted = $"{StagingPath}/{Path.GetFileNameWithoutExtension(zipPath)}";

        if (Directory.Exists(extracted))
            Directory.Delete(extracted, true);

        var unzipTask = Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extracted, true));

        while (!unzipTask.IsCompleted)
            yield return null;

        if (!unzipTask.IsCompletedSuccessfully)
        {
            MelonLogger.Error($"Failed extracting the Snap Camera zip: {unzipTask.Exception}");
            callback(null);
            yield break;
        }

        var palletJson = FindPalletJson(extracted);
        var palletDirectory = palletJson != null ? Path.GetDirectoryName(palletJson) : null;

        if (palletDirectory == null)
        {
            Directory.Delete(extracted, true);
            callback(null);
            yield break;
        }

        var destination = $"{ModsPath}/{new DirectoryInfo(palletDirectory).Name}";

        if (Directory.Exists(destination))
            Directory.Delete(destination, true);

        Directory.Move(palletDirectory, destination);
        Directory.Delete(extracted, true);

        callback(FindPalletJson(destination));
    }

    private static IEnumerator CoLoadToken(Action<string?> callback)
    {
        var settingsPath = ModDownloader.ModSettingsPath;

        if (!File.Exists(settingsPath))
        {
            callback(null);
            yield break;
        }

        var readTask = File.ReadAllTextAsync(settingsPath);

        while (!readTask.IsCompleted)
            yield return null;

        if (!readTask.IsCompletedSuccessfully)
        {
            callback(null);
            yield break;
        }

        callback(JObject.Parse(readTask.Result)["mod.io.access_token"]?.ToString());
    }

    private static HttpClient CreateClient(string token)
    {
        var client = new HttpClient(new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });

        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        // mod.io 404s every request that doesn't send a User-Agent
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{Main.Name}/{Main.Version}");
        return client;
    }

    private static int? GetLiveFile(JObject mod)
    {
        var platform = GetPlatform();

        if (mod["platforms"] is not JArray platforms)
            return null;

        foreach (var entry in platforms)
        {
            if (entry["platform"]?.ToString() == platform)
                return entry["modfile_live"]?.ToObject<int>();
        }

        return null;
    }

    private static string GetPlatform() => PlatformHelper.IsAndroid ? "android" : "windows";

    /// <summary>
    /// Records the pallet's mod.io source so the game can update it.
    /// </summary>
    private static ModListing CreateListing(int fileId)
    {
        var targets = new StringModTargetListingDictionary();

        targets.Add(GetPlatform(), new ModIOModTarget
        {
            GameId = GameId,
            ModId = ModId,
            ModfileId = fileId
        });

        return new ModListing
        {
            Author = null,
            Barcode = null,
            Description = null,
            Repository = null,
            Targets = targets
        };
    }

    private static string? FindPalletJson(string directory)
    {
        var files = Directory.GetFiles(directory, "*.pallet.json", SearchOption.AllDirectories);
        return files.Length > 0 ? files[0] : null;
    }

    private static void SetProgress(float progress)
    {
        Progress = progress;
        Changed?.Invoke();
    }

    private static void Fail(string reason)
    {
        IsDownloading = false;
        Changed?.Invoke();

        Notify("Snap Camera Download Failed", reason, NotificationType.Error);
    }

    private static void Notify(string title, string message, NotificationType type)
    {
        Notifier.Send(new Notification
        {
            Title = title,
            Message = message,
            Type = type,
            PopupLength = 4f,
            ShowTitleOnPopup = true
        });
    }
}
