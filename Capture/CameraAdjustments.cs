using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoneSnap.Capture;

/// <summary>
/// Temporarily applies the field of view and post-processing settings from <see cref="CaptureParams"/> until
/// destroyed.
/// </summary>
internal sealed class CameraAdjustments : IDisposable
{
    private readonly Camera _camera;
    private readonly float _previousFieldOfView;
    private readonly List<VolumeComponent> _disabledOverrides;

    private CameraAdjustments(Camera camera, CaptureParams captureParams)
    {
        _camera = camera;
        _previousFieldOfView = camera.fieldOfView;

        _disabledOverrides = captureParams.DisabledVolumeComponents is { Count: > 0 } disabledComponents
            ? DisableVolumeOverrides(camera, disabledComponents)
            : [];

        if (captureParams.FieldOfView is { } fieldOfView)
            camera.fieldOfView = fieldOfView;
    }

    public static CameraAdjustments Apply(Camera camera, CaptureParams captureParams) => new(camera, captureParams);

    public void Dispose()
    {
        if (_camera)
            _camera.fieldOfView = _previousFieldOfView;

        foreach (var volumeComponent in _disabledOverrides)
            volumeComponent.active = true;
    }

    private static List<VolumeComponent> DisableVolumeOverrides(Camera camera, IReadOnlySet<string> componentNames)
    {
        var disabledOverrides = new List<VolumeComponent>();

        foreach (var volume in VolumeManager.instance.GetVolumes(GetVolumeLayerMask(camera)))
        {
            if (!volume.isActiveAndEnabled)
                continue;

            var profile = volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;

            if (profile == null)
                continue;

            foreach (var volumeComponent in profile.components)
            {
                if (!volumeComponent.active || !componentNames.Contains(volumeComponent.GetIl2CppType().Name))
                    continue;

                volumeComponent.active = false;
                disabledOverrides.Add(volumeComponent);
            }
        }

        return disabledOverrides;
    }

    private static LayerMask GetVolumeLayerMask(Camera camera)
    {
        var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        // Use the URP fallback for cameras without UniversalAdditionalCameraData
        return cameraData ? cameraData.volumeLayerMask : 1;
    }
}
