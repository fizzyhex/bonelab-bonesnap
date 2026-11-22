using System.Globalization;
using BoneLib;
using BoneSnap.SDK;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.Interaction;
using Il2CppSLZ.Marrow.Pool;
using Il2CppSLZ.Marrow.Warehouse;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;

namespace BoneSnap.Personalisation;

/// <summary>
/// Serializable data for the trinket's spawnable & fine-tuning
/// </summary>
internal readonly record struct TrinketSpec(string Barcode, float Scale, Vector3 Offset, Vector3 Rotation)
{
    public static readonly TrinketSpec None = new(string.Empty, 1f, Vector3.zero, Vector3.zero);

    public bool IsEmpty => string.IsNullOrEmpty(Barcode);

    /// <summary>
    /// Serializes a value for Fusion RPC sync.
    /// <c>barcode|scale|x,y,z|x,y,z</c> 
    /// </summary>
    public string Serialize() => IsEmpty
        ? string.Empty
        : string.Join('|', Barcode, SerFloat(Scale), SerVector(Offset), SerVector(Rotation));

    public static bool TryParse(string? value, out TrinketSpec spec)
    {
        spec = None;

        if (string.IsNullOrEmpty(value))
            return true;

        var parts = value.Split('|');

        if (parts.Length != 4 ||
            !TryDesFloat(parts[1],
                out var scale) ||
            !TryDesVector(parts[2],
                out var offset) ||
            !TryDesVector(parts[3],
                out var rotation))
            return false;

        spec = new TrinketSpec(parts[0], scale, offset, rotation);
        return true;
    }

    private static string SerFloat(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static string SerVector(Vector3 v) => $"{SerFloat(v.x)},{SerFloat(v.y)},{SerFloat(v.z)}";

    private static bool TryDesFloat(string value, out float result)
        => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static bool TryDesVector(string value, out Vector3 result)
    {
        result = default;
        var parts = value.Split(',');
        
        if (parts.Length != 3 ||
            !TryDesFloat(parts[0],
                out var x) ||
            !TryDesFloat(parts[1],
                out var y) ||
            !TryDesFloat(parts[2],
                out var z))
            return false;
        
        result = new Vector3(x, y, z);
        return true;
    }
}

internal sealed class TrinketAdjustment
{
    public float Scale { get; set; } = 1f;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float OffsetZ { get; set; }
    public float RotationX { get; set; }
    public float RotationY { get; set; }
    public float RotationZ { get; set; }
}

internal static class Trinkets
{
    // Largest extent of a trinket before the player scales it, in metres
    private const float TargetSize = .05f;

    private static readonly Quaternion HangRotation = Quaternion.Euler(180f, 0f, 0f);

    private readonly record struct Fit(Vector3 TopCentre, float Scale);

    private static readonly Dictionary<string, (GameObject Prefab, Fit Fit)> Loaded = new();
    private static readonly Dictionary<string, List<Action<GameObject?>>> Pending = new();

    private static string? _localSpec;

    /// <summary>
    /// The local player's trinket
    /// </summary>
    public static string LocalSpec => _localSpec ??= GetSpec(Main.Prefs, Main.Prefs.KeychainBarcode.Value).Serialize();

    public static void Invalidate() => _localSpec = null;

    public static TrinketSpec GetSpec(BoneSnapPreferences prefs, string barcode)
    {
        if (string.IsNullOrEmpty(barcode))
            return TrinketSpec.None;

        var adjustment = GetAdjustment(prefs, barcode);

        return new TrinketSpec(
            barcode,
            adjustment.Scale,
            new Vector3(adjustment.OffsetX, adjustment.OffsetY, adjustment.OffsetZ),
            new Vector3(adjustment.RotationX, adjustment.RotationY, adjustment.RotationZ));
    }

    public static TrinketAdjustment GetAdjustment(BoneSnapPreferences prefs, string barcode)
        => ReadAdjustments(prefs).TryGetValue(barcode, out var adjustment) ? adjustment : new TrinketAdjustment();

    public static void SetAdjustment(BoneSnapPreferences prefs, string barcode, TrinketAdjustment adjustment)
    {
        var adjustments = ReadAdjustments(prefs);
        adjustments[barcode] = adjustment;
        prefs.KeychainAdjustments.Value = JsonConvert.SerializeObject(adjustments);
    }

    public static void ResetAdjustment(BoneSnapPreferences prefs, string barcode)
    {
        var adjustments = ReadAdjustments(prefs);

        if (adjustments.Remove(barcode))
            prefs.KeychainAdjustments.Value = JsonConvert.SerializeObject(adjustments);
    }

    private static Dictionary<string, TrinketAdjustment> ReadAdjustments(BoneSnapPreferences prefs)
    {
        try
        {
            return JsonConvert.DeserializeObject<Dictionary<string, TrinketAdjustment>>(prefs.KeychainAdjustments.Value) ?? new();
        }
        catch (JsonException)
        {
            return new Dictionary<string, TrinketAdjustment>();
        }
    }
    
    public static bool TryGetHeldCrate(Hand? hand, out SpawnableCrate crate)
    {
        crate = null!;

        if (hand == null || !Player.HandsExist)
            return false;

        var held = Player.GetObjectInHand(hand);

        if (!Poolee.Cache.TryGet(held, out var poolee))
            return false;

        // NO recursion.
        if (poolee.SpawnableCrate == null || poolee.GetComponentInChildren<SnapPersonalisation>(true) != null)
            return false;

        crate = poolee.SpawnableCrate;
        return true;
    }

    public static string GetTitle(string barcode)
        => TryGetCrate(barcode, out var crate) ? StripRichText(crate.Title) : barcode;

    /// <summary>
    /// Loads a spawnable's prefab from its bundle.
    /// </summary>
    public static void LoadPrefab(string barcode, Action<GameObject?> onLoaded)
    {
        if (Loaded.TryGetValue(barcode, out var loaded) && loaded.Prefab != null)
        {
            onLoaded(loaded.Prefab);
            return;
        }

        if (Pending.TryGetValue(barcode, out var waiting))
        {
            waiting.Add(onLoaded);
            return;
        }

        if (!TryGetCrate(barcode, out var crate))
        {
            onLoaded(null);
            return;
        }

        Pending[barcode] = [onLoaded];

        crate.MainGameObject.LoadAsset((Action<GameObject>)(prefab =>
        {
            Pending.Remove(barcode, out var callbacks);

            if (prefab != null)
                Loaded[barcode] = (prefab, GetFit(prefab, crate));
            else
                MelonLogger.Warning($"Couldn't load keychain trinket '{barcode}'");

            foreach (var callback in callbacks ?? [])
            {
                try
                {
                    callback(prefab);
                }
                catch (Exception e)
                {
                    MelonLogger.Error($"Failed to hang keychain trinket '{barcode}'", e);
                }
            }
        }));
    }

    /// <summary>
    /// Hangs a render-only copy of a loaded prefab from the keychain
    /// </summary>
    public static GameObject Build(GameObject prefab, TrinketSpec spec, Transform anchor, int layer)
    {
        var fit = Loaded.TryGetValue(spec.Barcode, out var loaded) ? loaded.Fit : new Fit(Vector3.zero, 1f);

        // Undo the chain's scale
        var root = CreateChild("BoneSnap Trinket", anchor, layer);
        root.localRotation = HangRotation;
        root.localScale = Divide(Vector3.one, anchor.lossyScale);

        var pivot = CreateChild("Pivot", root, layer);
        pivot.localPosition = spec.Offset;
        pivot.localRotation = Quaternion.Euler(spec.Rotation);
        pivot.localScale = Vector3.one * (fit.Scale * Mathf.Max(spec.Scale, .01f));

        var model = CreateChild("Model", pivot, layer);
        model.localPosition = -fit.TopCentre;

        foreach (var (renderer, mesh) in GetVisibleMeshes(prefab))
        {
            var copy = CreateChild(renderer.name, model, layer);
            var source = renderer.transform;
            var prefabRoot = prefab.transform;

            copy.localPosition = prefabRoot.InverseTransformPoint(source.position);
            copy.localRotation = Quaternion.Inverse(prefabRoot.rotation) * source.rotation;
            copy.localScale = Divide(source.lossyScale, prefabRoot.lossyScale);

            copy.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var copyRenderer = copy.gameObject.AddComponent<MeshRenderer>();
            copyRenderer.sharedMaterials = renderer.sharedMaterials;
            copyRenderer.shadowCastingMode = renderer.shadowCastingMode;
        }

        return root.gameObject;
    }

    private static bool TryGetCrate(string barcode, out SpawnableCrate crate)
    {
        crate = null!;

        var warehouse = AssetWarehouse.Instance;

        if (warehouse == null || !warehouse.TryGetCrate(new Barcode(barcode), out var found) || found == null)
            return false;

        var spawnable = found.TryCast<SpawnableCrate>();

        if (spawnable == null)
            return false;

        crate = spawnable;
        return true;
    }

    /// <summary>
    /// Attempts to estimate the initial scale of a trinket via its bounds.
    /// </summary>
    private static Fit GetFit(GameObject prefab, SpawnableCrate crate)
    {
        if (!TryGetBodyBounds(prefab, out var bounds) && !TryGetUsable(crate.ColliderBounds, out bounds))
            TryGetMeshBounds(prefab, out bounds);

        var size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        var scale = size > .0001f ? TargetSize / size : 1f;

        return new Fit(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z), scale);
    }

    private static bool TryGetBodyBounds(GameObject prefab, out Bounds bounds)
    {
        bounds = default;
        var found = false;

        if (!MarrowEntity.Cache.TryGet(prefab, out var entity))
            return false;
        
        if (entity.Bodies == null)
            return false;

        foreach (var body in entity.Bodies)
        {
            if (body == null || !TryGetUsable(body.Bounds, out var bodyBounds))
                continue;

            // MarrowBody bounds are axis-aligned, centred relative to the body's position
            var centre = prefab.transform.InverseTransformPoint(body.transform.position + bodyBounds.center);
            var local = new Bounds(centre, bodyBounds.size);

            if (found)
                bounds.Encapsulate(local);
            else
                bounds = local;

            found = true;
        }

        return found;
    }

    private static bool TryGetMeshBounds(GameObject prefab, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        var prefabRoot = prefab.transform;

        foreach (var (renderer, mesh) in GetVisibleMeshes(prefab))
        {
            var meshBounds = mesh.bounds;
            var source = renderer.transform;

            for (var corner = 0; corner < 8; corner++)
            {
                var point = meshBounds.center + Vector3.Scale(meshBounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                var local = prefabRoot.InverseTransformPoint(source.TransformPoint(point));

                if (found)
                    bounds.Encapsulate(local);
                else
                    bounds = new Bounds(local, Vector3.zero);

                found = true;
            }
        }

        return found;
    }

    private static bool TryGetUsable(Bounds candidate, out Bounds bounds)
    {
        bounds = candidate;
        return candidate.size.sqrMagnitude > 1e-8f;
    }

    /// <summary>
    /// The prefab's currently enabled meshes - skipping disabled objects and lower LODs.
    /// </summary>
    private static List<(Renderer Renderer, Mesh Mesh)> GetVisibleMeshes(GameObject prefab)
    {
        var lowerLods = new HashSet<IntPtr>();

        foreach (var lodGroup in prefab.GetComponentsInChildren<LODGroup>(true))
        {
            var lods = lodGroup.GetLODs();
            for (var i = 1; i < lods.Length; i++)
            {
                foreach (var lodRenderer in lods[i].renderers)
                {
                    if (lodRenderer != null)
                        lowerLods.Add(lodRenderer.Pointer);
                }
            }
        }

        var meshes = new List<(Renderer, Mesh)>();

        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || lowerLods.Contains(renderer.Pointer) || !IsActive(renderer.transform, prefab.transform))
                continue;

            Mesh? mesh = null;

            var skinned = renderer.TryCast<SkinnedMeshRenderer>();
            if (skinned != null)
                mesh = skinned.sharedMesh;
            else if (renderer.TryCast<MeshRenderer>() != null)
                mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;

            if (mesh != null)
                meshes.Add((renderer, mesh));
        }

        return meshes;
    }

    private static bool IsActive(Transform transform, Transform root)
    {
        for (var current = transform; current != null && current != root; current = current.parent)
        {
            if (!current.gameObject.activeSelf)
                return false;
        }

        return true;
    }

    private static Transform CreateChild(string name, Transform parent, int layer)
    {
        var child = new GameObject(name) { layer = layer };
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    private static Vector3 Divide(Vector3 a, Vector3 b) => new(
        b.x != 0f ? a.x / b.x : a.x,
        b.y != 0f ? a.y / b.y : a.y,
        b.z != 0f ? a.z / b.z : a.z);

    private static string StripRichText(string text)
        => System.Text.RegularExpressions.Regex.Replace(text ?? string.Empty, "<.*?>", string.Empty);
}
