using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 用指尖直接“戳”World Space 屏幕上的 UGUI 按钮。
    /// 指尖进入屏幕前方指定距离时触发一次点击，离开后才会再次触发。
    /// 除 targetCanvas（电视屏）外，运行时注册到
    /// <see cref="CanvasRayRegistry"/> 的界面（例如日记页）同样可戳。
    /// PC 上的鼠标点击仍由 EventSystem 现有的输入模块处理。
    /// </summary>
    public sealed class HandPokeInteractor : MonoBehaviour
    {
        [Header("指尖")]
        [Tooltip("通常是手部 / 控制器的锚点，例如 RightHandAnchor。")]
        [SerializeField] private Transform fingerTip;

        [Tooltip("可选的另一只手。")]
        [SerializeField] private Transform secondFingerTip;

        [Header("目标")]
        [SerializeField] private Canvas targetCanvas;

        [Tooltip("额外可戳的 Canvas，通常留空，由界面自己注册。")]
        [SerializeField] private Canvas[] extraCanvases;

        [Tooltip("留空时使用 Camera.main。")]
        [SerializeField] private Camera eventCamera;

        [Header("触控距离（米）")]
        [Tooltip("指尖离屏幕多近算按下。")]
        [SerializeField] private float pressDistance = 0.04f;

        [Tooltip("指尖离开多远才算松开，避免抖动重复点击。")]
        [SerializeField] private float releaseDistance = 0.09f;

        [Tooltip("超过这个距离就不再检测，避免误触。")]
        [SerializeField] private float maxReachDistance = 0.35f;

        private readonly List<RaycastResult> results =
            new List<RaycastResult>();

        private readonly List<Canvas> canvases = new List<Canvas>();

        private readonly bool[] touching = new bool[2];
        private readonly Canvas[] touchingCanvas = new Canvas[2];

        private void Update()
        {
            RefreshCanvases();

            TryPoke(0, fingerTip);
            TryPoke(1, secondFingerTip);
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

        private void TryPoke(int index, Transform tip)
        {
            if (tip == null)
            {
                touching[index] = false;
                touchingCanvas[index] = null;
                return;
            }

            Canvas nearestCanvas = null;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < canvases.Count; i++)
            {
                Canvas canvas = canvases[i];

                if (canvas == null || !canvas.isActiveAndEnabled)
                    continue;

                var rect = canvas.transform as RectTransform;

                if (rect == null)
                    continue;

                // Canvas 正面朝向本地 -Z，指尖从负 Z 方向靠近。
                Vector3 local = rect.InverseTransformPoint(tip.position);
                float frontDepth = -local.z;

                if (frontDepth > maxReachDistance ||
                    frontDepth < -pressDistance)
                {
                    continue;
                }

                if (!IsInside(rect, local))
                    continue;

                if (frontDepth < nearestDepth)
                {
                    nearestDepth = frontDepth;
                    nearestCanvas = canvas;
                }
            }

            if (nearestCanvas == null)
            {
                touching[index] = false;
                touchingCanvas[index] = null;
                return;
            }

            if (!touching[index])
            {
                if (nearestDepth > pressDistance)
                    return;

                touching[index] = true;
                touchingCanvas[index] = nearestCanvas;
                Click(nearestCanvas, tip.position);
                return;
            }

            // 换到别的界面或已经退到松开距离之外，下一次靠近才会重新点击。
            if (nearestCanvas != touchingCanvas[index] ||
                nearestDepth > releaseDistance)
            {
                touching[index] = false;
                touchingCanvas[index] = null;
            }
        }

        private static bool IsInside(RectTransform rect, Vector3 localPoint)
        {
            return localPoint.x >= rect.rect.xMin &&
                   localPoint.x <= rect.rect.xMax &&
                   localPoint.y >= rect.rect.yMin &&
                   localPoint.y <= rect.rect.yMax;
        }

        private void Click(Canvas canvas, Vector3 worldPoint)
        {
            if (EventSystem.current == null)
                return;

            var raycaster = canvas.GetComponent<GraphicRaycaster>();

            if (raycaster == null)
                return;

            Camera cam = raycaster.eventCamera;

            if (cam == null)
                cam = eventCamera != null ? eventCamera : Camera.main;

            if (cam == null)
                return;

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

            if (results.Count == 0)
                return;

            GameObject target = null;

            foreach (RaycastResult result in results)
            {
                if (result.gameObject == null)
                    continue;

                if (target == null)
                    target = result.gameObject;

                GameObject handler = ExecuteEvents.GetEventHandler<
                    IPointerClickHandler>(result.gameObject);

                if (handler != null)
                {
                    target = handler;
                    break;
                }
            }

            ExecuteEvents.ExecuteHierarchy(
                target,
                eventData,
                ExecuteEvents.pointerEnterHandler
            );

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

            EventSystem.current.SetSelectedGameObject(target);
        }
    }
}
