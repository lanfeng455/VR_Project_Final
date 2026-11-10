using Oculus.Interaction.Locomotion;
using UnityEngine;

namespace EchoDev
{
    /// <summary>
    /// Echo 传送规则：把"只能传送到声波覆盖区"接入 Meta 的 TeleportInteractor。
    /// 挂在带 TeleportInteractor 的物体上（Teleport block 会为手柄生成，通常每手一个），
    /// 自动接管 AcceptDestination —— 落点不在任何活动声波范围内就拒绝选中，
    /// 传送指示器会显示为无效，玩家无法传到"听不见"的地方。
    /// </summary>
    public class EchoTeleportRule : MonoBehaviour
    {
        [Tooltip("不填则自动取同物体上的 TeleportInteractor")]
        public TeleportInteractor teleportInteractor;

        void Awake()
        {
            if (teleportInteractor == null) teleportInteractor = GetComponent<TeleportInteractor>();
            if (teleportInteractor == null)
            {
                Debug.LogError($"[EchoTeleportRule] {name} 上找不到 TeleportInteractor，请挂到传送射线所在物体");
                enabled = false;
                return;
            }
            teleportInteractor.AcceptDestination = AcceptDestination;
        }

        bool AcceptDestination(TeleportInteractable interactable, Pose destination)
        {
            var mgr = EchoWaveManager.Instance;
            if (mgr == null) return false;

            bool covered = mgr.IsPointCovered(destination.position);
            if (!covered)
                Debug.Log($"[EchoTeleportRule] 拒绝传送：{destination.position} 不在声波覆盖范围内");
            return covered;
        }
    }
}
