namespace BoneSnap.Capture.CaptureHooks;

public interface ICaptureHook
{
    public void PreCapture(CaptureParams captureParams)
    {
    }

    public void PostCapture(CaptureParams captureParams)
    {
    }
}