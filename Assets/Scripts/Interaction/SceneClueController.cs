using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Interaction
{
    /// <summary>
    /// 场景线索白名单：clueId → 场景物体。
    /// 工具只允许使用这里登记过的 id，模型无法用自由字符串定位任意物体。
    /// 本轮没有正式线索资产，可用 Demo 球验证链路。
    /// </summary>
    public sealed class SceneClueController : MonoBehaviour
    {
        [Serializable]
        public sealed class ClueBinding
        {
            public string clueId = "clue_1";
            public GameObject target;
        }

        [Header("线索白名单")]
        [SerializeField] private ClueBinding[] clues = new ClueBinding[0];

        [Header("占位示例")]
        [Tooltip("没有配置任何线索时，自动生成一个可显隐的 Demo 球用于验证。")]
        [SerializeField] private bool createDemoClueWhenEmpty = true;

        [SerializeField] private string demoClueId = "demo_clue";

        private readonly List<string> revealed = new List<string>();

        public IReadOnlyList<ClueBinding> Clues => clues;

        public IReadOnlyList<string> ClueIds
        {
            get
            {
                var ids = new List<string>();

                for (int i = 0; i < clues.Length; i++)
                {
                    if (clues[i] != null &&
                        !string.IsNullOrWhiteSpace(clues[i].clueId))
                    {
                        ids.Add(clues[i].clueId);
                    }
                }

                return ids;
            }
        }

        private void Start()
        {
            if (createDemoClueWhenEmpty && ClueIds.Count == 0)
                CreateDemoClue();
        }

        /// <summary>幂等：重复揭示同一条线索返回成功，不会产生副作用。</summary>
        public bool TryReveal(string clueId, out string message)
        {
            if (string.IsNullOrWhiteSpace(clueId))
            {
                message = "Missing clueId.";
                return false;
            }

            ClueBinding binding = Find(clueId);

            if (binding == null || binding.target == null)
            {
                message = $"Unknown clueId '{clueId}'.";
                return false;
            }

            if (binding.target.activeSelf)
            {
                message = $"Clue '{clueId}' is already revealed.";
                return true;
            }

            binding.target.SetActive(true);

            if (!revealed.Contains(clueId))
                revealed.Add(clueId);

            message = $"Clue '{clueId}' revealed.";
            return true;
        }

        public bool IsRevealed(string clueId)
        {
            return revealed.Contains(clueId);
        }

        private ClueBinding Find(string clueId)
        {
            for (int i = 0; i < clues.Length; i++)
            {
                if (clues[i] != null && clues[i].clueId == clueId)
                    return clues[i];
            }

            return null;
        }

        private void CreateDemoClue()
        {
            Camera camera = Camera.main;
            Vector3 origin = camera != null
                ? camera.transform.position
                : Vector3.zero;

            Vector3 forward = camera != null
                ? camera.transform.forward
                : Vector3.forward;

            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f
                ? forward.normalized
                : Vector3.forward;

            var demo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            demo.name = "Clue_Demo_Sphere";
            demo.transform.position = origin + forward * 2.5f;
            demo.transform.localScale = Vector3.one * 0.18f;

            var collider = demo.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            var renderer = demo.GetComponent<Renderer>();

            if (renderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                shader ??= Shader.Find("Standard");

                if (shader != null)
                {
                    var material = new Material(shader);
                    material.color = new Color(0.35f, 0.85f, 1f, 1f);
                    renderer.material = material;
                }
            }

            clues = new[]
            {
                new ClueBinding
                {
                    clueId = demoClueId,
                    target = demo
                }
            };

            demo.SetActive(false);
        }
    }
}
