using BoneSnap.SDK;

namespace BoneSnap.Capture.CaptureHooks;

/// <summary>
/// Temporarily hides the BoneSnap overlays (timer & preview) during capture
/// </summary>
public class HideHUD : ICaptureHook
{
    private bool _wasActive;
    
    public void PreCapture(CaptureParams captureParams)
    {
        var hud = SnapHUD.GetInstance();
        _wasActive = hud.IsActive();
        hud.Hide();
    }

    public void PostCapture(CaptureParams captureParams)
    {
        if (_wasActive)
        {
            var hud = SnapHUD.GetInstance();
            hud.Show();
        }
    }
}