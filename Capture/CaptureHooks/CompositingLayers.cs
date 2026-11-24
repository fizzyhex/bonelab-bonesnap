using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.Pool;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoneSnap.Capture.CaptureHooks;

/// <summary>
/// If configured by the user, configures what's visible to the camera based on current compositing steps.
/// </summary>
public class CompositingLayers : ICaptureHook
{
    private readonly Dictionary<int, (GameObject go, int layer)> _dirty = [];

    public void PreCapture(CaptureParams captureParams)
    {
        if (captureParams.CompositingLayer == CompositingLayer.Combined)
            return;
        
        foreach (var renderer in CollectRigRenderers())
        {
            var go = renderer.gameObject;
            _dirty.TryAdd(go.GetInstanceID(), (go, go.layer));
            go.layer = Screenshotter.CompositingLayer;
        }
    }

    /// <summary>
    /// Collects the Renderers of all rigs in the scene, as well as their held objects.
    /// </summary>
    private static List<Renderer> CollectRigRenderers()
    {
        // TODO: Include held objects
        var rigs = Object.FindObjectsOfType<RigManager>(includeInactive: false);
        var renderers = new List<Renderer>();

        foreach (var rig in rigs)
        {
            CollectRenderers(rig, ref renderers);
        }

        return renderers;
    }

    private static void CollectRenderers(RigManager rig, ref List<Renderer> renderers)
    {
        renderers.AddRange(rig.GetComponentsInChildren<Renderer>(includeInactive: false));

        var physicsRig = rig.physicsRig;

        if (!physicsRig)
            return;

        // Both hands can hold the same object, so dedupe before collecting
        var held = new Dictionary<int, GameObject>();
        AddHeld(rig, physicsRig.rightHand, held);
        AddHeld(rig, physicsRig.leftHand, held);

        foreach (var go in held.Values)
            renderers.AddRange(go.GetComponentsInChildren<Renderer>(includeInactive: false));
    }

    private static void AddHeld(RigManager rig, Hand hand, Dictionary<int, GameObject> held)
    {
        if (!hand)
            return;

        var attached = hand.m_CurrentAttachedGO;

        if (!attached)
            return;

        var poolee = attached.GetComponentInParent<Poolee>();

        if (!poolee || poolee.gameObject == rig.gameObject)
            return;

        held.TryAdd(poolee.GetInstanceID(), poolee.gameObject);
    }

    public void PostCapture(CaptureParams captureParams)
    {
        foreach (var (go, oldLayer) in _dirty.Values)
        {
            if (go)
                go.layer = oldLayer;
        }

        _dirty.Clear();
    }
}