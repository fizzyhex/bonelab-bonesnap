using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoneSnap.SDK
{
    /// <summary>
    /// A small quad on the <see cref="SnapHUD"/> that previews what's being captured.
    /// </summary>
    [RegisterTypeInIl2Cpp]
    public class PreviewQuad : MonoBehaviour
    {
        public PreviewQuad(IntPtr intPtr) : base(intPtr) { }

        private const int TextureHeight = 256;
        private const float QuadHeight = 0.1f;

        private MeshRenderer _renderer;
        private RenderTexture _texture;

        private Camera _camera;
        private Transform? _anchor;
        private Renderer[] _hudRenderers = [];

        public static PreviewQuad Create(Transform parent)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "BoneSnap Preview";
            quad.transform.SetParent(parent, false);

            // The HUD follows the player's head, so it mustn't collide with anything
            DestroyImmediate(quad.GetComponent<Collider>());

            return quad.AddComponent<PreviewQuad>();
        }

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;

            // Shader.Find can't see shaders loaded from asset bundles
            var shaders = Resources.FindObjectsOfTypeAll<Shader>();
            var overlay = shaders.FirstOrDefault(shader => shader.name == "UI/Default");
            var unlit = shaders.FirstOrDefault(shader => shader.name == "Universal Render Pipeline/Unlit");

            if (overlay)
                _renderer.material.shader = overlay;
            else if (unlit)
            {
                _renderer.material.shader = unlit;
                MakeTransparent(_renderer.material);
            }
            else
                MelonLogger.Warning("Failed to locate an unlit shader! The capture preview will be lit by the scene.");

            DrawOnTop(_renderer.material);
            Clear();
        }

        /// <summary>
        /// The quad's opacity (0-1).
        /// </summary>
        public float Alpha
        {
            set
            {
                var color = _renderer.material.color;
                color.a = value;
                _renderer.material.color = color;
            }
        }

        /// <summary>
        /// Switches a URP Unlit material to alpha blending, matching what the editor's material inspector does.
        /// </summary>
        private static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1); // Transparent
            material.SetFloat("_Blend", 0); // Alpha
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        public static void DrawOnTop(Material material)
        {
            material.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always); // UI & TextMeshPro shaders
            material.SetFloat("_ZTest", (float)CompareFunction.Always); // URP shaders that expose it
            material.renderQueue = (int)RenderQueue.Overlay;
        }

        /// <summary>
        /// Renders <paramref name="camera"/> onto the quad every frame, until it's frozen or cleared.
        /// </summary>
        /// <param name="anchor">Optional transform to move the camera to before each render, for cameras that aren't following anything themselves.</param>
        public void Track(Camera camera, Transform? anchor = null)
        {
            _camera = camera;
            _anchor = anchor;
            _hudRenderers = transform.parent.GetComponentsInChildren<Renderer>().ToArray();

            enabled = true;
            _renderer.enabled = true;
        }

        /// <summary>
        /// Displays a copy of <paramref name="source"/> on the quad until it's next tracking or cleared.
        /// </summary>
        public void Freeze(Texture source)
        {
            Graphics.Blit(source, GetTexture(source.width, source.height));

            enabled = false;
            _renderer.enabled = true;
        }

        public void Clear()
        {
            enabled = false;
            _renderer.enabled = false;
        }

        // Rendered after everything has moved for the frame
        private void LateUpdate()
        {
            if (!_camera)
                return;

            if (_anchor)
                _camera.transform.SetPositionAndRotation(_anchor.position, _anchor.rotation);

            Render(_camera);
        }

        private void Render(Camera camera)
        {
            var target = GetTexture(camera.pixelWidth, camera.pixelHeight);
            var previousTarget = camera.targetTexture;

            // Keep the HUD out of its own preview
            var hiddenRenderers = _hudRenderers.Where(hudRenderer => hudRenderer && hudRenderer.enabled).ToArray();

            foreach (var hudRenderer in hiddenRenderers)
                hudRenderer.enabled = false;

            try
            {
                camera.targetTexture = target;
                camera.Render();
            }
            finally
            {
                camera.targetTexture = previousTarget;

                foreach (var hudRenderer in hiddenRenderers)
                    hudRenderer.enabled = true;
            }
        }

        /// <summary>
        /// Gets the preview texture, (re)creating it and resizing the quad to match the source's aspect ratio.
        /// </summary>
        private RenderTexture GetTexture(int sourceWidth, int sourceHeight)
        {
            var aspect = (float)sourceWidth / sourceHeight;
            var width = Mathf.Max(1, Mathf.RoundToInt(TextureHeight * aspect));

            if (_texture && _texture.width == width)
                return _texture;

            if (_texture)
                Destroy(_texture);

            _texture = new RenderTexture(width, TextureHeight, 24, RenderTextureFormat.Default, RenderTextureReadWrite.sRGB);
            _renderer.material.mainTexture = _texture;
            transform.localScale = new Vector3(QuadHeight * aspect, QuadHeight, 1);

            return _texture;
        }

        private void OnDestroy()
        {
            if (_texture)
                Destroy(_texture);
        }
    }
}
