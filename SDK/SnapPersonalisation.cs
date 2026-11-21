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
    /// Swings a camera's keychain, and (with BoneSnap installed) applies the player's accent colour and keychain trinket.
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
        public Il2CppReferenceField<Transform>? trinketAnchor;
        public Il2CppReferenceField<GameObject>? accentSync;
        public Il2CppReferenceField<GameObject>? trinketSync;

        private Transform? ChainRoot => chainRoot?.Get();
        private Transform? ChainLink => chainLink?.Get();
        internal GameObject? AccentSync => accentSync?.Get();
        internal GameObject? TrinketSync => trinketSync?.Get();
#else
        [Tooltip("Every renderer slot on this camera using this material is tinted with the player's accent colour")]
        public Material accentMaterial;
        [Tooltip("First keychain bone, attached to the camera")]
        public Transform chainRoot;
        [Tooltip("Second keychain bone, hanging from the first")]
        public Transform chainLink;
        [Tooltip("Where the keychain trinket hangs from (the chain's end)")]
        public Transform trinketAnchor;
        [Tooltip("Optional - GameObject with a Fusion RPCString, syncing the spawner's accent colour")]
        public GameObject accentSync;
        [Tooltip("Optional - GameObject with a Fusion RPCString, syncing the spawner's keychain trinket")]
        public GameObject trinketSync;

        private Transform ChainRoot => chainRoot;
        private Transform ChainLink => chainLink;
#endif

        private KeychainPhysics? _keychain;

        private void Awake()
        {
            _keychain = KeychainPhysics.Create(GetComponent<Rigidbody>(), ChainRoot, ChainLink);
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
            // Zone culling disables cameras too, so the style (and its sync) outlives this
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
            // Fusion decides whose style a networked camera shows; everything else shows ours.
            if (!FusionSync.Tick(this))
                ApplyLocalStyle();
        }

        /// <summary>
        /// Re-applies the local player's style to every camera showing it, and re-syncs the ones they spawned.
        /// </summary>
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

        /// <summary>
        /// Tints the camera body and its UI. An empty or invalid hex restores the authored colours.
        /// </summary>
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

        /// <summary>
        /// Hangs a trinket from the keychain, from a serialized <see cref="TrinketSpec"/>. Empty removes it.
        /// </summary>
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

    /// <summary>
    /// Drives the keychain's skinned bones with collider-less joint proxies, so the chain can swing
    /// without touching anything or tugging on the camera.
    /// </summary>
    internal sealed class KeychainPhysics
    {
        private const float LinkMass = .01f;
        private const float SwingLimit = 80f;
        private const float TwistLimit = 25f;
        // A proxy this far from where it should hang means the camera teleported (or was pooled)
        private const float SnapDistance = .25f;

        private readonly Rigidbody _body;
        private readonly Transform[] _bones;
        private readonly Quaternion[] _restRotations;
        private readonly Rigidbody[] _proxies;
        private readonly Vector3 _attachAnchor;

        public static KeychainPhysics? Create(Rigidbody? body, Transform? root, Transform? link)
        {
            if (body == null || root == null || link == null)
                return null;

            return new KeychainPhysics(body, new[] { root, link });
        }

        private KeychainPhysics(Rigidbody body, Transform[] bones)
        {
            _body = body;
            _bones = bones;
            _restRotations = new Quaternion[bones.Length];
            _proxies = new Rigidbody[bones.Length];
            _attachAnchor = body.transform.InverseTransformPoint(bones[0].position);

            var connected = body;

            for (var i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                _restRotations[i] = bone.localRotation;

                var proxy = new GameObject($"{body.name} (Keychain Link {i + 1})");
                proxy.transform.SetPositionAndRotation(bone.position, bone.rotation);

                // The bone's +Y runs down the chain, to the next bone
                var length = Vector3.Distance(bone.position, GetEnd(bones, i).position);

                var rigidbody = proxy.AddComponent<Rigidbody>();
                rigidbody.mass = LinkMass;
                rigidbody.drag = .5f;
                rigidbody.angularDrag = 2f;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.solverIterations = 12;
                rigidbody.solverVelocityIterations = 4;
                rigidbody.centerOfMass = Vector3.up * (length * .5f);
                // There are no colliders to derive inertia from, so use a thin rod's
                rigidbody.inertiaTensor = Vector3.one * Mathf.Max(LinkMass * length * length / 3f, 1e-6f);
                rigidbody.inertiaTensorRotation = Quaternion.identity;

                var joint = proxy.AddComponent<ConfigurableJoint>();
                joint.autoConfigureConnectedAnchor = false;
                joint.connectedBody = connected;
                joint.anchor = Vector3.zero;
                joint.connectedAnchor = connected.transform.InverseTransformPoint(bone.position);
                joint.axis = Vector3.up;
                joint.secondaryAxis = Vector3.forward;
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;
                joint.lowAngularXLimit = new SoftJointLimit { limit = -TwistLimit };
                joint.highAngularXLimit = new SoftJointLimit { limit = TwistLimit };
                joint.angularYLimit = new SoftJointLimit { limit = SwingLimit };
                joint.angularZLimit = new SoftJointLimit { limit = SwingLimit };
                joint.projectionMode = JointProjectionMode.PositionAndRotation;
                joint.projectionDistance = .005f;
                joint.projectionAngle = 5f;
                joint.enablePreprocessing = false;
                // Treat the camera as immovable, so the chain never tugs on the player's hand
                if (i == 0)
                    joint.connectedMassScale = .001f;

                _proxies[i] = rigidbody;
                connected = rigidbody;
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
            // Parents first, so each link's world rotation lands after its parent has moved
            for (var i = 0; i < _bones.Length; i++)
                _bones[i].rotation = _proxies[i].transform.rotation;
        }

        /// <summary>
        /// Puts the chain back at rest under the camera.
        /// </summary>
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
