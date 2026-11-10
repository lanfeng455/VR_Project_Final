using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 手柄射线：从手柄锚点发射一条激光，命中电视屏的 UGUI 后扣扳机点击。
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

        private readonly OVRInput.Controller[] controllers =
        {
            OVRInput.Controller.RTouch,
            OVRInput.Controller.LTouch
        };

        private Transform[] origins;
        private LineRenderer[] lines;
        private bool[] pressedLast;
        private Material lineMaterial;
        private GraphicRaycaster raycaster;

        private void Awake()
        {
            origins = new[] { rayOrigin, secondRayOrigin };
            lines = new LineRenderer[origins.Length];
            pressedLast = new bool[origins.Length];

            if (targetCanvas != null)
                raycaster = targetCanvas.GetComponent<GraphicRaycaster>();

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
            if (targetCanvas == null || raycaster == null)
            {
                HideAll();
                return;
            }

            for (int i = 0; i < origins.Length; i++)
            {
                if (lines[i] != null)
                    UpdateRay(i);
            }
        }

        private void UpdateRay(int index)
        {
            LineRenderer line = lines[index];

            // 手柄没连接时（例如只做手部追踪）不显示这条射线。
            if (!OVRInput.IsControllerConnected(controllers[index]))
            {
                line.enabled = false;
                pressedLast[index] = false;
                return;
            }

            var rect = targetCanvas.transform as RectTransform;

            if (rect == null)
            {
                line.enabled = false;
                return;
            }

            Transform origin = origins[index];
            var ray = new Ray(origin.position, origin.forward);

            // Canvas 正面朝本地 -Z，从正面一侧与平面求交。
            var plane = new Plane(rect.rotation * Vector3.back, rect.position);

            Vector3 hitPoint = ray.GetPoint(maxDistance);
            GameObject hover = null;

            if (plane.Raycast(ray, out float enter) &&
                enter > 0f &&
                enter <= maxDistance)
            {
                Vector3 point = ray.GetPoint(enter);

                if (IsInside(rect, point))
                {
                    hitPoint = point;
                    hover = FindUI(point);
                }
            }

            line.enabled = true;
            line.SetPosition(0, ray.origin);
            line.SetPosition(1, hitPoint);
            SetLineColor(line, hover != null);

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

        private GameObject FindUI(Vector3 worldPoint)
        {
            Camera cam = eventCamera != null ? eventCamera : Camera.main;

            if (cam == null || EventSystem.current == null)
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

            foreach (RaycastResult result in results)
            {
                if (result.gameObject != null)
                    return result.gameObject;
            }

            return null;
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

            ExecuteEvents.ExecuteHierarchy(
                target,
                eventData,
                ExecuteEvents.pointerClickHandler
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
