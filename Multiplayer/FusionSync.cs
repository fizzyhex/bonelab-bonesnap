using System.Runtime.CompilerServices;
using BoneSnap.Cloning;
using BoneSnap.Personalisation;
using BoneSnap.SDK;
using LabFusion.Entities;
using LabFusion.Marrow.Integration;
using LabFusion.Network;
using LabFusion.Network.Serialization;
using LabFusion.Scene;
using LabFusion.SDK.Extenders;
using LabFusion.SDK.Modules;
using MelonLoader;
using UnityEngine;

namespace BoneSnap.Multiplayer;

internal static class FusionSync
{
    private static bool? _isAvailable;

    public static bool IsAvailable => _isAvailable ??= MelonBase.RegisteredMelons.Any(melon => melon.Info.Name == "LabFusion");

    public static void Initialize()
    {
        if (IsAvailable)
            FusionBridge.RegisterMessages();
    }

    public static void SendCloneCreated(int id, bool includeHeld)
    {
        if (IsAvailable)
            FusionBridge.SendClone(CloneAction.Create, id, includeHeld);
    }

    public static void SendCloneDeleted(int id)
    {
        if (IsAvailable)
            FusionBridge.SendClone(CloneAction.Delete, id, false);
    }

    public static bool Tick(SnapPersonalisation camera) => IsAvailable && FusionBridge.Tick(camera);

    public static void OnLocalStyleChanged(SnapPersonalisation camera)
    {
        if (!IsAvailable || !FusionBridge.OnLocalStyleChanged(camera))
            camera.ApplyLocalStyle();
    }

    public static void Forget(SnapPersonalisation camera)
    {
        if (IsAvailable)
            FusionBridge.Forget(camera);
    }
}

/// <summary>
/// Abstracted away Fusion API (so that we don't need to introduce it as a required dep).
/// </summary>
internal static class FusionBridge
{
    private static readonly float[] PushDelays = [1f, 4f];

    private sealed class State
    {
        public RPCString? Accent;
        public RPCString? Trinket;
        public ushort EntityId;
        public bool HasEntity;
        public bool IsLocallyOwned;
        public int Pushes;
        public float NextPush;
    }

    private static readonly ConditionalWeakTable<SnapPersonalisation, State> States = new();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Tick(SnapPersonalisation camera)
    {
        if (!NetworkSceneManager.IsLevelNetworked)
            return false;

        var state = GetState(camera);
        var variable = state.Accent ?? state.Trinket;

        // Not synced
        if (variable == null)
            return false;

        // Not registered with Fusion (or owned) yet - show the local look until ownership is established
        if (!RPCVariableExtender.Cache.TryGet(variable, out var entity) || !entity.HasOwner)
        {
            camera.ApplyAccent(string.Empty);
            camera.ApplyTrinket(string.Empty);
            return true;
        }

        if (!state.HasEntity || state.EntityId != entity.ID)
        {
            // Fusion gives the spawner ownership of what they spawn
            state.HasEntity = true;
            state.EntityId = entity.ID;
            state.IsLocallyOwned = entity.IsOwner;
            state.Pushes = 0;
            state.NextPush = Time.time + PushDelays[0];
        }

        if (state.IsLocallyOwned)
        {
            camera.ApplyLocalStyle();

            if (state.Pushes < PushDelays.Length && Time.time >= state.NextPush)
            {
                Push(state);
                state.Pushes++;

                if (state.Pushes < PushDelays.Length)
                    state.NextPush = Time.time + PushDelays[state.Pushes] - PushDelays[state.Pushes - 1];
            }

            return true;
        }

        camera.ApplyAccent(Read(state.Accent, state.EntityId));
        camera.ApplyTrinket(Read(state.Trinket, state.EntityId));
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool OnLocalStyleChanged(SnapPersonalisation camera)
    {
        if (!NetworkSceneManager.IsLevelNetworked || !States.TryGetValue(camera, out var state) || !state.HasEntity)
            return false;

        if (!state.IsLocallyOwned)
            return true;

        camera.ApplyLocalStyle();
        Push(state);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Forget(SnapPersonalisation camera) => States.Remove(camera);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RegisterMessages() => ModuleMessageManager.RegisterHandler<CloneMessage>();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SendClone(CloneAction action, int id, bool includeHeld)
    {
        if (!NetworkInfo.HasServer)
            return;

        var data = new CloneData { Action = (byte)action, Id = id, IncludeHeld = includeHeld };
        MessageRelay.RelayModule<CloneMessage, CloneData>(data, CommonMessageRoutes.ReliableToOtherClients);
    }

    private static State GetState(SnapPersonalisation camera)
    {
        if (States.TryGetValue(camera, out var state))
            return state;

        state = new State
        {
            Accent = FindVariable(camera.AccentSync),
            Trinket = FindVariable(camera.TrinketSync)
        };

        States.Add(camera, state);
        return state;
    }

    private static RPCString? FindVariable(GameObject? holder) => holder == null ? null : holder.GetComponent<RPCString>();

    private static void Push(State state)
    {
        state.Accent?.SetValue(Stamp(state.EntityId, AccentColor.CurrentHex));
        state.Trinket?.SetValue(Stamp(state.EntityId, Trinkets.LocalSpec));
    }

    private static string Stamp(ushort entityId, string value) => $"{entityId}:{value}";

    private static string Read(RPCString? variable, ushort entityId)
    {
        var value = variable?.GetLatestValue();
        var prefix = $"{entityId}:";

        return value != null && value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : string.Empty;
    }
}

internal enum CloneAction : byte
{
    Create,
    Delete
}

internal sealed class CloneData : INetSerializable
{
    public byte Action;
    public int Id;
    public bool IncludeHeld;

    public int? GetSize() => sizeof(byte) + sizeof(int) + sizeof(bool);

    public void Serialize(INetSerializer serializer)
    {
        serializer.SerializeValue(ref Action);
        serializer.SerializeValue(ref Id);
        serializer.SerializeValue(ref IncludeHeld);
    }
}

/// <summary>
/// Recreates another player's avatar clones by baking their rig as seen locally.
/// </summary>
[Net.SkipHandleWhileLoading]
internal sealed class CloneMessage : ModuleMessageHandler
{
    protected override void OnHandleMessage(ReceivedMessage received)
    {
        if (received.Sender is not { } sender)
            return;

        var data = received.ReadData<CloneData>();

        switch ((CloneAction)data.Action)
        {
            case CloneAction.Create:
                if (NetworkPlayerManager.TryGetPlayer(sender, out var player) && player.HasRig)
                    AvatarClones.Create(player.RigRefs.RigManager, data.IncludeHeld, sender, data.Id);
                break;
            case CloneAction.Delete:
                AvatarClones.DeleteRemote(sender, data.Id);
                break;
        }
    }
}
