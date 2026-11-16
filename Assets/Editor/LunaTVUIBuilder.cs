using Game.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// 一次性把 LUNA 的 A/B 两套电视界面生成到场景里并保存。
    /// 只在手动点菜单时执行，不会在编辑器启动时自动运行，
    /// 也不会覆盖用户已经调好的界面（除非在弹窗里选择 Rebuild）。
    /// </summary>
    public static class LunaTVUIBuilder
    {
        private const string MenuPath =
            "Tools/VR Project/LUNA/Build TV UI In Scene";

        private const string HomeRootName = "LUNA Home";
        private const string LogRootName = "LUNA Log";
        private const string BackgroundName = "背景";

        [MenuItem(MenuPath)]
        public static void Build()
        {
            TVScreenUI screen = Object.FindFirstObjectByType<TVScreenUI>(
                FindObjectsInactive.Include);

            if (screen == null)
            {
                EditorUtility.DisplayDialog(
                    "LUNA TV UI",
                    "No TVScreenUI component found in the open scene.",
                    "OK");

                return;
            }

            Transform canvas = screen.transform;

            if (canvas.Find(HomeRootName) != null ||
                canvas.Find(LogRootName) != null)
            {
                bool rebuild = EditorUtility.DisplayDialog(
                    "LUNA TV UI",
                    "LUNA UI already exists under this canvas.\n\n" +
                    "Rebuild will delete the existing generated objects " +
                    "and regenerate them, losing manual tweaks.",
                    "Rebuild",
                    "Cancel");

                if (!rebuild)
                    return;
            }

            // 字体必须在删除旧文本对象之前取到。
            TMP_FontAsset font = ResolveFont(canvas);

            if (font == null)
            {
                EditorUtility.DisplayDialog(
                    "LUNA TV UI",
                    "No TMP font asset found. Assign a font on any " +
                    "TextMeshPro object first.",
                    "OK");

                return;
            }

            var serialized = new SerializedObject(screen);
            Color textColor = GetColor(serialized, "textColor");
            Color dimColor = GetColor(serialized, "dimColor");

            DestroyGenerated(canvas);
            RemoveLegacyChildren(canvas);
            RectTransform background = EnsureBackground(canvas);

            Rect rect = ((RectTransform)canvas).rect;
            float width = rect.width > 0f ? rect.width : 1600f;
            float height = rect.height > 0f ? rect.height : 900f;
            float k = height / 600f;
            int layer = canvas.gameObject.layer;

            // ---------------- 界面 A ----------------
            RectTransform home = CreateGroup(canvas, HomeRootName, layer);

            LunaMoonGraphic homeMoon = CreateMoon(
                home,
                "LUNA Moon",
                false,
                screen,
                layer,
                new Vector2(width * 0.235f, width * 0.235f),
                new Vector2(width * 0.285f, height * 0.60f),
                new Vector2(0.5f, 0.5f));

            TMP_Text nameText = CreateText(
                home,
                "LUNA Name",
                "LUNA",
                52f * k,
                TextAlignmentOptions.Center,
                new Vector2(width * 0.34f, 80f * k),
                new Vector2(width * 0.255f, height * 0.185f),
                new Vector2(0.5f, 0.5f),
                textColor,
                font,
                layer);

            nameText.characterSpacing = 18f;

            TMP_Text heading = CreateText(
                home,
                "LUNA Heading",
                "Need A Hint?",
                40f * k,
                TextAlignmentOptions.Left,
                new Vector2(width * 0.46f, 60f * k),
                new Vector2(width * 0.50f, height * 0.845f),
                new Vector2(0f, 0.5f),
                textColor,
                font,
                layer);

            heading.fontStyle = FontStyles.Bold;

            TMP_Text hints = CreateText(
                home,
                "LUNA Hints",
                BuildHints(),
                16f * k,
                TextAlignmentOptions.TopLeft,
                new Vector2(width * 0.47f, 150f * k),
                new Vector2(width * 0.50f, height * 0.775f),
                new Vector2(0f, 1f),
                textColor,
                font,
                layer);

            hints.lineSpacing = 16f;

            CreateText(
                home,
                "LUNA Footer",
                "Say LOG to see the questions you have asked.",
                14f * k,
                TextAlignmentOptions.Left,
                new Vector2(width * 0.48f, 40f * k),
                new Vector2(width * 0.487f, height * 0.155f),
                new Vector2(0f, 0.5f),
                dimColor,
                font,
                layer);

            // ---------------- 界面 B ----------------
            RectTransform log = CreateGroup(canvas, LogRootName, layer);

            LunaMoonGraphic logMoon = CreateMoon(
                log,
                "LUNA Log Moon",
                true,
                screen,
                layer,
                new Vector2(width * 0.13f, width * 0.13f),
                new Vector2(width * 0.13f, height * 0.825f),
                new Vector2(0.5f, 0.5f));

            CreateText(
                log,
                "LUNA Log Return Hint",
                "Touch me to return",
                12f * k,
                TextAlignmentOptions.Left,
                new Vector2(width * 0.30f, 30f * k),
                new Vector2(width * 0.049f, height * 0.705f),
                new Vector2(0f, 0.5f),
                dimColor,
                font,
                layer);

            TMP_Text logTitle = CreateText(
                log,
                "LUNA Log Title",
                "LUNA LOG",
                42f * k,
                TextAlignmentOptions.Center,
                new Vector2(width * 0.52f, 70f * k),
                new Vector2(width * 0.60f, height * 0.845f),
                new Vector2(0.5f, 0.5f),
                textColor,
                font,
                layer);

            logTitle.characterSpacing = 14f;

            TMP_Text logEntries = CreateText(
                log,
                "LUNA Log Entries",
                "No questions yet.",
                15f * k,
                TextAlignmentOptions.TopRight,
                new Vector2(width * 0.46f, 380f * k),
                new Vector2(width * 0.86f, height * 0.735f),
                new Vector2(1f, 1f),
                textColor,
                font,
                layer);

            logEntries.lineSpacing = 14f;
            logEntries.overflowMode = TextOverflowModes.Truncate;

            // ---------------- 绑定控制器 ----------------
            serialized.FindProperty("homeRoot").objectReferenceValue =
                home.gameObject;
            serialized.FindProperty("logRoot").objectReferenceValue =
                log.gameObject;
            serialized.FindProperty("homeMoon").objectReferenceValue =
                homeMoon;
            serialized.FindProperty("logMoon").objectReferenceValue =
                logMoon;
            serialized.FindProperty("logText").objectReferenceValue =
                logEntries;
            serialized.FindProperty("emptyLogText").stringValue =
                "No questions yet.";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            home.gameObject.SetActive(true);
            log.gameObject.SetActive(false);

            if (background != null)
                background.SetAsFirstSibling();

            EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
            EditorSceneManager.SaveScene(screen.gameObject.scene);

            Selection.activeGameObject = home.gameObject;

            Debug.Log(
                "LUNA TV UI generated and saved under '" +
                canvas.name + "'.",
                screen);
        }

        // ---------------- 生成 ----------------

        private static string BuildHints()
        {
            string[] hints =
            {
                "Look for clues.",
                "Make guesses. Ask questions (Press A).",
                "I will answer YES, NO or IRRELEVANT.",
                "Key questions change the scene.",
                "The changes reveal new clues.",
                "Think you have solved it? Tell me!"
            };

            var builder = new System.Text.StringBuilder();

            for (int i = 0; i < hints.Length; i++)
            {
                if (i > 0)
                    builder.Append('\n');

                builder.Append(i + 1).Append(". ").Append(hints[i]);
            }

            return builder.ToString();
        }

        private static RectTransform CreateGroup(
            Transform parent,
            string objectName,
            int layer)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.layer = layer;

            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;

            return rect;
        }

        private static LunaMoonGraphic CreateMoon(
            Transform parent,
            string objectName,
            bool clickable,
            TVScreenUI screen,
            int layer,
            Vector2 size,
            Vector2 position,
            Vector2 pivot)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.layer = layer;

            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localScale = Vector3.one;

            var graphic = go.AddComponent<LunaMoonGraphic>();
            graphic.raycastTarget = clickable;

            if (!clickable)
                return graphic;

            var button = go.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.None;
            UnityEventTools.AddPersistentListener(
                button.onClick,
                new UnityEngine.Events.UnityAction(screen.ShowHome));

            return graphic;
        }

        private static TMP_Text CreateText(
            Transform parent,
            string objectName,
            string content,
            float fontSize,
            TextAlignmentOptions alignment,
            Vector2 size,
            Vector2 position,
            Vector2 pivot,
            Color color,
            TMP_FontAsset font,
            int layer)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.layer = layer;

            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localScale = Vector3.one;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.richText = true;
            text.raycastTarget = false;

            return text;
        }

        // ---------------- 清理 ----------------

        private static void DestroyGenerated(Transform canvas)
        {
            DestroyChild(canvas, HomeRootName);
            DestroyChild(canvas, LogRootName);
        }

        private static void DestroyChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);

            if (child != null)
                Object.DestroyImmediate(child.gameObject);
        }

        /// <summary>
        /// 删除旧布局留下的纯 UI 子对象（标题/日志窗口/海龟汤窗口/清空日志/清空提问）。
        /// 只保留背景板，避免场景里堆废弃对象。
        /// </summary>
        private static void RemoveLegacyChildren(Transform canvas)
        {
            string[] keep =
            {
                BackgroundName,
                HomeRootName,
                LogRootName
            };

            var children = new System.Collections.Generic.List<Transform>();

            foreach (Transform child in canvas)
                children.Add(child);

            for (int i = 0; i < children.Count; i++)
            {
                Transform child = children[i];

                if (System.Array.IndexOf(keep, child.name) >= 0)
                    continue;

                Object.DestroyImmediate(child.gameObject);
            }
        }

        private static RectTransform EnsureBackground(Transform canvas)
        {
            var background = canvas.Find(BackgroundName) as RectTransform;

            if (background == null)
            {
                var go = new GameObject(
                    BackgroundName,
                    typeof(RectTransform),
                    typeof(Image));

                go.layer = canvas.gameObject.layer;

                background = (RectTransform)go.transform;
                background.SetParent(canvas, false);
                background.anchorMin = Vector2.zero;
                background.anchorMax = Vector2.one;
                background.offsetMin = Vector2.zero;
                background.offsetMax = Vector2.zero;
                background.pivot = new Vector2(0.5f, 0.5f);
                background.localScale = Vector3.one;
            }

            // 旧背景上挂着按钮，会被手部戳按误触，去掉。
            var button = background.GetComponent<Button>();

            if (button != null)
                Object.DestroyImmediate(button);

            var image = background.GetComponent<Image>();

            if (image != null)
            {
                image.color = new Color(0.23f, 0.23f, 0.23f, 1f);
                image.raycastTarget = false;
            }

            background.SetAsFirstSibling();
            return background;
        }

        // ---------------- 资源 ----------------

        private static TMP_FontAsset ResolveFont(Transform canvas)
        {
            TMP_Text[] texts = canvas.GetComponentsInChildren<TMP_Text>(true);

            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].font != null)
                    return texts[i].font;
            }

            return TMP_Settings.defaultFontAsset;
        }

        private static Color GetColor(
            SerializedObject serialized,
            string propertyName)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);

            return property != null
                ? property.colorValue
                : Color.white;
        }
    }
}
