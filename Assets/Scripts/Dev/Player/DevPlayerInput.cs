using UnityEngine;
using UnityEngine.InputSystem;

namespace EchoDev
{
    /// <summary>
    /// 开发用输入中枢（仅 PC 键鼠）：纯代码内建 InputAction，不依赖 .inputactions 资产。
    /// 其他玩家脚本统一从这一个入口读输入。
    /// WASD 移动 / 鼠标转向 / 右键传送 / 空格握住 / 左键射线
    /// VR 模式下 DevPlayer 整体下线，手柄输入由 Meta SDK 接管（本组件不参与）。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class DevPlayerInput : MonoBehaviour
    {
        InputAction moveAction;
        InputAction lookAction;
        InputAction teleportAction;
        InputAction grabAction;
        InputAction rayAction;

        public Vector2 Move { get; private set; }
        public Vector2 LookDelta { get; private set; }
        public bool TeleportPressed { get; private set; }
        public bool GrabHeld { get; private set; }
        public bool RayPressed { get; private set; }

        void Awake()
        {
            moveAction = new InputAction("DevMove", InputActionType.Value);
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            lookAction = new InputAction("DevLook", InputActionType.Value, "<Mouse>/delta");
            teleportAction = new InputAction("DevTeleport", InputActionType.Button, "<Mouse>/rightButton");
            grabAction = new InputAction("DevGrab", InputActionType.Button, "<Keyboard>/space");
            rayAction = new InputAction("DevRay", InputActionType.Button, "<Mouse>/leftButton");
        }

        void OnEnable()
        {
            foreach (var a in new[] { moveAction, lookAction, teleportAction, grabAction, rayAction })
                a.Enable();
        }

        void OnDisable()
        {
            foreach (var a in new[] { moveAction, lookAction, teleportAction, grabAction, rayAction })
                a.Disable();
        }

        void Update()
        {
            Move = moveAction.ReadValue<Vector2>();
            LookDelta = lookAction.ReadValue<Vector2>();
            TeleportPressed = teleportAction.WasPressedThisFrame();
            RayPressed = rayAction.WasPressedThisFrame();
            GrabHeld = grabAction.IsPressed();
        }
    }
}
