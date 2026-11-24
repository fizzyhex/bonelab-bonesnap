using Il2CppOccaSoftware.Exposure.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoneSnap.Capture;

/// <summary>
/// Freezes auto exposure at the current value to prevent it from varying across compositing passes.
/// </summary>
internal sealed class AutoExposureFreeze : IDisposable
{
    private readonly record struct AdaptationSpeed(FloatParameter Parameter, float Value, bool OverrideState);

    private readonly List<AdaptationSpeed> _frozenSpeeds = [];

    private AutoExposureFreeze(Camera camera)
    {
        foreach (var volume in VolumeManager.instance.GetVolumes(CameraAdjustments.GetVolumeLayerMask(camera)))
        {
            if (!volume.isActiveAndEnabled)
                continue;

            var profile = volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;

            if (profile == null)
                continue;

            foreach (var volumeComponent in profile.components)
            {
                if (volumeComponent.TryCast<AutoExposure>() is not { } autoExposure)
                    continue;

                Freeze(autoExposure.darkToLightSpeed);
                Freeze(autoExposure.lightToDarkSpeed);
            }
        }
    }

    public static AutoExposureFreeze Apply(Camera camera) => new(camera);

    public void Dispose()
    {
        foreach (var (parameter, value, overrideState) in _frozenSpeeds)
        {
            parameter.value = value;
            parameter.overrideState = overrideState;
        }
    }

    private void Freeze(FloatParameter speed)
    {
        _frozenSpeeds.Add(new AdaptationSpeed(speed, speed.value, speed.overrideState));

        speed.overrideState = true;
        speed.value = 0;
    }
}
