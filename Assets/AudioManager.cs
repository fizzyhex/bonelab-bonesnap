using Il2CppSLZ.Marrow.Audio;
using UnityEngine;

namespace BoneSnap.Assets;

internal static class AudioManager
{
    private static GameObject _audioGameObject = null!;

    public static void Play(AudioClip clip, float volume = 0.7f)
    {
        if (_audioGameObject == null) _audioGameObject = new GameObject("BoneSnap AudioManager");
        var audioSource = _audioGameObject.GetComponent<AudioSource>()
            ? _audioGameObject.GetComponent<AudioSource>()
            : _audioGameObject.AddComponent<AudioSource>();
        audioSource.outputAudioMixerGroup = Audio3dManager.hardInteraction;
        audioSource.PlayOneShot(clip, volume);
    }
}