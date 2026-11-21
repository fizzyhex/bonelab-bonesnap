using Il2CppOccaSoftware.Exposure.Runtime;
using Il2CppSLZ.Bonelab;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BoneSnap;

// Stores lazy references to scene objects that are used frequently
// (this class sucks, but it's lazy initialised, so whatever for now... it can be refactored later...)
public class ReferenceCache
{
    public struct SpectatorReferences
    {
        public RigScreenOptions? RigScreenOptions;
        public Camera? Camera;
        public SmoothFollower? Follower;
        public AutoExposure? AutoExposure;
        public Volume? Volume;
    }
    
    private RigScreenOptions? _rigScreenOptions;
    private SpectatorReferences? _spectatorReferences;
    private AutoExposure? _autoExposure;
    
    public SpectatorReferences GetSpectatorReferences()
    {
        if (_spectatorReferences != null)
            return (SpectatorReferences)_spectatorReferences;

        var rigScreenOptions = GetRigScreenOptions();
        var spectatorCamera = rigScreenOptions != null ? GetSpectatorCamera(rigScreenOptions) : null;
        var volume = spectatorCamera != null ? GetVolume(spectatorCamera) : null;
        
        volume?.profile?.TryGet(out _autoExposure);

        _spectatorReferences = new SpectatorReferences
        {
            RigScreenOptions = GetRigScreenOptions(),
            Camera = spectatorCamera,
            Follower = spectatorCamera != null ? spectatorCamera.GetComponent<SmoothFollower>() : null,
            AutoExposure = _autoExposure,
            Volume = volume
        };
        
        return (SpectatorReferences)_spectatorReferences;
    }
    
    private Volume? GetVolume(Camera spectatorCamera) => spectatorCamera.GetComponent<Volume>();

    private RigScreenOptions? GetRigScreenOptions()
    {
        if (_rigScreenOptions)
            return _rigScreenOptions;
        
        return _rigScreenOptions = Object
            .FindObjectsOfType<RigScreenOptions>()
            .Where(rigScreenOptions => rigScreenOptions.gameObject.activeInHierarchy)
            .FirstOrDefault(rigScreenOptions => rigScreenOptions.cam);
    }
    
    private Camera? GetSpectatorCamera(RigScreenOptions rigScreenOptions)
        => rigScreenOptions.cam ? rigScreenOptions.cam : null;

    public void ClearReferences()
    {
        _rigScreenOptions = null;
        _spectatorReferences = null;
        _autoExposure = null;
    }
}