using BoneSnap.Capture;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;

namespace BoneSnap.SDK
{
    public interface ISnapHUDState
    {}
    
    public class IdleState : ISnapHUDState
    {}

    public class PreviewState(Camera? previewCamera = null, Transform? previewAnchor = null, bool previewScreen = false, Func<CaptureParams>? previewParams = null) : ISnapHUDState
    {
        public Camera? PreviewCamera = previewCamera;
        public Transform? PreviewAnchor = previewAnchor;
        // Preview the game window instead of PreviewCamera
        public bool PreviewScreen = previewScreen;
        // Camera adjustments to apply to PreviewCamera's renders
        public Func<CaptureParams>? PreviewParams = previewParams;
    }

    public class CountdownState(float duration, Camera? previewCamera = null, Transform? previewAnchor = null, bool previewScreen = false, Func<CaptureParams>? previewParams = null)
        : PreviewState(previewCamera, previewAnchor, previewScreen, previewParams)
    {
        public float Duration = duration;
    }
    
    public class EncodingInProgressState : ISnapHUDState
    {}
    
    public class SaveCompleteState(string fileName, float encodeTime) : ISnapHUDState
    {
        public string FileName = fileName;
        public float EncodeTime = encodeTime;
    }
    
    public class DisplayDebugState(string text) : ISnapHUDState
    {
        public string Text = text;
    }
    
    /// <summary>
    /// Lazy-initialised state manager for BoneSnap's HUD.
    /// </summary>
    [RegisterTypeInIl2Cpp]
    public class SnapHUD : MonoBehaviour
    {
        public SnapHUD(IntPtr intPtr) : base(intPtr) { }
        
        private enum State
        {
            Idle,
            Countdown,
            Encoding,
            Saved,
            Debug
        }
        
        private TMP_Text _textMesh;
        private PreviewQuad _preview;
        private ISnapHUDState _currentState;
        private float _stateStart;
        private float ElapsedStateTime => Time.timeSinceLevelLoad - _stateStart;

        private Vector3 _positionOffset = new(0, -0.3f, .7f);
        private Vector3 _rotationOffset = new(5, 0, 0);
        private Vector3 _previewOffset = new(0, 0.11f, 0);

        private static bool PreviewEnabled => Main.Prefs.ShowPreview.Value;

        private static SnapHUD _instance;

        /// <summary>
        /// Raised after the HUD swaps state.
        /// </summary>
        public static event Action? StateChanged;

        /// <summary>
        /// Whether the HUD is showing a preview without a countdown.
        /// </summary>
        public static bool IsPreviewing => _instance && _instance._currentState is PreviewState and not CountdownState;
        
        /// <summary>
        /// Gets the current Instance (or creates one if it doesn't exist).
        /// </summary>
        public static SnapHUD GetInstance()
        {
            if (!_instance)
                _instance = new GameObject("BoneSnap HUD") {transform = { parent = BoneLib.Player.ControllerRig.transform } } .AddComponent<SnapHUD>();

            return _instance;
        }

        private void Init()
        {
            _preview = PreviewQuad.Create(transform);
            _preview.transform.localPosition = _previewOffset;

            _textMesh = gameObject.AddComponent<TextMeshPro>();
            _textMesh.richText = true;
            _textMesh.fontSize = 0.3f;
            _textMesh.alignment = TextAlignmentOptions.Center;
            _textMesh.rectTransform.sizeDelta = new Vector2(2, 1);

            var arlon = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault((x) => x.name == "arlon-medium SDF");

            if (arlon)
            {
                _textMesh.font = arlon;
                _textMesh.fontSharedMaterial = arlon.material;
            }
            else
                MelonLogger.Warning("Failed to locate TMP_FontAsset! Falling back to the default TextMeshPro font.");

            PreviewQuad.DrawOnTop(_textMesh.fontMaterial);
        }

