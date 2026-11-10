using UnityEngine;

namespace EchoDev
{
    /// <summary>
    /// 一圈声波的运行时数据（与渲染表现分离）。
    /// 数据层只负责"波在哪、扩到哪"，传送合法性判定只依赖这里。
    /// </summary>
    public class EchoWave
    {
        public Vector3 Origin;    // 波心（世界坐标，一般贴地）
        public float Speed;       // 扩散速度 m/s
        public float MaxRadius;   // 最大半径 m
        public float Elapsed;     // 已存活秒数
        public float Strength;    // 强度 0..1（影响 shader 亮度）
        public Color Color;       // 波颜色（区分来源：脚步/射线/场景声源）

        public float CurrentRadius => Mathf.Min(Elapsed * Speed, MaxRadius);
        public bool IsAlive => Elapsed * Speed < MaxRadius;

        public EchoWave(Vector3 origin, float speed, float maxRadius, float strength, Color color)
        {
            Origin = origin;
            Speed = Mathf.Max(0.01f, speed);
            MaxRadius = maxRadius;
            Elapsed = 0f;
            Strength = Mathf.Clamp01(strength);
            Color = color;
        }

        public void Tick(float dt) => Elapsed += dt;
    }
}
