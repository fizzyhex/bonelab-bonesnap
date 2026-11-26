using Il2CppSLZ.Bonelab;
using UnityEngine;
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
    }
    
    private RigScreenOptions? _rigScreenOptions;
    private SpectatorReferences? _spectatorReferences;
    
    public SpectatorReferences GetSpectatorReferences()
    {
        if (_spectatorReferences != null)
            return (SpectatorReferences)_spectatorReferences;

        var rigScreenOptions = GetRigScreenOptions();
        var spectatorCamera = rigScreenOptions != null ? GetSpectatorCamera(rigScreenOptions) : null;

        var references = new SpectatorReferences
        {
            RigScreenOptions = rigScreenOptions,
            Camera = spectatorCamera,
            Follower = spectatorCamera != null ? spectatorCamera.GetComponent<SmoothFollower>() : null
        };

        if (spectatorCamera != null)
            _spectatorReferences = references;

        return references;
    }
    
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
    }
}