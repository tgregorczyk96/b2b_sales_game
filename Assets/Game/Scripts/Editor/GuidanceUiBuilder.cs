using SalesSim.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Editor
{
    /// <summary>
    /// Adds the live guidance panel to SalesTestScene: below the chat, next to a narrowed debug panel, directly above the
    /// input row — three buttons (Hinweis, Mehr Hinweis, Beispiel) and a scrollable area with one section per level.
    /// Run via "Sales Sim/Build Guidance UI" or <c>-executeMethod SalesSim.Editor.GuidanceUiBuilder.Build</c>.
    /// Idempotent: replaces what an earlier run created. The chat panel is not touched.
    /// </summary>
    public static class GuidanceUiBuilder
    {
        private const string ScenePath = "Assets/Game/Scenes/SalesTestScene.unity";

        private static readonly Color PanelColor = new Color(0.13f, 0.15f, 0.19f, 0.92f);
        private static readonly Color ButtonColor = new Color(0.93f, 0.94f, 0.96f, 1f);
        private static readonly Color ExampleButtonColor = new Color(0.78f, 0.87f, 1f, 1f);
        private static readonly Color TitleColor = new Color(0.55f, 0.78f, 1f, 1f);
        private static readonly Color MutedColor = new Color(0.68f, 0.72f, 0.8f, 1f);

        [MenuItem("Sales Sim/Build Guidance UI")]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = GameObject.Find("UIRoot").transform;
            var controller = Object.FindAnyObjectByType<SalesConversationController>();

            var old = root.Find("GuidancePanel");
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            // The debug readout moves to the right third so guidance gets the readable space under the chat.
            Anchor(GameObject.Find("DebugPanel").GetComponent<RectTransform>(), 0.72f, 0.24f, 0.96f, 0.53f);

            var panel = NewUi("GuidancePanel", root);
            Anchor(panel, 0.42f, 0.195f, 0.705f, 0.53f);
            panel.gameObject.AddComponent<Image>().color = PanelColor;
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 12);
            layout.spacing = 10f;
            SetChildControl(layout, expandWidth: true);

            var scroll = NewUi("Scroll", panel);
            scroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;

            var viewport = NewUi("Viewport", scroll);
            Anchor(viewport, 0f, 0f, 1f, 1f);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // catches the mouse wheel

            var content = NewUi("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 14f;
            SetChildControl(contentLayout, expandWidth: true);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport = viewport;
            scrollRect.content = content;

            var empty = NewText("EmptyHint", content, 22f, MutedColor);
            empty.fontStyle = FontStyles.Italic;
            empty.text = "Kommst du nicht weiter? Hol dir einen Hinweis – oder gleich ein Beispiel.";

            var hint = NewSection("HintSection", content, "Hinweis");
            var hintMore = NewSection("HintMoreSection", content, "Mehr Hinweis");
            var example = NewSection("ExampleSection", content, "Beispiel");

            var row = NewUi("Buttons", panel);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = true;
            var rowSize = row.gameObject.AddComponent<LayoutElement>();
            rowSize.preferredHeight = 54f;
            rowSize.flexibleHeight = 0f; // the row's layout group would otherwise claim spare height from the text area
            var hintButton = NewButton("HintButton", row, "Hinweis", ButtonColor);
            var hintMoreButton = NewButton("HintMoreButton", row, "Mehr Hinweis", ButtonColor);
            var exampleButton = NewButton("ExampleButton", row, "Beispiel", ExampleButtonColor);

            var view = panel.gameObject.AddComponent<GuidancePanelView>();
            var so = new SerializedObject(view);
            so.FindProperty("hintButton").objectReferenceValue = hintButton;
            so.FindProperty("hintMoreButton").objectReferenceValue = hintMoreButton;
            so.FindProperty("exampleButton").objectReferenceValue = exampleButton;
            so.FindProperty("scrollRect").objectReferenceValue = scrollRect;
            so.FindProperty("content").objectReferenceValue = content;
            so.FindProperty("emptyHint").objectReferenceValue = empty.gameObject;
            AssignSection(so, "hint", hint);
            AssignSection(so, "hintMore", hintMore);
            AssignSection(so, "example", example);
            so.ApplyModifiedPropertiesWithoutUndo();

            var controllerSo = new SerializedObject(controller);
            controllerSo.FindProperty("guidance").objectReferenceValue = view;
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            // After a guidance click the input is focused again; it must keep the player's draft instead of selecting it.
            var input = GameObject.Find("PlayerInput").GetComponent<TMP_InputField>();
            input.onFocusSelectAll = false;
            EditorUtility.SetDirty(input);

            // Keep the result and setup overlays on top of everything.
            foreach (var name in new[] { "ResultOverlay", "SetupOverlay" })
            {
                var overlay = root.Find(name);
                if (overlay != null)
                {
                    overlay.SetAsLastSibling();
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[GuidanceUiBuilder] SalesTestScene guidance UI built.");
        }

        /// <summary>
        /// One guidance level: title with "Kopieren" button, and the text as read-only selectable text (mouse selection +
        /// Ctrl+C, like the chat). A plain TMP text cannot be selected, so guidance could not be copied before.
        /// </summary>
        private static (GameObject Root, SelectableMessageText Body, CopyTextButton Copy) NewSection(string name, Transform parent, string title)
        {
            var section = NewUi(name, parent);
            var layout = section.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            SetChildControl(layout, expandWidth: true);

            var heading = NewText("Title", section, 18f, TitleColor);
            heading.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            heading.text = title;
            CopyUiParts.NewHeaderRow(section, heading, out var copy);

            var body = CopyUiParts.NewSelectableText("Body", section, 24f, Color.white);
            section.gameObject.SetActive(false);
            return (section.gameObject, body, copy);
        }

        private static void AssignSection(SerializedObject so, string field, (GameObject Root, SelectableMessageText Body, CopyTextButton Copy) section)
        {
            var property = so.FindProperty(field);
            property.FindPropertyRelative("root").objectReferenceValue = section.Root;
            property.FindPropertyRelative("body").objectReferenceValue = section.Body;
            property.FindPropertyRelative("copyButton").objectReferenceValue = section.Copy;
        }

        private static Button NewButton(string name, Transform parent, string label, Color color)
        {
            var rect = NewUi(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var text = NewText("Label", rect, 22f, new Color(0.15f, 0.16f, 0.2f));
            text.alignment = TextAlignmentOptions.Center;
            text.text = label;
            Anchor(text.rectTransform, 0f, 0f, 1f, 1f);
            return button;
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
            text.raycastTarget = false;
            return text;
        }

        private static void Anchor(RectTransform rect, float minX, float minY, float maxX, float maxY)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
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
