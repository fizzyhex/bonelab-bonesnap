#nullable enable
using UnityEngine;
#if MELONLOADER
using BoneSnap.Multiplayer;
using BoneSnap.Personalisation;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Fields;
using MelonLoader;
#endif

namespace BoneSnap.SDK
{
    /// <summary>
    /// Handles keychain physics, and applies the player's accent colour / trinket.
    /// </summary>
#if MELONLOADER
    [RegisterTypeInIl2Cpp]
#endif
    public class SnapPersonalisation : MonoBehaviour
    {
#if MELONLOADER
        public SnapPersonalisation(IntPtr intPtr) : base(intPtr) { }

        public Il2CppReferenceField<Material>? accentMaterial;
        public Il2CppReferenceField<Transform>? chainRoot;
        public Il2CppReferenceField<Transform>? chainLink;
        public Il2CppReferenceField<Rigidbody>? chainRootBody;
        public Il2CppReferenceField<Rigidbody>? chainLinkBody;
        public Il2CppReferenceField<Transform>? trinketAnchor;
        public Il2CppReferenceField<GameObject>? accentSync;
        public Il2CppReferenceField<GameObject>? trinketSync;

        private Transform? ChainRoot => chainRoot?.Get();
        private Transform? ChainLink => chainLink?.Get();
        private Rigidbody? ChainRootBody => chainRootBody?.Get();
        private Rigidbody? ChainLinkBody => chainLinkBody?.Get();
        internal GameObject? AccentSync => accentSync?.Get();
        internal GameObject? TrinketSync => trinketSync?.Get();
#else
        public Material accentMaterial;

        [Tooltip("Root bone of the keychain")]
        public Transform chainRoot;

        [Tooltip("Second bone of the keychain")]
        public Transform chainLink;

        public Rigidbody chainRootBody;

        public Rigidbody chainLinkBody;

        public Transform trinketAnchor;

        [Tooltip("Optional RPCString object for syncing the spawner's accent colour.")]
        public GameObject accentSync;

        [Tooltip("Optional RPCString object for syncing the spawner's trinket.")]
        public GameObject trinketSync;

        private Transform ChainRoot => chainRoot;
        private Transform ChainLink => chainLink;
        private Rigidbody ChainRootBody => chainRootBody;
        private Rigidbody ChainLinkBody => chainLinkBody;
#endif

        private KeychainPhysics? _keychain;

        private void Awake()
        {
            _keychain = KeychainPhysics.Create(GetComponent<Rigidbody>(), ChainRoot, ChainLink, ChainRootBody, ChainLinkBody);
        }

        private void OnEnable()
        {
            _keychain?.Enable();
#if MELONLOADER
            Active.Add(this);
#endif
        }

        private void OnDisable()
        {
            _keychain?.Disable();
#if MELONLOADER
            Active.Remove(this);
#endif
        }

        private void OnDestroy()
        {
            _keychain?.Destroy();
#if MELONLOADER
            FusionSync.Forget(this);
#endif
        }

        private void FixedUpdate()
        {
            _keychain?.FixedTick();
        }

        private void LateUpdate()
        {
            _keychain?.LateTick();
        }

#if MELONLOADER
        private static readonly HashSet<SnapPersonalisation> Active = new();

        private string? _accentHex;
        private string? _trinketSpec;
        private GameObject? _trinket;
        private int _trinketVersion;
        private SnapAccent[]? _accentGraphics;
        private (Renderer Renderer, int Slot)[]? _accentSlots;

        private void Update()
        {
            if (!FusionSync.Tick(this))
                ApplyLocalStyle();
        }
        
        [HideFromIl2Cpp]
        internal static void RefreshLocalStyles()
        {
            foreach (var personalisation in Active)
                FusionSync.OnLocalStyleChanged(personalisation);
        }

        [HideFromIl2Cpp]
        internal void ApplyLocalStyle()
        {
            ApplyAccent(AccentColor.CurrentHex);
            ApplyTrinket(Trinkets.LocalSpec);
        }
        
        [HideFromIl2Cpp]
        internal void ApplyAccent(string hex)
        {
            if (hex == _accentHex)
                return;

            _accentHex = hex;

            _accentGraphics ??= GetComponentsInChildren<SnapAccent>(true).ToArray();
            _accentSlots ??= FindAccentSlots();

            if (!AccentColor.TryParseHex(hex, out var accent))
            {
                foreach (var (renderer, slot) in _accentSlots)
                    renderer.SetPropertyBlock(null, slot);

                foreach (var graphic in _accentGraphics)
                    graphic.Restore();

                return;
            }

            var block = new MaterialPropertyBlock();
            block.SetColor(AccentColor.BaseColorProperty, accent);

            foreach (var (renderer, slot) in _accentSlots)
                renderer.SetPropertyBlock(block, slot);

            foreach (var graphic in _accentGraphics)
                graphic.Apply(accent);
        }

