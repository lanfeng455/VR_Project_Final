using UnityEngine;
using UnityEngine.InputSystem;

namespace EchoDev
{
    /// <summary>
    /// 开发用 FPS 视角：鼠标 X 转动玩家根物体（Yaw），鼠标 Y 转动相机（Pitch，±85°）。
    /// 点击画面锁定鼠标，Esc 释放；未锁定时不转向。
    /// </summary>
    public class DevPlayerLook : MonoBehaviour
    {
        public Transform cameraTransform;
        public float sensitivity = 0.12f;
        public float pitchLimit = 85f;

        DevPlayerInput input;
        float pitch;

        void Start()
        {
            input = GetComponentInParent<DevPlayerInput>();
            if (cameraTransform == null)
            {
                var cam = GetComponentInChildren<Camera>();
                if (cam != null) cameraTransform = cam.transform;
            }
            if (cameraTransform != null)
            {
                float x = cameraTransform.localEulerAngles.x;
                pitch = x > 180f ? x - 360f : x;
            }
        }

        void Update()
        {
            HandleCursorLock();
            if (input == null || Cursor.lockState != CursorLockMode.Locked) return;

            Vector2 d = input.LookDelta * sensitivity;
            transform.Rotate(0f, d.x, 0f); // Yaw：根物体

            pitch = Mathf.Clamp(pitch - d.y, -pitchLimit, pitchLimit);
            if (cameraTransform != null) // Pitch：相机
                cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void HandleCursorLock()
        {
            var mouse = Mouse.current;
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (mouse != null && (mouse.leftButton.wasPressedThisFrame ||
                                      mouse.rightButton.wasPressedThisFrame))
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
            else if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }
}
