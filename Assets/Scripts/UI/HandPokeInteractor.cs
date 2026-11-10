using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 用指尖直接“戳”World Space 屏幕上的 UGUI 按钮。
    /// 指尖进入屏幕前方指定距离时触发一次点击，离开后才会再次触发。
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

        private readonly bool[] touching = new bool[2];

        private void Update()
        {
            if (targetCanvas == null)
                return;

            Camera cam = eventCamera != null
                ? eventCamera
                : Camera.main;

            if (cam == null)
                return;

            TryPoke(0, fingerTip, cam);
            TryPoke(1, secondFingerTip, cam);
        }

        private void TryPoke(int index, Transform tip, Camera cam)
        {
            if (tip == null)
            {
                touching[index] = false;
                return;
            }

            var rect = targetCanvas.transform as RectTransform;

            if (rect == null)
                return;

            // Canvas 正面朝向本地 -Z，指尖从负 Z 方向靠近。
            Vector3 local = rect.InverseTransformPoint(tip.position);
            float frontDepth = -local.z;

            if (frontDepth > maxReachDistance ||
                frontDepth < -pressDistance)
            {
                touching[index] = false;
                return;
            }

            bool inside =
                local.x >= rect.rect.xMin &&
                local.x <= rect.rect.xMax &&
                local.y >= rect.rect.yMin &&
                local.y <= rect.rect.yMax;

            if (!touching[index])
            {
                if (!inside || frontDepth > pressDistance)
                    return;

                touching[index] = true;
                Click(rect, tip.position, cam);
            }
            else if (!inside || frontDepth > releaseDistance)
            {
                touching[index] = false;
            }
        }

        private void Click(
            RectTransform rect,
            Vector3 worldPoint,
            Camera cam)
        {
            var raycaster = targetCanvas.GetComponent<GraphicRaycaster>();

            if (raycaster == null)
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

            GameObject target = results[0].gameObject;

            ExecuteEvents.ExecuteHierarchy(
                target,
                eventData,
                ExecuteEvents.pointerClickHandler
            );

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(target);
        }
    }
}
