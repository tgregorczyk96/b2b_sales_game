using System.Collections;
using NUnit.Framework;
using SalesSim.Application;
using SalesSim.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SalesSim.Tests.PlayMode
{
    /// <summary>
    /// Copying chat messages and guidance with the "Kopieren" buttons (through a fake clipboard, so no test depends on
    /// the Windows clipboard), plus the selectable read-only guidance text. The real Ctrl+C path to the system clipboard
    /// is checked where the clipboard is accessible and skipped otherwise.
    /// </summary>
    public sealed class CopyTests
    {
        private ScriptedSession session;
        private ConversationHistoryView history;
        private GuidancePanelView guidance;
        private TMP_InputField input;
        private Button send;
        private FakeClipboard clipboard;
        private IClipboard original;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("SalesTestScene");
            yield return null;

            history = Object.FindAnyObjectByType<ConversationHistoryView>();
            guidance = Object.FindAnyObjectByType<GuidancePanelView>();
            input = GameObject.Find("PlayerInput").GetComponent<TMP_InputField>();
            send = GameObject.Find("SendButton").GetComponent<Button>();

            original = TextClipboard.Current;
            clipboard = new FakeClipboard();
            TextClipboard.Current = clipboard;

            session = new ScriptedSession();
            var loop = Object.FindAnyObjectByType<SalesRunLoop>();
            loop.Configure(session, new ScriptedScenarioSource(), null);
            loop.StartNewRun("Easy", 42);
            yield return null;
        }

        [TearDown]
        public void RestoreClipboard()
        {
            TextClipboard.Current = original;
        }

        [UnityTest]
        public IEnumerator CustomerAndPlayerMessages_CanBeCopied_WithoutChangingTheText()
        {
            input.text = "Haben Sie dienstags freie Termine?";
            send.onClick.Invoke();
            session.Answer("Dienstags ist es eher ruhig.");
            yield return null;

            foreach (var message in history.Messages)
            {
                var before = message.Text;
                message.CopyButton.Button.onClick.Invoke();

                Assert.That(clipboard.Text, Is.EqualTo(before), message.Sender.ToString());
                Assert.That(message.Text, Is.EqualTo(before), "Copying never changes the visible text.");
                Assert.That(message.IsReadOnly, Is.True);
            }

            Assert.That(clipboard.Copied, Is.EqualTo(new[] { "Guten Tag?", "Haben Sie dienstags freie Termine?", "Dienstags ist es eher ruhig." }));
        }

        [UnityTest]
        public IEnumerator CopyButton_ShowsKopiert_ThenResets()
        {
            var button = history.Messages[0].CopyButton;
            Assert.That(button.Label, Is.EqualTo("Kopieren"));

            button.Button.onClick.Invoke();
            Assert.That(button.Label, Is.EqualTo("Kopiert"));

            yield return new WaitForSecondsRealtime(1.8f);
            Assert.That(button.Label, Is.EqualTo("Kopieren"));
        }

        [UnityTest]
        public IEnumerator Hint_HintMore_AndExample_CanBeCopied()
        {
            Press(GuidanceLevel.Hint);
            Press(GuidanceLevel.HintMore);
            Press(GuidanceLevel.Example);
            session.AnswerExample();
            yield return null;

            Copy(GuidanceLevel.Hint);
            Assert.That(clipboard.Text, Is.EqualTo(ScriptedSession.HintText));
            Copy(GuidanceLevel.HintMore);
            Assert.That(clipboard.Text, Is.EqualTo(ScriptedSession.HintMoreText));
            Copy(GuidanceLevel.Example);
            Assert.That(clipboard.Text, Is.EqualTo(ScriptedSession.ExampleText));
            Assert.That(guidance.TextOf(GuidanceLevel.Example), Is.EqualTo(ScriptedSession.ExampleText), "The text stays as it was.");
            Assert.That(history.Messages.Count, Is.EqualTo(1), "Copying guidance never touches the chat.");
        }

        [UnityTest]
        public IEnumerator CopyButton_IsOnlyThere_WhenThereIsFinishedGuidance()
        {
            Assert.That(guidance.CopyButtonFor(GuidanceLevel.Example).gameObject.activeInHierarchy, Is.False);

            Press(GuidanceLevel.Example);
            yield return null;
            Assert.That(guidance.StateOf(GuidanceLevel.Example), Is.EqualTo(GuidanceSectionState.Loading));
            Assert.That(guidance.TextOf(GuidanceLevel.Example), Does.StartWith("Beispiel wird formuliert"));
            Assert.That(guidance.CopyButtonFor(GuidanceLevel.Example).gameObject.activeInHierarchy, Is.False, "Nothing to copy while loading.");

            session.FailExample();
            yield return null;
            Assert.That(guidance.CopyButtonFor(GuidanceLevel.Example).gameObject.activeInHierarchy, Is.False, "Errors are not copied.");

            Press(GuidanceLevel.Example);
            session.AnswerExample();
            yield return null;
            Assert.That(guidance.CopyButtonFor(GuidanceLevel.Example).gameObject.activeInHierarchy, Is.True);
        }

        [UnityTest]
        public IEnumerator Copying_KeepsTheDraft_AndGivesTheFocusBackToTheInput()
        {
            Press(GuidanceLevel.Hint);
            input.text = "Ich wollte fragen, ob";
            input.caretPosition = input.text.Length;

            Copy(GuidanceLevel.Hint);
            history.Messages[0].CopyButton.Button.onClick.Invoke();
            yield return null;
            yield return null;

            Assert.That(input.text, Is.EqualTo("Ich wollte fragen, ob"), "The draft survives copying.");
            Assert.That(input.isFocused, Is.True, "Straight back to writing.");
            Assert.That(input.caretPosition, Is.EqualTo(input.text.Length), "Typing continues at the end of the draft.");
            Assert.That(input.selectionAnchorPosition, Is.EqualTo(input.selectionFocusPosition), "Nothing selected that a keystroke could overwrite.");
        }

        [UnityTest]
        public IEnumerator GuidanceText_IsSelectable_ButReadOnly()
        {
            Press(GuidanceLevel.Example);
            session.AnswerExample();
            yield return null;

            var body = guidance.BodyFor(GuidanceLevel.Example);
            Assert.That(body.readOnly, Is.True);
            Assert.That(body.interactable, Is.True, "Selectable with the mouse.");

            body.ActivateInputField();
            yield return null;
            body.ProcessEvent(Event.KeyboardEvent("x"));
            body.ProcessEvent(Event.KeyboardEvent("backspace"));
            body.ProcessEvent(Event.KeyboardEvent("^v"));
            body.ProcessEvent(Event.KeyboardEvent("^x"));
            Assert.That(guidance.TextOf(GuidanceLevel.Example), Is.EqualTo(ScriptedSession.ExampleText));
            body.DeactivateInputField();
        }

        [UnityTest]
        public IEnumerator GuidanceSelection_IsCopiedWithCtrlC_ToTheSystemClipboard()
        {
            // TMP's own Ctrl+C path writes to the operating system clipboard; when another process holds it, skip.
            GUIUtility.systemCopyBuffer = "clipboard-probe";
            if (GUIUtility.systemCopyBuffer != "clipboard-probe")
            {
                Assert.Ignore("The system clipboard is not accessible in this environment.");
            }

            Press(GuidanceLevel.Hint);
            yield return null;
            var body = guidance.BodyFor(GuidanceLevel.Hint);
            body.ActivateInputField();
            yield return null;
            body.selectionStringAnchorPosition = 0;
            body.selectionStringFocusPosition = body.text.Length;
            GUIUtility.systemCopyBuffer = string.Empty;
            body.ProcessEvent(Event.KeyboardEvent("^c"));

            Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo(ScriptedSession.HintText));
            body.DeactivateInputField();
        }

        private void Press(GuidanceLevel level) => guidance.ButtonFor(level).onClick.Invoke();

        private void Copy(GuidanceLevel level) => guidance.CopyButtonFor(level).Button.onClick.Invoke();
    }
}
