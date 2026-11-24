using BoneSnap.Cloning;
using BoneSnap.Utilities;
using UnityEngine;

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

        foreach (var renderer in CollectPlayerRenderers())
        {
            var go = renderer.gameObject;
            _dirty.TryAdd(go.GetInstanceID(), (go, go.layer));
            go.layer = Screenshotter.CompositingLayer;
        }
    }

    private static List<Renderer> CollectPlayerRenderers()
    {
        var renderers = new List<Renderer>();

        foreach (var rig in RigQuery.GetRigs(HideRigsMode.All))
            renderers.AddRange(RigQuery.GetRenderers(rig, includeHeld: true));

        renderers.AddRange(AvatarClones.GetRenderers());
        return renderers;
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
