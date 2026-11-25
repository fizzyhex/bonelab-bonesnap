using UnityEngine;

namespace BoneSnap.Utilities;

/// <summary>
/// Helpers for building render-only copies of objects.
/// </summary>
internal static class RendererCopy
{
    /// <summary>
    /// Returns meshes from enabled renderers under <paramref name="root"/>, excluding lower LODs.
    /// </summary>
    public static List<(Renderer Renderer, Mesh Mesh)> GetVisibleMeshes(GameObject root)
    {
        var lowerLods = new HashSet<IntPtr>();

        foreach (var lodGroup in root.GetComponentsInChildren<LODGroup>(true))
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

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || lowerLods.Contains(renderer.Pointer) || !IsActive(renderer.transform, root.transform))
                continue;

            if (GetSharedMesh(renderer) is { } mesh)
                meshes.Add((renderer, mesh));
        }

        return meshes;
    }

    public static Mesh? GetSharedMesh(Renderer renderer)
    {
        var skinned = renderer.TryCast<SkinnedMeshRenderer>();
        if (skinned != null)
            return skinned.sharedMesh;

        if (renderer.TryCast<MeshRenderer>() != null)
            return renderer.GetComponent<MeshFilter>()?.sharedMesh;

        return null;
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
}
