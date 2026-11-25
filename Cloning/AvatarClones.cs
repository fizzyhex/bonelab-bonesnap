using System.Collections;
using BoneLib;
using BoneSnap.Assets;
using BoneSnap.Multiplayer;
using BoneSnap.Utilities;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BoneSnap.Cloning;

/// <summary>
/// Responsible for creating a static, render-only copies of avatars (and optionally held items)
/// </summary>
internal sealed record AvatarClone(int Id, byte? Owner, GameObject Root, List<Renderer> Renderers, List<Mesh> OwnedMeshes)
{
    public bool IsLocal => Owner == null;

    public Bounds GetBounds()
    {
        var bounds = default(Bounds);
        var found = false;

        foreach (var renderer in Renderers)
        {
            if (renderer == null)
                continue;

            if (found)
                bounds.Encapsulate(renderer.bounds);
            else
                bounds = renderer.bounds;

            found = true;
        }

        return bounds;
    }
}

/// <summary>
/// Creates, tracks and removes avatar clones.
/// </summary>
internal static class AvatarClones
{
    private static readonly List<AvatarClone> Clones = [];
    private static int _nextId;

    public static bool FusionSyncEnabled { get; set; }

    /// <summary>
    /// Raised whenever a clone is created or removed.
    /// </summary>
    public static event Action? Changed;

    public static IReadOnlyList<AvatarClone> Local => Clones.Where(clone => clone.IsLocal).ToList();

    public static IEnumerable<Renderer> GetRenderers()
        => Clones.SelectMany(clone => clone.Renderers).Where(renderer => renderer != null);

    /// <summary>
    /// Clones the local player's avatar after <paramref name="delay"/> seconds.
    /// </summary>
    public static void CloneLocalAfterDelay(float delay, bool includeHeld)
    {
        if (AssetManager.CaptureTimerAudio != null)
            AudioManager.Play(AssetManager.CaptureTimerAudio);

        MelonCoroutines.Start(CoroutineUtils.WaitAndThenRunCallback(delay, () =>
            MelonCoroutines.Start(CloneLocal(includeHeld))));
    }

    private static IEnumerator CloneLocal(bool includeHeld)
    {
        AvatarClone? clone;

        using (HeadOffset.Suppress())
        {
            yield return new WaitForEndOfFrame();

            if (Player.RigManager == null)
                yield break;

            clone = Create(Player.RigManager, includeHeld, owner: null, id: ++_nextId);
        }

        if (clone == null)
            yield break;

        if (AssetManager.CaptureStartAudio != null)
            AudioManager.Play(AssetManager.CaptureStartAudio);

        if (FusionSyncEnabled)
            FusionSync.SendCloneCreated(clone.Id, includeHeld);
    }

    /// <summary>
    /// Bakes <paramref name="rig"/>'s current avatar pose into a new clone.
    /// </summary>
    public static AvatarClone? Create(RigManager rig, bool includeHeld, byte? owner, int id)
    {
        var avatar = rig.avatar;

        if (avatar == null)
            return null;

        var sources = RendererCopy.GetVisibleMeshes(avatar.gameObject);

        var forced = new HashSet<IntPtr>();
        AddForced(avatar.headMeshes);
        AddForced(avatar.hairMeshes);

        if (includeHeld)
        {
            foreach (var held in RigQuery.GetHeldObjects(rig))
                sources.AddRange(RendererCopy.GetVisibleMeshes(held));
        }

        var root = new GameObject($"BoneSnap Clone {id}");
        var renderers = new List<Renderer>();
        var ownedMeshes = new List<Mesh>();
        var copied = new HashSet<IntPtr>();

        foreach (var (renderer, mesh) in sources)
        {
            if (!copied.Add(renderer.Pointer))
                continue;

            renderers.Add(CopyRenderer(renderer, mesh, root.transform, forced.Contains(renderer.Pointer), ownedMeshes));
        }

        var clone = new AvatarClone(id, owner, root, renderers, ownedMeshes);
        Clones.Add(clone);
        Changed?.Invoke();
        return clone;

        void AddForced(IEnumerable<SkinnedMeshRenderer>? meshes)
        {
            if (meshes == null)
                return;

            foreach (var mesh in meshes)
            {
                if (mesh == null || mesh.sharedMesh == null || !forced.Add(mesh.Pointer))
                    continue;

                sources.Add((mesh, mesh.sharedMesh));
            }
        }
    }

    public static void Delete(AvatarClone clone)
    {
        if (!Clones.Remove(clone))
            return;

        Destroy(clone);

        if (clone.IsLocal && FusionSyncEnabled)
            FusionSync.SendCloneDeleted(clone.Id);

        Changed?.Invoke();
    }

    public static void DeleteRemote(byte owner, int id)
    {
        var clone = Clones.FirstOrDefault(clone => clone.Owner == owner && clone.Id == id);

        if (clone != null)
            Delete(clone);
    }

    public static void DeleteAll()
    {
        if (Clones.Count == 0)
            return;

        foreach (var clone in Clones)
        {
            Destroy(clone);

            if (clone.IsLocal && FusionSyncEnabled)
                FusionSync.SendCloneDeleted(clone.Id);
        }

        Clones.Clear();
        Changed?.Invoke();
    }

    public static void Clear()
    {
        if (Clones.Count == 0)
            return;

        foreach (var clone in Clones)
            Destroy(clone);

        Clones.Clear();
        Changed?.Invoke();
    }

    private static void Destroy(AvatarClone clone)
    {
        if (clone.Root != null)
            Object.Destroy(clone.Root);

        foreach (var mesh in clone.OwnedMeshes)
        {
            if (mesh != null)
                Object.Destroy(mesh);
        }
    }

    private static Renderer CopyRenderer(Renderer source, Mesh mesh, Transform parent, bool forceVisible,
        List<Mesh> ownedMeshes)
    {
        var sourceTransform = source.transform;
        var copy = new GameObject(source.name) { layer = source.gameObject.layer };
        copy.transform.SetParent(parent, false);
        copy.transform.SetPositionAndRotation(sourceTransform.position, sourceTransform.rotation);
        copy.transform.localScale = sourceTransform.lossyScale;

        var skinned = source.TryCast<SkinnedMeshRenderer>();

        if (skinned != null)
        {
            mesh = new Mesh { name = $"{source.name} (Baked)" };
            skinned.BakeMesh(mesh, true);
            ownedMeshes.Add(mesh);
        }

        copy.AddComponent<MeshFilter>().sharedMesh = mesh;

        var copyRenderer = copy.AddComponent<MeshRenderer>();
        copyRenderer.sharedMaterials = source.sharedMaterials;
        copyRenderer.shadowCastingMode = forceVisible ? ShadowCastingMode.On : source.shadowCastingMode;
        CopyPropertyBlocks(source, copyRenderer);

        return copyRenderer;
    }

    private static void CopyPropertyBlocks(Renderer source, Renderer destination)
    {
        var block = new MaterialPropertyBlock();

        if (source.HasPropertyBlock())
        {
            source.GetPropertyBlock(block);
            destination.SetPropertyBlock(block);
        }

        for (var slot = 0; slot < source.sharedMaterials.Length; slot++)
        {
            block.Clear();
            source.GetPropertyBlock(block, slot);

            if (!block.isEmpty)
                destination.SetPropertyBlock(block, slot);
        }
    }
}
