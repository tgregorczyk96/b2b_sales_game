using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    /// Push-to-talk in SalesTestScene against a scripted session and scripted speech input: Enter / microphone button
    /// start and send, Escape discards, the transcript goes through the input into the ordinary send path, and nothing
    /// but a recognized transcript ever becomes a turn. The keys are pressed through the controller's key handlers
    /// (<see cref="SalesConversationController.PressTalkKey"/>, <see cref="SalesConversationController.PressCancelKey"/>),
    /// which its Update calls for Enter / Escape. The engine side is covered by <c>SpeechInputTests</c> (EditMode).
    /// </summary>
    public sealed class PushToTalkTests
    {
        private const string Spoken = "Guten Tag, ich bin Tomasz von tom-gre-it.";

        private ScriptedSession session;
        private ScriptedSpeechInput speechInput;
        private SalesConversationController controller;
        private ConversationHistoryView history;
        private GuidancePanelView guidance;
        private SpeechInputView speech;
        private SalesRunLoop loop;
        private TMP_InputField input;
        private Button send;
        private Button end;
        private Button microphone;
        private List<string> inputValues;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("SalesTestScene");
            yield return null;

            controller = Object.FindAnyObjectByType<SalesConversationController>();
            history = Object.FindAnyObjectByType<ConversationHistoryView>();
            guidance = Object.FindAnyObjectByType<GuidancePanelView>();
            speech = Object.FindAnyObjectByType<SpeechInputView>();
            loop = Object.FindAnyObjectByType<SalesRunLoop>();
            input = GameObject.Find("PlayerInput").GetComponent<TMP_InputField>();
            send = GameObject.Find("SendButton").GetComponent<Button>();
            end = GameObject.Find("EndConversationButton").GetComponent<Button>();
            microphone = GameObject.Find("MicrophoneButton").GetComponent<Button>();
            inputValues = new List<string>();
            input.onValueChanged.AddListener(inputValues.Add);

            speechInput = new ScriptedSpeechInput();
            controller.SetSpeechInput(speechInput);
            session = new ScriptedSession();
            loop.Configure(session, new ScriptedScenarioSource(), null);
            loop.StartNewRun("Easy", 42);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Ready_ShowsTheMicrophoneButton_NextToTheInput()
        {
            Assert.That(speech.State, Is.EqualTo(SpeechInputViewState.Ready));
            Assert.That(speech.ButtonText, Is.EqualTo("Sprechen"));
            Assert.That(microphone.interactable, Is.True);
            Assert.That(speech.StatusText, Does.Contain("Enter"));
            yield break;
        }

        [UnityTest]
        public IEnumerator Enter_StartsRecording_AndLocksSendingAndGuidance()
        {
            controller.PressTalkKey();
            yield return null;

            Assert.That(speechInput.StartRequests, Is.EqualTo(1));
            Assert.That(controller.IsRecording, Is.True);
            Assert.That(speech.State, Is.EqualTo(SpeechInputViewState.Recording));
            Assert.That(speech.ButtonText, Is.EqualTo("Stopp"));
            Assert.That(speech.StatusText, Does.Contain("Aufnahme läuft"));
            Assert.That(microphone.interactable, Is.True, "The button stops the recording.");
            Assert.That(send.interactable, Is.False);
            Assert.That(input.interactable, Is.False);
            Assert.That(guidance.IsInteractable, Is.False);
            Assert.That(session.TurnsReceived, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator PartialTranscripts_AreShownInTheInput_WhileRecording()
        {
            controller.PressTalkKey();
            yield return null;

            speechInput.Partials.Report("Guten Tag");
            yield return null;

            Assert.That(input.text, Is.EqualTo("Guten Tag"));
            Assert.That(session.TurnsReceived, Is.EqualTo(0), "Partial transcripts are never sent.");
        }

        [UnityTest]
        public IEnumerator EnterAgain_TranscribesAndSends_TheTranscriptLikeATypedTurn()
        {
            controller.PressTalkKey();
            yield return null;
            controller.PressTalkKey();
            yield return null;

            Assert.That(speechInput.Current.FinishRequested, Is.True);
            Assert.That(speech.State, Is.EqualTo(SpeechInputViewState.Processing));
            Assert.That(speech.StatusText, Does.Contain("Sprache wird erkannt"));
            Assert.That(microphone.interactable, Is.False, "No second recording while the first is processed.");
            Assert.That(send.interactable, Is.False);

            speechInput.Current.Complete(Spoken);
            yield return null;

            Assert.That(inputValues, Does.Contain(Spoken), "The transcript is shown in the input before it is sent.");
            Assert.That(session.TurnsReceived, Is.EqualTo(1));
            Assert.That(history.Messages.Last().Sender, Is.EqualTo(ConversationSender.Player));
            Assert.That(history.Messages.Last().Text, Is.EqualTo(Spoken));
            Assert.That(history.IsThinking, Is.True);
            Assert.That(speechInput.Current.Disposed, Is.True);

            // The customer answers exactly as after a typed turn; the indicators come from the session unchanged.
            session.Answer("Ach so, worum geht's?", new SalesIndicators(55, 60, 30, 65));
            yield return null;

            Assert.That(history.Messages.Last().Sender, Is.EqualTo(ConversationSender.Customer));
            Assert.That(history.Messages.Last().Text, Is.EqualTo("Ach so, worum geht's?"));
            Assert.That(session.CurrentState.Indicators.Trust, Is.EqualTo(55));
            Assert.That(speech.State, Is.EqualTo(SpeechInputViewState.Ready));
            Assert.That(microphone.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator MicrophoneButton_StartsAndStopsTheRecording()
        {
            microphone.onClick.Invoke();
            yield return null;
            Assert.That(controller.IsRecording, Is.True);

            yield return null; // the next frame: one toggle per frame
            microphone.onClick.Invoke();
            yield return null;
            Assert.That(speechInput.Current.FinishRequested, Is.True);

            speechInput.Current.Complete(Spoken);
            yield return null;
            Assert.That(session.TurnsReceived, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Escape_DiscardsTheRecording_WithoutATurn()
        {
            controller.PressTalkKey();
            yield return null;
            speechInput.Partials.Report("Guten");
            yield return null;

            controller.PressCancelKey();
            yield return null;

            Assert.That(speechInput.Current.Disposed, Is.True);
            Assert.That(speechInput.Current.FinishRequested, Is.False, "No commit, so no transcript.");
            Assert.That(session.TurnsReceived, Is.EqualTo(0));
            Assert.That(history.Messages.Count(m => m.Sender == ConversationSender.Player), Is.EqualTo(0));
            Assert.That(input.text, Is.Empty);
            Assert.That(speech.StatusText, Does.Contain("abgebrochen"));
            Assert.That(send.interactable, Is.True);
            Assert.That(input.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator Escape_WhileProcessing_DropsTheLateTranscript()
        {
            controller.PressTalkKey();
            yield return null;
            controller.PressTalkKey();
            yield return null;

            controller.PressCancelKey();
            speechInput.Current.Complete(Spoken);
            yield return null;
            yield return null;

            Assert.That(session.TurnsReceived, Is.EqualTo(0));
            Assert.That(input.text, Is.Empty);
            Assert.That(controller.IsSpeechInProgress, Is.False);
        }

        [UnityTest]
        public IEnumerator EmptyTranscript_SendsNothing_AndSaysSo()
        {
            controller.PressTalkKey();
            yield return null;
            controller.PressTalkKey();
            yield return null;

            speechInput.Current.Complete(string.Empty);
            yield return null;

            Assert.That(session.TurnsReceived, Is.EqualTo(0));
            Assert.That(speech.State, Is.EqualTo(SpeechInputViewState.Error));
            Assert.That(speech.StatusText, Does.Contain("Keine Sprache erkannt"));
            Assert.That(microphone.interactable, Is.True, "Trying again is possible.");
            Assert.That(input.interactable, Is.True);
        }

        [TestCase(SpeechInputError.Authentication, "API-Key", ExpectedResult = null)]
        [TestCase(SpeechInputError.Unreachable, "nicht erreichbar", ExpectedResult = null)]
        [TestCase(SpeechInputError.NoMicrophone, "Kein Mikrofon", ExpectedResult = null)]
        [TestCase(SpeechInputError.MicrophoneDenied, "Kein Zugriff", ExpectedResult = null)]
        [TestCase(SpeechInputError.NotConfigured, "nicht eingerichtet", ExpectedResult = null)]
        [UnityTest]
        public IEnumerator StartFailure_IsShown_WithoutATurn_AndTheGameGoesOn(SpeechInputError error, string expected)
        {
            speechInput.StartError = new SpeechInputException(error, "scripted");

            controller.PressTalkKey();
            yield return null;

            Assert.That(session.TurnsReceived, Is.EqualTo(0));
            Assert.That(speech.State, Is.EqualTo(SpeechInputViewState.Error));
            Assert.That(speech.StatusText, Does.Contain(expected));
            Assert.That(controller.IsSpeechInProgress, Is.False);
            Assert.That(input.interactable, Is.True);
            Assert.That(send.interactable, Is.True);

            // The next attempt works.
            controller.PressTalkKey();
            yield return null;
            Assert.That(controller.IsRecording, Is.True);
        }

        [UnityTest]
        public IEnumerator TranscriptionFailure_IsShown_AndKeepsTheDraft()
        {
            input.text = "Mein Entwurf";
            microphone.onClick.Invoke(); // the button also works with a typed draft; Enter would not
            yield return null;
            controller.PressTalkKey();
            yield return null;

            speechInput.Current.Fail(SpeechInputError.Timeout);
            yield return null;

            Assert.That(session.TurnsReceived, Is.EqualTo(0));
            Assert.That(speech.State, Is.EqualTo(SpeechInputViewState.Error));
            Assert.That(speech.StatusText, Does.Contain("nicht rechtzeitig"));
            Assert.That(input.text, Is.EqualTo("Mein Entwurf"));
        }

        [UnityTest]
        public IEnumerator LostConnectionWhileRecording_EndsTheRecording_WithoutATurn()
        {
            controller.PressTalkKey();
            yield return null;

            speechInput.Current.Interrupt(SpeechInputError.Unreachable);
            yield return null;

            Assert.That(session.TurnsReceived, Is.EqualTo(0));
            Assert.That(speechInput.Current.FinishRequested, Is.False);
            Assert.That(speechInput.Current.Disposed, Is.True);
            Assert.That(speech.State, Is.EqualTo(SpeechInputViewState.Error));
        }

        [UnityTest]
        public IEnumerator EndingTheConversation_WhileRecording_DiscardsTheRecording()
        {
            controller.PressTalkKey();
            yield return null;
            Assert.That(end.interactable, Is.True);

            end.onClick.Invoke();
            yield return null;
            speechInput.Current.Complete(Spoken);
            yield return null;

            Assert.That(speechInput.Current.Disposed, Is.True);
            Assert.That(session.TurnsReceived, Is.EqualTo(0));
            Assert.That(controller.IsConversationOver, Is.True);
            Assert.That(controller.IsSpeechInProgress, Is.False);

            controller.PressTalkKey();
            yield return null;
            Assert.That(speechInput.StartRequests, Is.EqualTo(1), "No recording after the conversation has ended.");
        }

        [UnityTest]
        public IEnumerator EnterAndButton_InTheSameFrame_ToggleOnlyOnce()
        {
            controller.PressTalkKey();
            microphone.onClick.Invoke();
            yield return null;

            Assert.That(speechInput.StartRequests, Is.EqualTo(1));
            Assert.That(controller.IsRecording, Is.True, "Not started and stopped in one go.");
        }

        [UnityTest]
        public IEnumerator RepeatedEnter_SendsOnlyOneTurn()
        {
            controller.PressTalkKey();
            yield return null;
            controller.PressTalkKey();
            yield return null;
            controller.PressTalkKey(); // while processing: ignored
            microphone.onClick.Invoke();
            yield return null;

            speechInput.Current.Complete(Spoken);
            yield return null;
            controller.PressTalkKey(); // while the customer thinks: ignored
            yield return null;

            Assert.That(session.TurnsReceived, Is.EqualTo(1));
            Assert.That(speechInput.StartRequests, Is.EqualTo(1));
            Assert.That(microphone.interactable, Is.False, "No recording while the turn is in progress.");
        }

        [UnityTest]
        public IEnumerator Enter_WithATypedDraft_DoesNotRecord_AndTypingStillSends()
        {
            input.text = "Getippte Antwort";
            controller.PressTalkKey();
            yield return null;

            Assert.That(speechInput.StartRequests, Is.EqualTo(0));

            send.onClick.Invoke();
            yield return null;

            Assert.That(session.TurnsReceived, Is.EqualTo(1));
            Assert.That(history.Messages.Last().Text, Is.EqualTo("Getippte Antwort"));
        }

        [UnityTest]
        public IEnumerator Unavailable_DisablesTheButton_ShowsWhy_AndTypingWorks()
        {
            controller.SetSpeechInput(new UnavailableSpeechInput("Spracheingabe nicht eingerichtet (SALESSIM_ELEVENLABS_API_KEY fehlt)."));
            yield return null;

            Assert.That(speech.State, Is.EqualTo(SpeechInputViewState.Unavailable));
            Assert.That(microphone.interactable, Is.False);
            Assert.That(speech.StatusText, Does.Contain("SALESSIM_ELEVENLABS_API_KEY"));

            controller.PressTalkKey();
            yield return null;
            Assert.That(controller.IsSpeechInProgress, Is.False);

            input.text = "Hallo";
            send.onClick.Invoke();
            yield return null;
            Assert.That(session.TurnsReceived, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Escape_WithoutRecording_DoesNothing()
        {
            input.text = "Entwurf";
            controller.PressCancelKey();
            yield return null;

            Assert.That(input.text, Is.EqualTo("Entwurf"));
            Assert.That(speech.State, Is.EqualTo(SpeechInputViewState.Ready));
        }

        [UnityTest]
        public IEnumerator NewRun_WhileRecording_DiscardsIt()
        {
            controller.PressTalkKey();
            yield return null;
            var old = speechInput.Current;

            loop.StartNewRun("Easy", 43);
            yield return null;
            old.Complete(Spoken);
            yield return null;

            Assert.That(old.Disposed, Is.True);
            Assert.That(session.TurnsReceived, Is.EqualTo(0));
            Assert.That(speech.State, Is.EqualTo(SpeechInputViewState.Ready));
        }
    }
}
