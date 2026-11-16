using Game.Interaction;
using Game.TTS;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// 临时测试资产的一次性装配：
    /// 1) TTS 缓存预热组件（挂在 LUNA TTS 上）
    /// 2) grip 拾取交互器（左右手各一份状态）
    /// 3) 可拾取日记页 + 角落的临时朗读测试按钮
    ///
    /// 重复执行不会产生重复节点；所有改动都可以通过
    /// Tools/VR Project/LUNA/Toggle Test Extras 一键关掉。
    /// </summary>
    public static class LunaInteractionTestSetup
    {
        private const string MenuPath =
            "Tools/VR Project/LUNA/Add Interaction Test Props";

        private const string ToggleMenuPath =
            "Tools/VR Project/LUNA/Toggle Test Extras";

        private const string InteractorName = "Grip Grab Interactor";
        private const string DiaryName = "Diary Page";

        [MenuItem(MenuPath)]
        public static void Add()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "LUNA Test Props",
                    "Exit Play mode first.",
                    "OK"
                );

                return;
            }

            LunaTTSController tts =
                Object.FindFirstObjectByType<LunaTTSController>(
                    FindObjectsInactive.Include
                );

            if (tts == null)
            {
                // 预热要挂在 TTS 控制器上，先确保它存在。
                LunaTTSSetup.Add();

                tts = Object.FindFirstObjectByType<LunaTTSController>(
                    FindObjectsInactive.Include
                );
            }

            EnsureWarmup(tts);
            EnsureGrabInteractor();
            DiaryPage page = EnsureDiaryPage(tts);

            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = page != null
                ? page.gameObject
                : null;

            Debug.Log(
                "[LUNA TEST] Interaction test props are in place: " +
                "cache warmup, grip grab (both hands), diary page."
            );
        }

        [MenuItem(ToggleMenuPath)]
        public static void ToggleTestExtras()
        {
            if (Application.isPlaying)
                return;

            TTSCacheWarmup warmup =
                Object.FindFirstObjectByType<TTSCacheWarmup>(
                    FindObjectsInactive.Include
                );

            DiaryPage page =
                Object.FindFirstObjectByType<DiaryPage>(
                    FindObjectsInactive.Include
                );

            bool enable = warmup == null || !warmup.enabled;

            if (warmup != null)
            {
                Undo.RecordObject(warmup, "Toggle test extras");
                warmup.enabled = enable;
                EditorUtility.SetDirty(warmup);
            }

            if (page != null)
            {
                var serialized = new SerializedObject(page);
                serialized.FindProperty("enableTestButton").boolValue =
                    enable;

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(
                "[LUNA TEST] Test extras " +
                (enable ? "enabled" : "disabled") + "."
            );
        }

        private static void EnsureWarmup(LunaTTSController tts)
        {
            if (tts == null)
            {
                Debug.LogWarning(
                    "[LUNA TEST] No LunaTTSController; cache warmup " +
                    "was not added."
                );

                return;
            }

            if (tts.GetComponent<TTSCacheWarmup>() != null)
                return;

            Undo.AddComponent<TTSCacheWarmup>(tts.gameObject);
        }

        private static void EnsureGrabInteractor()
        {
            GameObject root = GameObject.Find(InteractorName);

            if (root == null)
            {
                root = new GameObject(InteractorName);

                Undo.RegisterCreatedObjectUndo(
                    root,
                    "Add grip grab interactor"
                );
            }

            GripGrabInteractor interactor =
                root.GetComponent<GripGrabInteractor>();

            if (interactor == null)
            {
                interactor = Undo.AddComponent<GripGrabInteractor>(
                    root
                );
            }

            Transform left = FindAnchor("LeftHandAnchor");
            Transform right = FindAnchor("RightHandAnchor");

            var serialized = new SerializedObject(interactor);

            if (left != null)
            {
                serialized.FindProperty("leftHand")
                    .objectReferenceValue = left;
            }

            if (right != null)
            {
                serialized.FindProperty("rightHand")
                    .objectReferenceValue = right;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform FindAnchor(string name)
        {
            GameObject found = GameObject.Find(name);

            if (found == null)
            {
                Debug.LogWarning(
                    "[LUNA TEST] '" + name + "' not found; assign the " +
                    "hand anchor manually on " + InteractorName + "."
                );
            }

            return found != null ? found.transform : null;
        }

        private static DiaryPage EnsureDiaryPage(
            LunaTTSController tts)
        {
            GameObject existing = GameObject.Find(DiaryName);

            if (existing != null)
            {
                DiaryPage found = existing.GetComponent<DiaryPage>();

                if (found == null)
                    found = Undo.AddComponent<DiaryPage>(existing);

                // 日记需要受重力：松手后自然下落。
                Rigidbody existingBody = existing.GetComponent<Rigidbody>();

                if (existingBody == null)
                    existingBody = Undo.AddComponent<Rigidbody>(existing);

                existingBody.useGravity = true;
                existingBody.isKinematic = false;

                return found;
            }

            var root = new GameObject(DiaryName);

            Undo.RegisterCreatedObjectUndo(root, "Add diary page");

            root.transform.position = ResolveDiaryPosition();
            root.transform.rotation = Quaternion.LookRotation(
                ResolveDiaryForward(),
                Vector3.up
            );

            // 纸面：一块很薄的立方体，只负责外观。
            GameObject paper = GameObject.CreatePrimitive(
                PrimitiveType.Cube
            );

            paper.name = "Paper";
            paper.transform.SetParent(root.transform, false);
            paper.transform.localScale = new Vector3(
                0.22f,
                0.30f,
                0.004f
            );

            Object.DestroyImmediate(paper.GetComponent<Collider>());

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 0.15f;
            body.useGravity = true;

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(0.22f, 0.30f, 0.008f);

            root.AddComponent<Grabbable>();

            DiaryPage page = root.AddComponent<DiaryPage>();

            GameObject canvasGo = CreateCanvas(root.transform);
            CreatePaperBackground(canvasGo.transform);

            TMP_Text title = CreateText(
                canvasGo.transform,
                "Title",
                "DIARY",
                26,
                TextAlignmentOptions.Center,
                new Color(0.16f, 0.14f, 0.12f),
                new Vector2(26f, -18f),
                new Vector2(-26f, -54f)
            );

            title.fontStyle = FontStyles.Bold;

            TMP_Text bodyText = CreateText(
                canvasGo.transform,
                "Body",
                string.Empty,
                17,
                TextAlignmentOptions.TopLeft,
                new Color(0.15f, 0.13f, 0.11f),
                new Vector2(26f, 58f),
                new Vector2(-26f, -60f)
            );

            GameObject buttonGo = CreateTestButton(
                canvasGo.transform,
                page
            );

            var serialized = new SerializedObject(page);
            serialized.FindProperty("bodyText").objectReferenceValue =
                bodyText;

            serialized.FindProperty("testButtonRoot")
                .objectReferenceValue = buttonGo;

            if (tts != null)
            {
                serialized.FindProperty("tts").objectReferenceValue = tts;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            return page;
        }

        private static Vector3 ResolveDiaryPosition()
        {
            GameObject tv = GameObject.Find("TV");

            if (tv == null)
                return new Vector3(0f, 1.05f, 1.2f);

            Vector3 forward = tv.transform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;

            forward.Normalize();

            return tv.transform.position +
                   forward * 0.55f +
                   Vector3.down * 0.55f;
        }

        private static Vector3 ResolveDiaryForward()
        {
            GameObject tv = GameObject.Find("TV");

            if (tv == null)
                return Vector3.forward;

            Vector3 forward = tv.transform.forward;
            forward.y = 0f;

            return forward.sqrMagnitude < 1e-4f
                ? Vector3.forward
                : forward.normalized;
        }

        private static GameObject CreateCanvas(Transform parent)
        {
            var canvasGo = new GameObject(
                "Diary Canvas",
                typeof(RectTransform)
            );

            canvasGo.transform.SetParent(parent, false);
            canvasGo.transform.localPosition = new Vector3(0f, 0f, -0.004f);
            canvasGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            canvasGo.transform.localScale = Vector3.one * 0.0005f;

            var rect = (RectTransform)canvasGo.transform;
            rect.sizeDelta = new Vector2(440f, 600f);

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            return canvasGo;
        }

        private static void CreatePaperBackground(Transform parent)
        {
            var go = new GameObject("Paper Fill", typeof(RectTransform));

            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.94f, 0.91f, 0.84f, 1f);

            // 纸张底色要在文字之下。
            go.transform.SetAsFirstSibling();
        }

        private static TMP_Text CreateText(
            Transform parent,
            string name,
            string content,
            float fontSize,
            TextAlignmentOptions alignment,
            Color color,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));

            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;

            return text;
        }

        private static GameObject CreateTestButton(
            Transform parent,
            DiaryPage page)
        {
            var go = new GameObject(
                "TEST Read Button",
                typeof(RectTransform)
            );

            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-16f, 14f);
            rect.sizeDelta = new Vector2(150f, 34f);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.22f, 0.26f, 0.32f, 0.92f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            TMP_Text label = CreateText(
                go.transform,
                "Label",
                "TEST: READ",
                13f,
                TextAlignmentOptions.Center,
                Color.white,
                Vector2.zero,
                Vector2.zero
            );

            label.raycastTarget = false;

            UnityEventTools.AddPersistentListener(
                button.onClick,
                page.OnTestButtonClicked
            );

            return go;
        }
    }
}
