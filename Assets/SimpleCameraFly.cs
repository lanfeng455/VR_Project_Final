using System;
using UnityEngine;

public class SimpleCameraFly : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float fastMoveSpeed = 15f;

    [Header("Mouse Look")]
    public float mouseSensitivity = 2f;

    private float yaw;
    private float pitch;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;
    }

    void Update()
    {
        Look();
        Move();
    }

    void Look()
    {
        // 按住鼠标右键才旋转视角
        if (Input.GetMouseButton(1))
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            yaw += mouseX;
            pitch -= mouseY;

            pitch = Mathf.Clamp(pitch, -89f, 89f);

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }

    void Move()
    {
        float speed = Input.GetKey(KeyCode.LeftShift)
            ? fastMoveSpeed
            : moveSpeed;

        Vector3 movement = Vector3.zero;

        // WASD
        if (Input.GetKey(KeyCode.W))
            movement += transform.forward;

        if (Input.GetKey(KeyCode.S))
            movement -= transform.forward;

        if (Input.GetKey(KeyCode.A))
            movement -= transform.right;

        if (Input.GetKey(KeyCode.D))
            movement += transform.right;

        // 上下移动
        if (Input.GetKey(KeyCode.E))
            movement += Vector3.up;

        if (Input.GetKey(KeyCode.Q))
            movement -= Vector3.up;

        transform.position += movement.normalized * speed * Time.deltaTime;
    }
}