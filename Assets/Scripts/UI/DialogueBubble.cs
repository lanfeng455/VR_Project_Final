using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 全场景通用的对话气泡：
    /// 负责显示/隐藏动画、富文本打字机，并在打印完成后等待 TTS 播放结束。
    /// </summary>
    public sealed class DialogueBubble : MonoBehaviour
    {
        public static DialogueBubble Instance { get; private set; }

        [Header("引用")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform background;
        [SerializeField] private TMP_Text contentText;

        [Header("显示/隐藏动画")]
        [SerializeField] private float fadeDuration = 0.18f;
        [SerializeField] private float slideOffset = 12f;

        [Header("打字机")]
        [SerializeField] private float charactersPerSecond = 30f;

        [Header("TTS")]
        [Tooltip("未接入 TTS 时，打字完成后停留的秒数。")]
        [SerializeField] private float holdSecondsWithoutTTS = 2f;
        [Tooltip("等待 TTS 播放结束的最长时间，避免一直不隐藏。")]
        [SerializeField] private float ttsTimeoutSeconds = 30f;

        [Header("常驻")]
        [Tooltip("跨场景保留对话UI。")]
        [SerializeField] private bool persistAcrossScenes = true;

        [Header("HUD 跟随")]
        [Tooltip("勾选后固定显示在视线前方，而不是固定在场景里。")]
        [SerializeField] private bool followView = true;
        [Tooltip("气泡到眼睛的距离（米）。")]
        [SerializeField] private float viewDistance = 1.8f;
        [Tooltip("背景条宽度占视野宽度的比例。")]
        [SerializeField, Range(0.2f, 1f)]
        private float viewWidthRatio = 0.45f;
        [Tooltip("气泡底边比视线（水平方向）低多少米。")]
        [SerializeField, Range(0.2f, 1.6f)]
        private float metersBelowEye = 0.9f;
        [Tooltip("被近处实体挡住时，气泡最多能拉近到多少米。")]
        [SerializeField, Range(0.5f, 1.5f)]
        private float minViewDistance = 0.9f;
        [Tooltip("留空时自动使用 Camera.main（VR 中为中心眼相机）。")]
        [SerializeField] private Camera targetCamera;

        /// <summary>
        /// 打字完成、需要朗读时触发。
        /// TTS 系统订阅后，在播放结束时调用 NotifySpeechFinished()。
        /// </summary>
        public event Action<string> SpeechRequested;

        /// <summary>
        /// 整句文字打印完成时触发（在 SpeechRequested 之前）。
        /// </summary>
        public event Action<string> DisplayCompleted;

        private Coroutine routine;
        private Vector2 backgroundHome;
        private Vector2 textHome;
        private float currentOffset;
        private bool speechFinished;
        private bool isShowing;

        public bool IsShowing => isShowing;
        public bool IsTyping { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (persistAcrossScenes)
                DontDestroyOnLoad(gameObject);

            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();

            if (background != null)
                backgroundHome = background.anchoredPosition;

            if (contentText != null)
            {
                textHome = contentText.rectTransform.anchoredPosition;
                contentText.text = string.Empty;
                contentText.maxVisibleCharacters = 0;

                // 背景条在层级里排在文字之后，UGUI 后画的会盖住先画的，
                // 把文字挪到最后一个，保证它永远画在黑条之上。
                contentText.transform.SetAsLastSibling();
            }

            // 没有内容时保持隐藏。
            ApplyState(0f, -slideOffset);

            SetupOverlayLayer();

#if UNITY_EDITOR
            PrepareEditorPreview();
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器里没有合成器：OVROverlayCanvas 只会留下一个 imposter 面片，
        /// 而字幕本体在层 3（主相机不渲染）。这里无条件关掉 overlay 并把层改回 0，
        /// 让 Game 视图直接看到真实的字幕 Canvas，和电视屏的处理保持一致。
        /// 注意：这一步不能再用 isHmdPresent 做条件——接着头显时它会把整块 UI 留在层 3，
        /// Game 视图就什么都看不到了。
        /// </summary>
        private void PrepareEditorPreview()
        {
            var overlay = GetComponent<OVROverlayCanvas>();

            if (overlay != null)
                overlay.enabled = false;

            SetLayerRecursively(gameObject, 0);
        }

        private static void SetLayerRecursively(
            GameObject target,
            int layer)
        {
            target.layer = layer;

            foreach (Transform child in target.transform)
                SetLayerRecursively(child.gameObject, layer);
        }
#endif

        /// <summary>
        /// 沿着视线（以及视线下方到气泡底边的方向）探测场景实体，
        /// 把气泡距离压到实体之前，避免被桌面、墙体切掉。
        /// </summary>
        private float ClampViewDistance(Camera camera, Vector3 forward)
        {
            Vector3 eye = camera.transform.position;
            float allowed = viewDistance;

            if (Physics.Raycast(
                    eye,
                    forward,
                    out RaycastHit hit,
                    allowed,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                allowed = hit.distance;
            }

            // 桌面、台面通常低于视线，只探视平线是探不到的。
            Vector3 toBottom =
                forward * viewDistance + Vector3.down * metersBelowEye;
            float bottomLength = toBottom.magnitude;

            if (bottomLength > 0.01f &&
                Physics.Raycast(
                    eye,
                    toBottom / bottomLength,
                    out RaycastHit bottomHit,
                    bottomLength,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                allowed = Mathf.Min(
                    allowed,
                    viewDistance * (bottomHit.distance / bottomLength)
                );
            }

            return Mathf.Max(minViewDistance, allowed - 0.15f);
        }

        /// <summary>
        /// 头显里把字幕放到 overlay 层渲染，避免被电视屏的 PunchAHole 从眼睛画面里擦掉。
        /// 同时保留普通 Canvas 渲染，PC 的 Game 视图依然能看到字幕。
        /// </summary>
        private void SetupOverlayLayer()
        {
            if (!OVRManager.isHmdPresent)
                return;

            var overlay = GetComponent<OVROverlayCanvas>();

            if (overlay == null)
                overlay = gameObject.AddComponent<OVROverlayCanvas>();

            // 必须用 OverlayType.Overlay：它渲染在场景内容之上。
            // 预制体里遗留的是 PunchAHole（Underlay，渲染在场景内容之后），
            // 所以气泡只要贴近桌子/墙/电视，就会被实体盖住或"插进"模型里。
            overlay.compositionMode =
                OVROverlayCanvas.CompositionMode.DepthTested;
            overlay.shape = OVROverlayCanvas.CanvasShape.Flat;
            overlay.layer = 5;
            overlay.maxTextureSize = 2048;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void LateUpdate()
        {
            if (!followView)
                return;

            Camera camera =
                targetCamera != null ? targetCamera : Camera.main;

            if (camera == null)
                return;

            RectTransform canvasRect = (RectTransform)transform;

            // 只取相机的水平朝向（yaw）来放置。
            // 不能直接用相机朝向：低头看屏幕时气泡会跟着转到脚下，
            // 距离一远就沉到地面以下（头显里就是"嵌进地里"、Game 视图里整块被地板挡住）。
            Vector3 forward = camera.transform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;

            forward.Normalize();

            // 1.8 米的正前方经常正好是桌子、墙或电视本体，
            // 气泡会被近处实体切掉一半。用射线把气泡压到实体之前。
            float distance = ClampViewDistance(camera, forward);

            // 只量横向：气泡宽度要始终占视野宽度的固定比例。
            // 直接问相机"实际会怎么投影"，比读 FOV 可靠（XR 下两者不一致）。
            Vector3 probeLeft =
                camera.transform.TransformPoint(
                    new Vector3(-1f, 0f, distance));

            Vector3 probeRight =
                camera.transform.TransformPoint(
                    new Vector3(1f, 0f, distance));

            float viewportPerMeterX =
                (camera.WorldToViewportPoint(probeRight).x -
                 camera.WorldToViewportPoint(probeLeft).x) * 0.5f;

            // 视野在气泡距离处的半宽（米）。
            float halfWidth =
                viewportPerMeterX > 0f
                    ? 0.5f / viewportPerMeterX
                    : distance *
                      Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) *
                      camera.aspect;

            // 让背景条宽度始终占视野宽度的固定比例，
            // PC 和头显的构图因此保持一致。
            float referenceWidth =
                background != null
                    ? background.rect.width
                    : canvasRect.rect.width;

            float scale =
                referenceWidth > 0f
                    ? viewWidthRatio * halfWidth * 2f / referenceWidth
                    : canvasRect.localScale.x;

            // 背景条底边相对 Canvas 中心（pivot 在中心）的像素偏移（向下为负）。
            // 用初始位置 backgroundHome，而不是被淡入淡出动画改过的当前位置，
            // 否则每帧的定位会跟着滑入/滑出一起漂。
            float barBottomPx =
                background != null
                    ? backgroundHome.y
                      - background.rect.height * background.pivot.y
                      - canvasRect.rect.height * 0.5f
                    : -canvasRect.rect.height * 0.5f;

            Vector3 position =
                camera.transform.position + forward * distance;

            // 底边比视线低 metersBelowEye，再把 Canvas 中心从底边抬回来。
            position.y -= metersBelowEye;
            position.y -= barBottomPx * scale;

            transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(forward, Vector3.up)
            );

            transform.localScale = Vector3.one * scale;

#if UNITY_EDITOR
            LogLayout(camera);
#endif
        }

#if UNITY_EDITOR
        private static float nextLayoutLogTime;

        /// <summary>临时诊断：每秒打印一次气泡的真实运行时状态。</summary>
        private void LogLayout(Camera camera)
        {
            if (Time.unscaledTime < nextLayoutLogTime)
                return;

            nextLayoutLogTime = Time.unscaledTime + 1f;

            Vector3 viewport =
                camera.WorldToViewportPoint(transform.position);

            float alpha = canvasGroup != null ? canvasGroup.alpha : -1f;
            Canvas canvas = GetComponent<Canvas>();
            bool canvasEnabled = canvas != null && canvas.enabled;

            Debug.Log(
                $"[Bubble] cam={camera.name} main={camera == Camera.main} " +
                $"fov={camera.fieldOfView:F1} aspect={camera.aspect:F2} " +
                $"vp={viewport.x:F2},{viewport.y:F2},{viewport.z:F2} " +
                $"scale={transform.lossyScale.x:F4} alpha={alpha:F2} " +
                $"showing={isShowing} layer={gameObject.layer} " +
                $"canvas={canvasEnabled} active={gameObject.activeInHierarchy}");
        }
#endif

        /// <summary>显示一句来自大模型的内容（打字机效果）。</summary>
        public void ShowText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Hide();
                return;
            }

            Restart();

            routine = StartCoroutine(
                ShowRoutine(text, true)
            );
        }

        /// <summary>立即显示完整内容，跳过打字机。</summary>
        public void ShowImmediate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Hide();
                return;
            }

            Restart();

            routine = StartCoroutine(
                ShowRoutine(text, false)
            );
        }

        /// <summary>
        /// 常驻显示一段短提示（例如录音指示），直到外部调用 Hide()。
        /// 复用同一个气泡，不新增任何 UI 容器。
        /// </summary>
        public void ShowSticky(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Hide();
                return;
            }

            Restart();

            routine = StartCoroutine(StickyRoutine(text));
        }

        private IEnumerator StickyRoutine(string text)
        {
            isShowing = true;
            IsTyping = false;
            speechFinished = true;

            if (contentText != null)
            {
                contentText.text = text;
                contentText.maxVisibleCharacters = int.MaxValue;
                contentText.ForceMeshUpdate();
            }

            yield return FadeTo(1f, 0f, fadeDuration);

            // 不自隐藏：等调用方在录音结束时调 Hide()。
            routine = null;
        }

        /// <summary>播放隐藏动画。</summary>
        public void Hide()
        {
            Restart();

            if (!isShowing &&
                canvasGroup != null &&
                canvasGroup.alpha <= 0f)
            {
                return;
            }

            routine = StartCoroutine(HideRoutine());
        }

        /// <summary>TTS 播放结束时由外部调用。</summary>
        public void NotifySpeechFinished()
        {
            speechFinished = true;
        }

        private void Restart()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }
        }

        private IEnumerator ShowRoutine(
            string text,
            bool useTypewriter)
        {
            isShowing = true;
            speechFinished = false;
            IsTyping = useTypewriter;

            if (contentText != null)
            {
                contentText.text = text;
                contentText.maxVisibleCharacters =
                    useTypewriter ? 0 : int.MaxValue;

                contentText.ForceMeshUpdate();
            }

            yield return FadeTo(1f, 0f, fadeDuration);

            if (useTypewriter && contentText != null)
                yield return TypeRoutine();

            IsTyping = false;
            DisplayCompleted?.Invoke(text);

            // 交给 TTS 朗读；若尚未接入 TTS，则按配置停留。
            SpeechRequested?.Invoke(text);

            if (SpeechRequested != null)
            {
                float elapsed = 0f;

                while (!speechFinished &&
                       elapsed < ttsTimeoutSeconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            else
            {
                float elapsed = 0f;

                while (elapsed < holdSecondsWithoutTTS)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            yield return FadeTo(
                0f,
                -slideOffset,
                fadeDuration
            );

            isShowing = false;
            routine = null;
        }

        private IEnumerator HideRoutine()
        {
            IsTyping = false;

            yield return FadeTo(
                0f,
                -slideOffset,
                fadeDuration
            );

            isShowing = false;
            routine = null;
        }

        private IEnumerator TypeRoutine()
        {
            contentText.ForceMeshUpdate();

            int total = contentText.textInfo.characterCount;

            if (charactersPerSecond <= 0f)
            {
                contentText.maxVisibleCharacters = total;
                yield break;
            }

            contentText.maxVisibleCharacters = 0;

            float shown = 0f;

            while (shown < total)
            {
                shown +=
                    Time.unscaledDeltaTime * charactersPerSecond;

                contentText.maxVisibleCharacters =
                    Mathf.Min(total, Mathf.FloorToInt(shown));

                yield return null;
            }

            contentText.maxVisibleCharacters = total;
        }

        private IEnumerator FadeTo(
            float targetAlpha,
            float targetOffset,
            float duration)
        {
            if (canvasGroup == null)
                yield break;

            float startAlpha = canvasGroup.alpha;
            float startOffset = currentOffset;

            if (duration <= 0f)
            {
                ApplyState(targetAlpha, targetOffset);
                yield break;
            }

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                float t = Mathf.Clamp01(elapsed / duration);
                t = t * t * (3f - 2f * t);

                ApplyState(
                    Mathf.Lerp(startAlpha, targetAlpha, t),
                    Mathf.Lerp(startOffset, targetOffset, t)
                );

                yield return null;
            }

            ApplyState(targetAlpha, targetOffset);
        }

        private void ApplyState(float alpha, float offset)
        {
            currentOffset = offset;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = alpha;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }

            if (background != null)
            {
                background.anchoredPosition =
                    backgroundHome + new Vector2(0f, offset);
            }

            if (contentText != null)
            {
                contentText.rectTransform.anchoredPosition =
                    textHome + new Vector2(0f, offset);
            }
        }

        /// <summary>静态入口：显示一句内容。</summary>
        public static void Show(string text)
        {
            if (Instance == null)
            {
                Debug.LogWarning(
                    "DialogueBubble: no dialogue UI available in the current scene."
                );
                return;
            }

            Instance.ShowText(text);
        }

        /// <summary>静态入口：隐藏对话气泡。</summary>
        public static void HideCurrent()
        {
            if (Instance != null)
                Instance.Hide();
        }
    }
}
