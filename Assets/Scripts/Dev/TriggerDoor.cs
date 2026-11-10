using System.Collections;
using UnityEngine;

namespace EchoDev
{
    /// <summary>
    /// 简易触发门：指定名字的物体进入 Trigger 后，门绕自身 Y 轴旋转 openAngle 度（openDuration 秒，缓入缓出）。
    /// 开门瞬间激活 soundObject（可在上面绑 EchoEmitter 等发声单元），soundLifetime 秒后销毁它。
    /// 只触发一次；触发器可以是本物体或任意子物体上的 Collider（记得勾 Is Trigger）。
    /// </summary>
    public class TriggerDoor : MonoBehaviour
    {
        [SerializeField] private bool destroyOnTrigger;
        [Header("触发")]
        [Tooltip("进入 Trigger 的物体名（碰撞体或其刚体的名字）")]
        public string targetObjectName = "DevPlayer";

        [Header("开门动画")]
        [Tooltip("开门后门停在世界 Y 轴角度（旋转到该角度，非相对增量）")]
        public float openAngle = 90f;
        [Tooltip("开门动画时长（秒）")]
        public float openDuration = 1.5f;

        [Header("发声单元")]
        [Tooltip("开门时激活的物体（绑 EchoEmitter），放场景里默认保持未激活")]
        public GameObject soundObject;
        [Tooltip("激活后几秒销毁")]
        public float soundLifetime = 5f;

        Quaternion closedRot;
        bool opened;

        void Awake()
        {
            closedRot = transform.rotation;
        }

        void OnTriggerEnter(Collider other)
        {
            if (opened) return;

            bool nameMatch = other.name == targetObjectName
                          || (other.attachedRigidbody != null && other.attachedRigidbody.name == targetObjectName);
            if (!nameMatch) return;

            opened = true;
            Debug.Log($"[TriggerDoor] {other.name} 触发开门：{name}");
            StartCoroutine(OpenAndSound());
        }

        IEnumerator OpenAndSound()
        {
            // 激活发声单元，soundLifetime 秒后销毁
            if (soundObject != null)
            {
                soundObject.SetActive(true);
                Destroy(soundObject, soundLifetime);
            }

            // 开门动画：缓入缓出"旋转到" openAngle（世界 Y 轴绝对角度）
            // LerpAngle 自动走最短旋转方向到达目标角度
            float startY = closedRot.eulerAngles.y;
            float t = 0f;
            while (t < openDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / openDuration);
                float angle = Mathf.LerpAngle(startY, openAngle, k);
                transform.rotation = Quaternion.Euler(0f, angle, 0f);
                yield return null;
            }
            transform.rotation = Quaternion.Euler(0f, openAngle, 0f);

            if (destroyOnTrigger)
            {
                this.enabled = false;
            }
        }
    }
}
