using Il2CppSLZ.Marrow.Audio;
using UnityEngine;

namespace BoneSnap.Assets;

internal static class AudioManager
{
    private static GameObject _audioGameObject = null!;

    public static void Play(AudioClip clip)
    {
        if (_audioGameObject == null) _audioGameObject = new GameObject("AudioManager");
        var audioSource = _audioGameObject.GetComponent<AudioSource>()
            ? _audioGameObject.GetComponent<AudioSource>()
            : _audioGameObject.AddComponent<AudioSource>();
        audioSource.outputAudioMixerGroup = Audio3dManager.hardInteraction;
        audioSource.PlayOneShot(clip, 0.7f);
    }
}