        [HideFromIl2Cpp]
        private (Renderer, int)[] FindAccentSlots()
        {
            var material = accentMaterial?.Get();

            if (material == null)
                return [];

            var slots = new List<(Renderer, int)>();

            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;

                for (var slot = 0; slot < materials.Length; slot++)
                {
                    if (materials[slot] == material)
                        slots.Add((renderer, slot));
                }
            }

            return slots.ToArray();
        }
        
        [HideFromIl2Cpp]
        internal void ApplyTrinket(string serializedSpec)
        {
            if (serializedSpec == _trinketSpec)
                return;

            _trinketSpec = serializedSpec;
            var version = ++_trinketVersion;
            DestroyTrinket();

            var anchor = trinketAnchor?.Get();

            if (anchor == null || !TrinketSpec.TryParse(serializedSpec, out var spec) || spec.IsEmpty)
                return;

            Trinkets.LoadPrefab(spec.Barcode, prefab =>
            {
                // The camera may have been despawned, or given a different trinket, while the bundle loaded
                if (this == null || version != _trinketVersion || prefab == null)
                    return;

                _trinket = Trinkets.Build(prefab, spec, anchor, gameObject.layer);
            });
        }

        [HideFromIl2Cpp]
        private void DestroyTrinket()
        {
            if (_trinket != null)
                Destroy(_trinket);

            _trinket = null;
        }
#endif
    }
    
    internal sealed class KeychainPhysics
    {
        // A proxy this far from where it should hang means the camera teleported (or was pooled)
        private const float SnapDistance = .25f;

        private readonly Rigidbody _body;
        private readonly Transform[] _bones;
        private readonly Quaternion[] _restRotations;
        private readonly Rigidbody[] _proxies;
        private readonly Vector3 _attachAnchor;

        public static KeychainPhysics? Create(Rigidbody? body, Transform? root, Transform? link, Rigidbody? rootProxy, Rigidbody? linkProxy)
        {
            if (body == null || root == null || link == null || rootProxy == null || linkProxy == null)
                return null;

            return new KeychainPhysics(body, new[] { root, link }, new[] { rootProxy, linkProxy });
        }

        private KeychainPhysics(Rigidbody body, Transform[] bones, Rigidbody[] proxies)
        {
            _body = body;
            _bones = bones;
            _restRotations = new Quaternion[bones.Length];
            _proxies = proxies;
            _attachAnchor = body.transform.InverseTransformPoint(bones[0].position);

            for (var i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                _restRotations[i] = bone.localRotation;

                var proxy = proxies[i];
                // Move it out from the camera, so it doesn't effect the chain
                proxy.transform.SetParent(null, true);

                var length = Vector3.Distance(bone.position, GetEnd(bones, i).position);

                proxy.solverIterations = 12;
                proxy.solverVelocityIterations = 4;
                proxy.centerOfMass = Vector3.up * (length * .5f);

                const float MinInertia = 0.000001f;
                proxy.inertiaTensor = Vector3.one * Mathf.Max(proxy.mass * length * length / 3f, MinInertia);
                proxy.inertiaTensorRotation = Quaternion.identity;
            }
        }

        private static Transform GetEnd(Transform[] bones, int index)
        {
            if (index + 1 < bones.Length)
                return bones[index + 1];

            var bone = bones[index];
            return bone.childCount > 0 ? bone.GetChild(0) : bone;
        }

        public void Enable()
        {
            Snap();

            foreach (var proxy in _proxies)
                proxy.gameObject.SetActive(true);
        }

        public void Disable()
        {
            foreach (var proxy in _proxies)
            {
                if (proxy != null)
                    proxy.gameObject.SetActive(false);
            }
        }

        public void Destroy()
        {
            foreach (var proxy in _proxies)
            {
                if (proxy != null)
                    UnityEngine.Object.Destroy(proxy.gameObject);
            }
        }

        public void FixedTick()
        {
            var attachPoint = _body.transform.TransformPoint(_attachAnchor);

            if (Vector3.Distance(_proxies[0].position, attachPoint) > SnapDistance)
                Snap();
        }

        public void LateTick()
        {
            for (var i = 0; i < _bones.Length; i++)
                _bones[i].rotation = _proxies[i].transform.rotation;
        }

        private void Snap()
        {
            for (var i = 0; i < _bones.Length; i++)
            {
                var bone = _bones[i];
                bone.localRotation = _restRotations[i];

                var proxy = _proxies[i];
                proxy.transform.SetPositionAndRotation(bone.position, bone.rotation);
                proxy.position = bone.position;
                proxy.rotation = bone.rotation;
                proxy.velocity = _body.velocity;
                proxy.angularVelocity = Vector3.zero;
            }
        }
    }
}
