using UnityEngine;

namespace EchoDev
{
    /// <summary>
    /// 演出定位点：物体的 Transform 就是过场相机的位姿（摆哪是哪，所见即所得）。
    /// CinematicRig.Play(本组件) 时 vcam 复制这里的位置+朝向，应用这里的 FOV，
    /// 播放 duration 秒后自动切回玩家相机。
    ///
    /// 调机位工作流：
    ///   1. Scene 视图漫游到理想机位 → 选中本物体 → Ctrl+Shift+F（Align With View）吸附位姿
    ///   2. 选中时 Scene 视图会画 FOV 视锥线，微调位置/朝向/FOV
    ///   3. 组件右键 → "预览机位" 在 Game 视图直接看出片画面，右键 → "停止预览" 复原
    /// </summary>
    public class CinematicPoint : MonoBehaviour
    {
        [Tooltip("过场相机 FOV")]
        public float fov = 50f;

        [Tooltip("演出时长（秒），播完自动切回玩家相机")]
        public float duration = 3f;

        [Tooltip("播放瞬间是否同时震动画面")]
        public bool shakeOnPlay = false;

        [Tooltip("震动力度")]
        public float shakeForce = 0.5f;

        [Tooltip("视锥预览长度（米，仅 Scene 视图显示用）")]
        public float gizmoRange = 8f;

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            // 从定位点沿朝向画 FOV 视锥，Scene 视图所见即所得
            Gizmos.color = new Color(1f, 0.62f, 0.2f);
            Transform t = transform;
            float aspect = Camera.main != null ? Camera.main.aspect : 16f / 9f;
            float halfH = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * gizmoRange;
            float halfW = halfH * aspect;

            Vector3 p = t.position;
            Vector3 c = p + t.forward * gizmoRange;
            Vector3 tl = c + t.up * halfH - t.right * halfW;
            Vector3 tr = c + t.up * halfH + t.right * halfW;
            Vector3 bl = c - t.up * halfH - t.right * halfW;
            Vector3 br = c - t.up * halfH + t.right * halfW;

            Gizmos.DrawLine(p, tl); Gizmos.DrawLine(p, tr);
            Gizmos.DrawLine(p, bl); Gizmos.DrawLine(p, br);
            Gizmos.DrawLine(tl, tr); Gizmos.DrawLine(tr, br);
            Gizmos.DrawLine(br, bl); Gizmos.DrawLine(bl, tl);
        }

        [ContextMenu("预览机位（Game 视图）")]
        void PreviewCamera()
        {
            var rig = FindFirstObjectByType<CinematicRig>(FindObjectsInactive.Include);
            if (rig == null)
            {
                Debug.LogWarning("[CinematicPoint] 场景里没有 CinematicRig，先跑 Tools → EchoDev → 搭建演出相机 Rig");
                return;
            }
            rig.Play(this, autoStop: false); // 不启动自动恢复计时，看够了手动停
        }

        [ContextMenu("停止预览")]
        void StopPreview()
        {
            var rig = FindFirstObjectByType<CinematicRig>(FindObjectsInactive.Include);
            rig?.Stop();
        }
#endif
    }
}
