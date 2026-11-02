using SalesSim.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Editor
{
    /// <summary>
    /// Adds push-to-talk to SalesTestScene's input row: a "Sprechen" button between the input and "Senden" (the input is
    /// narrowed for it), a pulsing recording dot on the button and a status line below the row.
    /// Run via "Sales Sim/Build Speech Input UI" or <c>-executeMethod SalesSim.Editor.SpeechInputUiBuilder.Build</c>.
    /// Idempotent: replaces what an earlier run created.
    /// </summary>
    public static class SpeechInputUiBuilder
    {
        private const string ScenePath = "Assets/Game/Scenes/SalesTestScene.unity";

        private static readonly Color ButtonColor = new Color(0.93f, 0.94f, 0.96f, 1f);
        private static readonly Color LabelColor = new Color(0.15f, 0.16f, 0.2f, 1f);
        private static readonly Color DotColor = new Color(1f, 0.92f, 0.92f, 1f);
        private static readonly Color StatusColor = new Color(0.78f, 0.81f, 0.88f, 1f);

        [MenuItem("Sales Sim/Build Speech Input UI")]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var input = GameObject.Find("PlayerInput").GetComponent<RectTransform>();
            var parent = input.parent;
            var controller = Object.FindAnyObjectByType<SalesConversationController>();

            foreach (var name in new[] { "MicrophoneButton", "SpeechStatusLabel" })
            {
                var old = parent.Find(name);
                if (old != null)
                {
                    Object.DestroyImmediate(old.gameObject);
                }
            }

            // Input 0.04–0.70, microphone 0.71–0.80, "Senden" stays at 0.82–0.96 (same row height 0.05–0.18).
            Anchor(input, 0.04f, 0.05f, 0.70f, 0.18f);

            var buttonRect = NewUi("MicrophoneButton", parent);
            buttonRect.SetSiblingIndex(input.GetSiblingIndex() + 1);
            Anchor(buttonRect, 0.71f, 0.05f, 0.80f, 0.18f);
            var image = buttonRect.gameObject.AddComponent<Image>();
            image.color = ButtonColor;
            var button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            // Enter is the push-to-talk key; keyboard navigation must not select the button and "submit" it as well.
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var label = NewText("Label", buttonRect, 24f, LabelColor);
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            label.text = "Sprechen";
            Anchor(label.rectTransform, 0f, 0f, 1f, 1f);

            var dot = NewUi("RecordingDot", buttonRect);
            dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.82f);
            dot.sizeDelta = new Vector2(14f, 14f);
            var dotImage = dot.gameObject.AddComponent<Image>();
            dotImage.color = DotColor;
            dotImage.raycastTarget = false;
            dot.gameObject.SetActive(false);

            var status = NewText("SpeechStatusLabel", parent, 20f, StatusColor);
            Anchor(status.rectTransform, 0.04f, 0.008f, 0.80f, 0.048f);
            status.alignment = TextAlignmentOptions.MidlineLeft;
            status.textWrappingMode = TextWrappingModes.NoWrap;
            status.overflowMode = TextOverflowModes.Ellipsis;

            var view = buttonRect.gameObject.AddComponent<SpeechInputView>();
            var so = new SerializedObject(view);
            so.FindProperty("button").objectReferenceValue = button;
            so.FindProperty("buttonImage").objectReferenceValue = image;
            so.FindProperty("buttonLabel").objectReferenceValue = label;
            so.FindProperty("statusLabel").objectReferenceValue = status;
            so.FindProperty("recordingDot").objectReferenceValue = dotImage;
            so.ApplyModifiedPropertiesWithoutUndo();

            var controllerSo = new SerializedObject(controller);
            controllerSo.FindProperty("speech").objectReferenceValue = view;
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            // Keep the result and setup overlays on top of everything.
            var root = GameObject.Find("UIRoot").transform;
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
            Debug.Log("[SpeechInputUiBuilder] SalesTestScene speech input UI built.");
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
    }
}
