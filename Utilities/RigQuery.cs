using BoneLib;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.Pool;
using UnityEngine;

namespace BoneSnap.Utilities;

/// <summary>
/// Gathers rigs/held objects for capture hooks and cloning.
/// </summary>
internal static class RigQuery
{
    public static RigManager[] GetRigs(HideRigsMode mode) => mode switch
    {
        HideRigsMode.Local when Player.RigManager != null => [Player.RigManager],
        HideRigsMode.All => UnityEngine.Object.FindObjectsOfType<RigManager>(includeInactive: false).ToArray(),
        _ => []
    };
    
    public static List<GameObject> GetHeldObjects(RigManager rig)
    {
        var held = new List<GameObject>();
        var physicsRig = rig.physicsRig;

        if (!physicsRig)
            return held;

        foreach (var hand in new[] { physicsRig.leftHand, physicsRig.rightHand })
        {
            var root = GetHeldRoot(hand);

            if (root != null && root != rig.gameObject && held.All(existing => existing != root))
                held.Add(root);
        }

        return held;
    }

    /// <summary>
    /// Gather enabled Renderers under the Ri.
    /// </summary>
    public static List<Renderer> GetRenderers(RigManager rig, bool includeHeld)
    {
        var renderers = new List<Renderer>(rig.GetComponentsInChildren<Renderer>(includeInactive: false));

        if (!includeHeld)
            return renderers;

        foreach (var go in GetHeldObjects(rig))
            renderers.AddRange(go.GetComponentsInChildren<Renderer>(includeInactive: false));

        return renderers;
    }

    /// <summary>
    /// Get the Poolee from a grabbed item.
    /// </summary>
    private static GameObject? GetHeldRoot(Hand? hand)
    {
        if (!hand)
            return null;

        var attached = hand!.m_CurrentAttachedGO;

        if (!attached)
            return null;

        var poolee = attached.GetComponentInParent<Poolee>();
        return poolee ? poolee.gameObject : null;
    }
}
