using DG.Tweening;
using Unity.Cinemachine;
using UnityEngine;

namespace EchoDev
{
    /// <summary>
    /// Cinemachine 演出控制器（挂在演出相机 Rig 根上，默认禁用）：
    ///   Play(point) —— 禁用玩家相机 → 启用 Rig → vcam 摆到定位点位姿 → duration 后自动切回
    ///   Shake()     —— 画面震动（Impulse），可在事件链任何时点调用（如门开始移动时）
    /// 触发方式：任何 UnityEvent 静态绑定（GameEventListener / ActionSequence / TriggerDoor…）。
    ///
    /// VR 预留：演出相机独立于玩家相机；将来接 XR 时只需在 Play/Stop 里
    /// 额外关/开 XR display（头显切 2D 播片），其余结构不变。
    /// </summary>
    public class CinematicRig : MonoBehaviour
    {
        [Tooltip("演出相机（挂 CinemachineBrain）")]
        public Camera cinematicCamera;

        [Tooltip("虚拟相机：Play 时被摆到定位点")]
        public CinemachineCamera vcam;

        [Tooltip("震动源")]
        public CinemachineImpulseSource impulseSource;

        [Tooltip("玩家相机：播放时禁用、结束后恢复。留空则首次 Play 时取 Camera.main")]
        public Camera playerCamera;

        Tween endTimer;

        /// <summary>在定位点播放演出，duration 后自动切回（事件链用这个）。</summary>
        public void Play(CinematicPoint point) => Play(point, autoStop: true);

        /// <summary>带控制的播放：autoStop=false 时不自动切回（编辑器预览用）。</summary>
        public void Play(CinematicPoint point, bool autoStop)
        {
            if (point == null) return;

            // 防重入：演出进行中忽略重复 Play（通常是事件回路：相机禁用/激活又广播了同一频道）
            if (autoStop && gameObject.activeSelf)
            {
                Debug.LogWarning("[CinematicRig] 演出进行中，忽略一次重复 Play —— 请检查是否存在事件回路"
                    + "（如玩家相机上的 LifecycleEvents 广播了同一频道）");
                return;
            }

            EnsureRefs();
            if (cinematicCamera == null || vcam == null) return;

            // 记住并禁用玩家相机（演出期间画面归过场相机）
            if (playerCamera == null)
            {
                Camera main = Camera.main;
                if (main != null && main != cinematicCamera) playerCamera = main;
            }
            if (playerCamera != null) playerCamera.gameObject.SetActive(false);

            // vcam 摆位 + 视觉参数
            vcam.transform.SetPositionAndRotation(point.transform.position, point.transform.rotation);
            vcam.Lens.FieldOfView = point.fov;

            gameObject.SetActive(true);

            endTimer?.Kill();
            endTimer = null;
            if (autoStop) endTimer = DOVirtual.DelayedCall(point.duration, Stop);

            if (point.shakeOnPlay) Shake(point.shakeForce);
        }

        /// <summary>立即切回玩家相机。</summary>
        public void Stop()
        {
            endTimer?.Kill();
            endTimer = null;
            gameObject.SetActive(false);
            if (playerCamera != null) playerCamera.gameObject.SetActive(true);
        }

        /// <summary>画面震动（默认力度）。供 UnityEvent 调用，如门开始抬起的瞬间。</summary>
        public void Shake() => Shake(0.5f);

        public void Shake(float force)
        {
            if (impulseSource != null) impulseSource.GenerateImpulse(Vector3.one * force);
        }

        void EnsureRefs()
        {
            // Rig 默认禁用，Awake 可能尚未执行；首次 Play 时兜底找引用
            if (cinematicCamera == null) cinematicCamera = GetComponentInChildren<Camera>(true);
            if (vcam == null) vcam = GetComponentInChildren<CinemachineCamera>(true);
            if (impulseSource == null) impulseSource = GetComponent<CinemachineImpulseSource>();
        }

        void OnDestroy() => endTimer?.Kill();
    }
}
