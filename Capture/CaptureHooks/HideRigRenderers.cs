using BoneSnap.Utilities;
using UnityEngine;

namespace BoneSnap.Capture.CaptureHooks;

/// <summary>
/// If configured by the user, hides RigManagers during preview.
/// </summary>
public class HideRigRenderers : ICaptureHook
{
    private readonly List<Renderer> _hidden = [];

    private void Hide(HideRigsMode mode)
    {
        _hidden.Clear();

        foreach (var rigManager in RigQuery.GetRigs(mode))
        {
            foreach (var renderer in RigQuery.GetRenderers(rigManager, includeHeld: false))
            {
                if (!renderer.enabled)
                    continue;

                renderer.enabled = false;
                _hidden.Add(renderer);
            }
        }
    }

    public void PreCapture(CaptureParams captureParams)
    {
        Hide(captureParams.HideRigs);
    }

    public void PostCapture(CaptureParams captureParams)
    {
        foreach (var renderer in _hidden)
        {
            if (renderer != null)
                renderer.enabled = true;
        }
    }
}
