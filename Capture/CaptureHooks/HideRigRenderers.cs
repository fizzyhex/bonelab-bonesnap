using BoneLib;
using Il2CppSLZ.Marrow;
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

        RigManager[] rigManagers = mode switch
        {
            HideRigsMode.Local when Player.RigManager != null => [Player.RigManager],
            // Includes the rigs of other players when Fusion is installed
            HideRigsMode.All => UnityEngine.Object.FindObjectsOfType<RigManager>().ToArray(),
            _ => []
        };

        foreach (var rigManager in rigManagers)
        {
            foreach (var renderer in rigManager.GetComponentsInChildren<Renderer>())
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