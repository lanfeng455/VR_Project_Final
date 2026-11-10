using UnityEngine;

namespace EchoDev
{
    /// <summary>
    /// EchoDev 通用的层号解析（带兜底，不依赖工程 TagManager 的具体配置）。
    /// 背景：内置层名是 "Ignore Raycast"（带空格），
    /// NameToLayer 找不到时返回 -1，直接 set_layer(-1) 会报错。
    /// </summary>
    public static class EchoLayers
    {
        public static readonly int IgnoreRaycast = Resolve("Ignore Raycast", 4);

        static int Resolve(string name, int builtinFallback)
        {
            int layer = LayerMask.NameToLayer(name);
            return layer >= 0 ? layer : builtinFallback;
        }
    }
}