        public void Show() => GetInstance()?.gameObject.SetActive(true);
        public void Hide() => GetInstance()?.gameObject.SetActive(false);
        public bool IsActive() => GetInstance()?.gameObject.activeSelf ?? false;

        private void LateUpdate()
        {
            const float lerpMultiplier = 60 * 0.2f;
            var head = BoneLib.Player.Head;

            UpdatePosition(head,
                Time.deltaTime * lerpMultiplier);
            
            if (_currentState is CountdownState countdown)
            {
                countdown.Duration -= Time.deltaTime;
                _textMesh.text = $"{Mathf.Max(countdown.Duration, 0):0.00}";
            }
            else if (_currentState is EncodingInProgressState)
            {
                _textMesh.text = "Encoding...";
                _textMesh.alpha = Mathf.Abs(Mathf.Sin(ElapsedStateTime)) / 3;
            }
            else if (_currentState is SaveCompleteState saved)
            {
                _textMesh.text = $"<i>{saved.FileName}</i>\nSaved!";
                _textMesh.alpha = 1 - ElapsedStateTime / 3;

                if (_textMesh.alpha <= 0)
                {
                    SwapState(new IdleState());
                }
            }
            else if (_currentState is DisplayDebugState debug)
            {
                _textMesh.text = debug.Text;
            }

            _preview.Alpha = _currentState is EncodingInProgressState ? 1 : _textMesh.alpha;
        }

        /// <summary>
        /// Freezes the preview on the provided texture, preventing it from updating until cleared.
        /// </summary>
        public void FreezePreview(Texture capture)
        {
            if (!_textMesh)
                Init();

            if (PreviewEnabled)
                _preview.Freeze(capture);
        }

        private void UpdatePosition(Transform head, float lerp)
        {
            var posGoal = head.position + head.rotation * _positionOffset; // TransformPoint takes into account object scale, which we don't want
            var rotGoal = head.rotation * Quaternion.Euler(_rotationOffset);

            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, posGoal, lerp),
                Quaternion.Slerp(transform.rotation, rotGoal, lerp)
            );
        }

        public void TransitionIn(Transform head)
        {
            UpdatePosition(head, 1);
            transform.Translate(0, 0, 2, Space.Self);
            transform.Rotate(0, 0, 45);
        }

        public void SwapState(ISnapHUDState state)
        {
            if (!_textMesh)
                Init();
            
            switch (state)
            {
                case IdleState idle:
                    this.enabled = false;
                    _textMesh.enabled = false;
                    // The property is called alpha but it actually means opacity... fak you
                    _textMesh.alpha = 1;
                    _preview.Clear();
                    break;
                case CountdownState countdown:
                case EncodingInProgressState encoding:
                case SaveCompleteState saved:
                case DisplayDebugState debug:
                    this.enabled = true;
                    _textMesh.enabled = true;
                    TransitionIn(BoneLib.Player.Head);
                    break;
                case PreviewState preview:
                    this.enabled = true;
                    // No timer, just the preview
                    _textMesh.enabled = false;
                    _textMesh.alpha = 1;
                    TransitionIn(BoneLib.Player.Head);
                    break;
            }

            // Swap to live preview. Previews requested directly are shown regardless of the countdown preview setting
            if (state is PreviewState previewState)
            {
                if (state is CountdownState && !PreviewEnabled)
                    _preview.Clear();
                else if (previewState.PreviewScreen)
                    _preview.TrackScreen();
                else if (previewState.PreviewCamera is { } camera && camera)
                    _preview.Track(camera, previewState.PreviewAnchor, previewState.PreviewParams);
                else
                    _preview.Clear();
            }

            _stateStart = Time.timeSinceLevelLoad;
            _currentState = state;

            StateChanged?.Invoke();
        }

        public void HiUnityExplorerThisIsForDebugging(string text)
        {
            SwapState(new DisplayDebugState(text));
        }
    }
}
