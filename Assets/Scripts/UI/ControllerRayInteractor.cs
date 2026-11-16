using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 手柄射线：从手柄锚点发射一条激光，命中 World Space Canvas 上的 UGUI
    /// 后扣扳机点击。电视屏由 targetCanvas 指定，日记页等界面在运行时
    /// 通过 <see cref="CanvasRayRegistry"/> 注册，复用同一套输入与射线。
    /// 与 HandPokeInteractor 互补：一个负责戳屏，一个负责远距离指选。
    /// </summary>
    public sealed class ControllerRayInteractor : MonoBehaviour
    {
        [Header("射线起点")]
        [Tooltip("通常是 RightHandAnchor。")]
        [SerializeField] private Transform rayOrigin;

        [Tooltip("通常是 LeftHandAnchor，可留空只保留右手。")]
        [SerializeField] private Transform secondRayOrigin;

        [Header("目标")]
        [Tooltip("电视屏的 Canvas。")]
        [SerializeField] private Canvas targetCanvas;

        [Tooltip("额外可指选的 Canvas。日记页会在运行时注册自己的 Canvas，" +
                 "这里只用于手动补充其它界面。")]
        [SerializeField] private Canvas[] extraCanvases;

        [Tooltip("留空时使用 Camera.main。")]
        [SerializeField] private Camera eventCamera;

        [Header("射线")]
        [Tooltip("射线最大长度（米）。")]
        [SerializeField] private float maxDistance = 5f;

        [SerializeField] private float lineWidth = 0.005f;

        [SerializeField] private Color idleColor =
            new Color(0.55f, 0.8f, 1f, 0.5f);

        [SerializeField] private Color hoverColor =
            new Color(1f, 0.85f, 0.35f, 1f);

        private readonly List<RaycastResult> results =
            new List<RaycastResult>();

        private readonly List<Canvas> canvases = new List<Canvas>();

        private readonly OVRInput.Controller[] controllers =
        {
            OVRInput.Controller.RTouch,
            OVRInput.Controller.LTouch
        };

        private Transform[] origins;
        private LineRenderer[] lines;
        private bool[] pressedLast;
        private GameObject[] hovered;
        private Material lineMaterial;

        private void Awake()
        {
            origins = new[] { rayOrigin, secondRayOrigin };
            lines = new LineRenderer[origins.Length];
            pressedLast = new bool[origins.Length];
            hovered = new GameObject[origins.Length];

            CreateMaterial();

            for (int i = 0; i < origins.Length; i++)
            {
                if (origins[i] != null)
                    lines[i] = CreateLine(i);
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != null)
                    Destroy(lines[i].gameObject);
            }

            if (lineMaterial != null)
                Destroy(lineMaterial);
        }

        private void Update()
        {
            if (origins == null)
                return;

            RefreshCanvases();

            if (canvases.Count == 0)
            {
                ClearHover();
                HideAll();
                return;
            }

            for (int i = 0; i < origins.Length; i++)
            {
                if (lines[i] != null)
                    UpdateRay(i);
            }
        }

        private void RefreshCanvases()
        {
            canvases.Clear();

            if (targetCanvas != null)
                canvases.Add(targetCanvas);

            if (extraCanvases != null)
            {
                for (int i = 0; i < extraCanvases.Length; i++)
                {
                    Canvas canvas = extraCanvases[i];

                    if (canvas != null && !canvases.Contains(canvas))
                        canvases.Add(canvas);
                }
            }

            CanvasRayRegistry.Collect(canvases);
        }

        private void UpdateRay(int index)
        {
            LineRenderer line = lines[index];

            // 手柄没连接时（例如只做手部追踪）不显示这条射线。
            if (!OVRInput.IsControllerConnected(controllers[index]))
            {
                line.enabled = false;
                pressedLast[index] = false;
                SetHover(index, null);
                return;
            }

            Transform origin = origins[index];
            var ray = new Ray(origin.position, origin.forward);

            Vector3 hitPoint = ray.GetPoint(maxDistance);
            float nearest = float.MaxValue;
            GameObject hover = null;

            for (int i = 0; i < canvases.Count; i++)
            {
                Canvas canvas = canvases[i];

                if (canvas == null || !canvas.isActiveAndEnabled)
                    continue;

                var rect = canvas.transform as RectTransform;

                if (rect == null)
                    continue;

                // Canvas 正面朝本地 -Z，从正面一侧与平面求交。
                var plane = new Plane(
                    rect.rotation * Vector3.back,
                    rect.position
                );

                if (!plane.Raycast(ray, out float enter) ||
                    enter <= 0f ||
                    enter > maxDistance ||
                    enter >= nearest)
                {
                    continue;
                }

                Vector3 point = ray.GetPoint(enter);

                if (!IsInside(rect, point))
                    continue;

                nearest = enter;
                hitPoint = point;
                hover = FindUI(canvas, point);
            }

            line.enabled = true;
            line.SetPosition(0, ray.origin);
            line.SetPosition(1, hitPoint);
            SetLineColor(line, hover != null);

            SetHover(index, hover);

            bool pressed = OVRInput.GetDown(
                OVRInput.Button.PrimaryIndexTrigger,
                controllers[index]
            );

            if (pressed && !pressedLast[index] && hover != null)
                Click(hover);

            pressedLast[index] = pressed;
        }

        private static bool IsInside(RectTransform rect, Vector3 worldPoint)
        {
            Vector3 local = rect.InverseTransformPoint(worldPoint);

            return local.x >= rect.rect.xMin &&
                   local.x <= rect.rect.xMax &&
                   local.y >= rect.rect.yMin &&
                   local.y <= rect.rect.yMax;
        }

        private GameObject FindUI(Canvas canvas, Vector3 worldPoint)
        {
            if (EventSystem.current == null)
                return null;

            var raycaster = canvas.GetComponent<GraphicRaycaster>();

            if (raycaster == null)
                return null;

            // 用 raycaster 自己认定的相机换算屏幕坐标，World Space 与
            // Screen Space - Camera 的 Canvas 都能对齐。
            Camera cam = raycaster.eventCamera;

            if (cam == null)
                cam = eventCamera != null ? eventCamera : Camera.main;

            if (cam == null)
                return null;

            Vector2 screenPoint =
                RectTransformUtility.WorldToScreenPoint(cam, worldPoint);

            var eventData = new PointerEventData(EventSystem.current)
            {
                position = screenPoint,
                pressPosition = screenPoint,
                button = PointerEventData.InputButton.Left,
                clickCount = 1
            };

            results.Clear();
            raycaster.Raycast(eventData, results);

            // 优先返回真正能接收点击的那个对象（通常就是 Button），
            // 否则纸张背景之类的 Graphic 会挡住按钮的 hover / click。
            GameObject fallback = null;

            foreach (RaycastResult result in results)
            {
                if (result.gameObject == null)
                    continue;

                if (fallback == null)
                    fallback = result.gameObject;

                GameObject handler = ExecuteEvents.GetEventHandler<
                    IPointerClickHandler>(result.gameObject);

                if (handler != null)
                    return handler;
            }

            return fallback;
        }

        /// <summary>进入 / 离开按钮时派发 hover 事件，让 UGUI 的 ColorBlock 真正有反馈。</summary>
        private void SetHover(int index, GameObject target)
        {
            if (hovered[index] == target)
                return;

            if (hovered[index] != null)
            {
                Dispatch(
                    hovered[index],
                    ExecuteEvents.pointerExitHandler
                );
            }

            hovered[index] = target;

            if (target != null)
            {
                Dispatch(
                    target,
                    ExecuteEvents.pointerEnterHandler
                );
            }
        }

        private void ClearHover()
        {
            if (hovered == null)
                return;

            for (int i = 0; i < hovered.Length; i++)
                SetHover(i, null);
        }

        private static void Dispatch<T>(
            GameObject target,
            ExecuteEvents.EventFunction<T> handler)
            where T : IEventSystemHandler
        {
            if (target == null || EventSystem.current == null)
                return;

            var eventData = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                clickCount = 1
            };

            ExecuteEvents.ExecuteHierarchy(target, eventData, handler);
        }

        private static void Click(GameObject target)
        {
            if (EventSystem.current == null)
                return;

            var eventData = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                clickCount = 1
            };

            // 先 down 再 click 再 up：UGUI 的按下态和 onClick 都靠这一串事件，
            // 只发 pointerClick 时 Button 的 Pressed 颜色不会出现。
            ExecuteEvents.ExecuteHierarchy(
                target,
                eventData,
                ExecuteEvents.pointerDownHandler
            );

            ExecuteEvents.ExecuteHierarchy(
                target,
                eventData,
                ExecuteEvents.pointerClickHandler
            );

            ExecuteEvents.ExecuteHierarchy(
                target,
                eventData,
                ExecuteEvents.pointerUpHandler
            );
        }

        private void HideAll()
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != null)
                    lines[i].enabled = false;
            }
        }

        private LineRenderer CreateLine(int index)
        {
            var go = new GameObject("ControllerRay" + index);

            // 电视屏本身在层 3（overlay 专用），激光必须放回主相机可见的层。
            go.layer = 0;
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.numCapVertices = 4;
            line.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            if (lineMaterial != null)
                line.material = lineMaterial;

            line.startColor = idleColor;
            line.endColor = idleColor;
            line.enabled = false;

            return line;
        }

        private void CreateMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");

            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader != null)
                lineMaterial = new Material(shader);
        }

        private void SetLineColor(LineRenderer line, bool hover)
        {
            Color color = hover ? hoverColor : idleColor;
            line.startColor = color;
            line.endColor = color;
        }
    }
}
