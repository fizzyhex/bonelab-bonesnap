using BoneSnap.Capture;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoneSnap.SDK
{
    /// <summary>
    /// The quad for <see cref="SnapHUD"/> that previews what's being captured.
    /// </summary>
    [RegisterTypeInIl2Cpp]
    public class PreviewQuad : MonoBehaviour
    {
        private static readonly int Surface = Shader.PropertyToID("_Surface");
        private static readonly int Blend = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
        private static readonly int UnityGuizTestMode = Shader.PropertyToID("unity_GUIZTestMode");
        private static readonly int ZTest = Shader.PropertyToID("_ZTest");

        public PreviewQuad(IntPtr intPtr) : base(intPtr) { }

        private const int TextureHeight = 256;
        private const float QuadHeight = 0.1f;

        private MeshRenderer _renderer;
        private RenderTexture _texture;

        private Camera? _camera;
        private Transform? _anchor;
        private Func<CaptureParams>? _captureParams;
        private Renderer[] _hudRenderers = [];

        private bool _trackingScreen;
        private RenderTexture? _screenTexture;

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
                MelonLogger.Warning($"Failed to locate unlit shader for {nameof(PreviewQuad)}!");

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
            material.SetFloat(Surface, 1); // Transparent
            material.SetFloat(Blend, 0); // Alpha
            material.SetFloat(SrcBlend, (float)BlendMode.SrcAlpha);
            material.SetFloat(DstBlend, (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat(ZWrite, 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        public static void DrawOnTop(Material material)
        {
            material.SetFloat(UnityGuizTestMode, (float)CompareFunction.Always); // UI & TextMeshPro shaders
            material.SetFloat(ZTest, (float)CompareFunction.Always); // URP shaders that expose it
            material.renderQueue = (int)RenderQueue.Overlay;
        }

        /// <summary>
        /// Renders <paramref name="camera"/> onto the quad every frame, until it's frozen or cleared.
        /// </summary>
        public void Track(Camera camera, Transform? anchor = null, Func<CaptureParams>? captureParams = null)
        {
            _camera = camera;
            _anchor = anchor;
            _captureParams = captureParams;
            _hudRenderers = transform.parent.GetComponentsInChildren<Renderer>().ToArray();
            _trackingScreen = false;

            enabled = true;
            _renderer.enabled = true;
        }

        /// <summary>
        /// Copies the game window onto the quad every frame until frozen or cleared.
        /// </summary>
        /// <remarks>
        /// The window includes the HUD, so the preview recursively contains itself when the HUD is in view.
        /// </remarks>
        public void TrackScreen()
        {
            _camera = null;
            _anchor = null;
            _trackingScreen = true;

            enabled = true;
            _renderer.enabled = true;
        }

        /// <summary>
        /// Displays a copy of <paramref name="source"/> on the quad until it's next tracking or cleared.
        /// </summary>
        public void Freeze(Texture source)
        {
            ScreenCopy.BlitUpright(source, GetTexture(source.width, source.height));

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
            if (_trackingScreen)
            {
                CopyScreen();
                return;
            }

            if (_camera is not { } camera || !camera)
                return;

            Render(camera);
        }

        private void Render(Camera camera)
        {
            var target = GetTexture(camera.pixelWidth, camera.pixelHeight);
            var previousTarget = camera.targetTexture;
            var cameraTransform = camera.transform;
            var previousPosition = cameraTransform.position;
            var previousRotation = cameraTransform.rotation;

            // Keep the HUD out of its own preview
            var hiddenRenderers = _hudRenderers.Where(hudRenderer => hudRenderer && hudRenderer.enabled).ToArray();

            foreach (var hudRenderer in hiddenRenderers)
                hudRenderer.enabled = false;

            if (_anchor is { } anchor && anchor)
                cameraTransform.SetPositionAndRotation(anchor.position, anchor.rotation);

            var adjustments = _captureParams is { } captureParams ? CameraAdjustments.Apply(camera, captureParams()) : null;

            try
            {
                camera.targetTexture = target;
                camera.Render();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                adjustments?.Dispose();

                if (_anchor)
                    cameraTransform.SetPositionAndRotation(previousPosition, previousRotation);

                foreach (var hudRenderer in hiddenRenderers)
                    hudRenderer.enabled = true;
            }
        }

        /// <summary>
        /// Displays the previous frame's window copy on alternate frames if it matches the current
        /// window size, then queues a new copy.
        /// </summary>
        private void CopyScreen()
        {
            if (_screenTexture is { } screenTexture && screenTexture && ScreenCopy.IsScreenSized(screenTexture))
            {
                // Reading the copy target every frame stops Unity from writing new copies into it, for some reason?
                // It may just be a D3D11 issue, unsure if it happens on Quest.
                if (Time.frameCount % 2 == 0)
                    ScreenCopy.BlitUpright(screenTexture, GetTexture(screenTexture.width, screenTexture.height));
            }
            else
            {
                // If the screen texture is missing or the window has resized, a fresh copy is
                // available next frame.
                if (_screenTexture)
                    Destroy(_screenTexture);

                _screenTexture = ScreenCopy.CreateScreenTexture();
            }

            ScreenCopy.RequestInto(_screenTexture);
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

            if (_screenTexture)
                Destroy(_screenTexture);
        }
    }
}
