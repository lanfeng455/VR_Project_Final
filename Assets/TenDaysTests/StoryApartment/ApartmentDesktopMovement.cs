using UnityEngine;
using UnityEngine.InputSystem;

namespace TenDays.StoryApartment
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class ApartmentDesktopMovement : MonoBehaviour
    {
        [SerializeField] Camera eye;
        [SerializeField, Min(0f)] float speed = 2.05f;
        [SerializeField, Min(0f)] float mouseSensitivity = .095f;

        CharacterController body;
        float pitch;
        float fall;
        bool captured;

        void Awake()
        {
            body = GetComponent<CharacterController>();
            if (eye)
                pitch = Mathf.DeltaAngle(0f, eye.transform.localEulerAngles.x);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (!eye)
                return;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                ReleaseMouse();
            if (mouse != null && !captured && mouse.leftButton.wasPressedThisFrame)
            {
                captured = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (captured && mouse != null)
            {
                var mouseDelta = mouse.delta.ReadValue() * mouseSensitivity;
                pitch = Mathf.Clamp(pitch - mouseDelta.y, -78f, 78f);
                transform.Rotate(0f, mouseDelta.x, 0f);
                eye.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            if (keyboard == null)
                return;
            var input = Vector2.ClampMagnitude(new Vector2(
                Axis(keyboard.aKey.isPressed, keyboard.dKey.isPressed),
                Axis(keyboard.sKey.isPressed, keyboard.wKey.isPressed)), 1f);
            Move(input, Time.deltaTime);
        }

        void Move(Vector2 input, float deltaTime)
        {
            fall = body.isGrounded ? -2f : Mathf.Max(fall - 14f * deltaTime, -15f);
            var velocity = (transform.right * input.x + transform.forward * input.y) * speed + Vector3.up * fall;
            body.Move(velocity * deltaTime);
        }

        static float Axis(bool negative, bool positive) => (positive ? 1f : 0f) - (negative ? 1f : 0f);

        void OnApplicationFocus(bool focus)
        {
            if (!focus) ReleaseMouse();
        }

        void OnDisable() => ReleaseMouse();

        void ReleaseMouse()
        {
            if (!captured) return;
            captured = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
