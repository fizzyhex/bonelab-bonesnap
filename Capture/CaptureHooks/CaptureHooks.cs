namespace BoneSnap.Capture.CaptureHooks;

public class CaptureHooks
{
    private readonly ICaptureHook[] _hooks = [new RevealLocalMeshes(), new HideRigRenderers(), new HideHUD()];

    public void PreCapture(CaptureParams captureParams)
    {
        foreach (var hook in _hooks)
            hook.PreCapture(captureParams);
    }

    public void PostCapture(CaptureParams captureParams)
    {
        foreach (var hook in _hooks)
            hook.PostCapture(captureParams);
    }
}