using SalesSim.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Editor
{
    /// <summary>
    /// Builds the chat history UI of SalesTestScene (scroll view, thinking bubble) and the chat message prefab.
    /// Run once via "Sales Sim/Build Conversation UI" or
    /// <c>-executeMethod SalesSim.Editor.ConversationUiBuilder.Build</c>; it replaces the former single CustomerText.
    /// </summary>
    public static class ConversationUiBuilder
    {
        private const string ScenePath = "Assets/Game/Scenes/SalesTestScene.unity";
        private const string PrefabPath = "Assets/Game/Prefabs/ConversationMessage.prefab";

        private static readonly Color SenderColor = new Color(0.72f, 0.76f, 0.84f, 1f);
        private static readonly Color SelectionColor = new Color(0.4f, 0.62f, 1f, 0.45f);

        [MenuItem("Sales Sim/Build Conversation UI")]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var panel = GameObject.Find("CustomerDialoguePanel").GetComponent<RectTransform>();
            var controller = Object.FindAnyObjectByType<SalesConversationController>();

            var oldText = panel.Find("CustomerText");
            if (oldText != null)
            {
                Object.DestroyImmediate(oldText.gameObject);
            }

            var existing = panel.Find("ConversationHistory");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var prefab = BuildMessagePrefab();

            var historyRoot = NewUi("ConversationHistory", panel);
            Stretch(historyRoot, 12f);
            var scroll = historyRoot.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var viewport = NewUi("Viewport", historyRoot);
            Stretch(viewport, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // catches wheel/drag on empty space

            var content = NewUi("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(4, 4, 4, 8);
            contentLayout.spacing = 10f;
            SetChildControl(contentLayout, expandWidth: true);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;

            var thinking = BuildThinkingIndicator(content);

            var history = historyRoot.gameObject.AddComponent<ConversationHistoryView>();
            var historySo = new SerializedObject(history);
            historySo.FindProperty("scrollRect").objectReferenceValue = scroll;
            historySo.FindProperty("content").objectReferenceValue = content;
            historySo.FindProperty("messagePrefab").objectReferenceValue = prefab;
            historySo.FindProperty("thinkingIndicator").objectReferenceValue = thinking;
            historySo.ApplyModifiedPropertiesWithoutUndo();

            var controllerSo = new SerializedObject(controller);
            controllerSo.FindProperty("history").objectReferenceValue = history;
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[ConversationUiBuilder] SalesTestScene chat history built.");
        }

        private static ConversationMessageView BuildMessagePrefab()
        {
            var row = new GameObject("ConversationMessage", typeof(RectTransform));
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            SetChildControl(rowLayout, expandWidth: false);

            var bubble = NewUi("Bubble", row.transform);
            var background = bubble.gameObject.AddComponent<Image>();
            var bubbleLayout = bubble.gameObject.AddComponent<VerticalLayoutGroup>();
            bubbleLayout.padding = new RectOffset(16, 16, 10, 12);
            bubbleLayout.spacing = 4f;
            SetChildControl(bubbleLayout, expandWidth: true);
            bubble.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var sender = NewText("Sender", bubble, 18f, SenderColor);
            sender.fontStyle = FontStyles.Bold;
            CopyUiParts.NewHeaderRow(bubble, sender, out var copyButton);

            // Standard TMP input field layout: text area and text are stretched by anchors, no layout groups inside.
            // The input field itself reports the text's preferred height to the bubble (it is an ILayoutElement).
            var bodyRoot = NewUi("Body", bubble);
            var textArea = NewUi("Text Area", bodyRoot);
            Stretch(textArea, 0f);
            textArea.gameObject.AddComponent<RectMask2D>();
            var text = NewText("Text", textArea, 24f, Color.white);
            Stretch(text.rectTransform, 0f);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = false;

            var body = bodyRoot.gameObject.AddComponent<SelectableMessageText>();
            body.textViewport = textArea;
            body.textComponent = text;
            body.lineType = TMP_InputField.LineType.MultiLineNewline;
            body.readOnly = true;
            body.richText = false;
            body.onFocusSelectAll = false;
            body.resetOnDeActivation = true;
            body.selectionColor = SelectionColor;
            body.transition = Selectable.Transition.None;
            body.navigation = new Navigation { mode = Navigation.Mode.None };

            var view = row.AddComponent<ConversationMessageView>();
            var viewSo = new SerializedObject(view);
            viewSo.FindProperty("row").objectReferenceValue = rowLayout;
            viewSo.FindProperty("background").objectReferenceValue = background;
            viewSo.FindProperty("senderLabel").objectReferenceValue = sender;
            viewSo.FindProperty("body").objectReferenceValue = body;
            viewSo.FindProperty("copyButton").objectReferenceValue = copyButton;
            viewSo.ApplyModifiedPropertiesWithoutUndo();

            if (!AssetDatabase.IsValidFolder("Assets/Game/Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets/Game", "Prefabs");
            }

            var saved = PrefabUtility.SaveAsPrefabAsset(row, PrefabPath);
            Object.DestroyImmediate(row);
            return saved.GetComponent<ConversationMessageView>();
        }

        private static ThinkingIndicatorView BuildThinkingIndicator(RectTransform content)
        {
            var row = NewUi("ThinkingIndicator", content);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            SetChildControl(rowLayout, expandWidth: false);

            var bubble = NewUi("Bubble", row);
            bubble.gameObject.AddComponent<Image>().color = new Color(0.2f, 0.22f, 0.27f, 0.95f);
            var bubbleLayout = bubble.gameObject.AddComponent<HorizontalLayoutGroup>();
            bubbleLayout.padding = new RectOffset(18, 18, 6, 10);
            SetChildControl(bubbleLayout, expandWidth: false);
            var size = bubble.gameObject.AddComponent<LayoutElement>();
            size.minWidth = 84f;

            var dots = NewText("Dots", bubble, 30f, Color.white);
            dots.fontStyle = FontStyles.Bold;
            dots.text = "...";

            var view = row.gameObject.AddComponent<ThinkingIndicatorView>();
            var viewSo = new SerializedObject(view);
            viewSo.FindProperty("dots").objectReferenceValue = dots;
            viewSo.ApplyModifiedPropertiesWithoutUndo();
            row.gameObject.SetActive(false);
            return view;
        }

        private static RectTransform NewUi(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, float size, Color color)
        {
            var text = NewUi(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.color = color;
            return text;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void SetChildControl(HorizontalOrVerticalLayoutGroup layout, bool expandWidth)
        {
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = expandWidth;
            layout.childForceExpandHeight = false;
        }
    }
}
