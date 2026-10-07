using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SalesEngine.Customers;
using SalesEngine.Difficulty;
using SalesEngine.Training;
using SalesSim.Application;
using SalesSim.EngineAdapter;
using GuidanceAvailability = SalesSim.Application.GuidanceAvailability;

namespace SalesSim.Tests.EditMode
{
    /// <summary>
    /// The Unity adapter against the real engine with a scripted HTTP stack: no network, no API costs.
    /// Customer and interpreter call the API one after the other, so requests are answered in order.
    /// </summary>
    public sealed class SalesEngineGameSessionTests
    {
        private const string ScenarioId = "hair-salon:1406361028";
        private const string FakeKey = "sk-ant-test-NOT-REAL-abcdefghijklmnop";
        private const string LlmUtterance = "Wir haben ein Google-Profil, aber ehrlich gesagt kümmere ich mich da kaum drum.";

        private static readonly string[] PlaceholderLines =
        {
            "Ja, worum geht es denn?", "Mhm.", "Und weiter?", "Okay ...", "Tut mir leid, ich muss jetzt weitermachen. Auf Wiederhören.",
        };

        // Shape and content taken from the engine's own AI tests (CustomerTestData.ValidAnswer).
        private const string ValidCustomerAnswer =
            "{\"utterance\":\"" + LlmUtterance + "\"," +
            "\"reaction\":{\"engagement\":\"Engaged\",\"trustSignal\":\"Neutral\",\"interestSignal\":\"Positive\"," +
            "\"patienceSignal\":\"Stable\",\"conversationIntent\":\"Continue\"}," +
            "\"perspective\":{\"awareness\":[],\"perceivedPriority\":[],\"satisfaction\":[]}," +
            "\"disclosure\":[],\"salespersonReferences\":[]}";

        private static string ContentDirectory =>
            Path.Combine(UnityEngine.Application.streamingAssetsPath, "SalesEngine", "content");

        [Test]
        public async Task SuccessfulApiResponse_IsUsedInState_WithoutFallback()
        {
            var http = new ScriptedHandler(Message(ValidCustomerAnswer), Status(HttpStatusCode.InternalServerError));
            var logs = new List<string>();
            var session = Create(http, logs);

            await session.StartSessionAsync(new SessionStartRequest(ScenarioId));
            var state = await session.SendPlayerTurnAsync(new PlayerTurn("Guten Tag, haben Sie kurz Zeit?"));

            Assert.That(state.CustomerMessage, Is.EqualTo(LlmUtterance));
            Assert.That(state.Intent, Is.EqualTo("Continue"));
            Assert.That(session.CurrentState, Is.SameAs(state));
            Assert.That(session.UsesFallbackCustomer, Is.False);
            Assert.That(logs, Does.Contain("[SalesClient] Status: 200"));
            Assert.That(logs, Does.Contain("[SalesClient] Response received: yes"));
            Assert.That(logs, Does.Contain("[SalesClient] Deserialization successful: yes"));
            Assert.That(logs, Does.Contain("[SalesClient] Fallback used: no"));
        }

        [Test]
        public async Task RealAnthropicClient_IsCalled_InsteadOfThePlaceholderCustomer()
        {
            var http = new ScriptedHandler(Message(ValidCustomerAnswer), Status(HttpStatusCode.InternalServerError));
            var logs = new List<string>();
            var session = Create(http, logs);

            await session.StartSessionAsync(new SessionStartRequest(ScenarioId));
            var state = await session.SendPlayerTurnAsync(new PlayerTurn("Guten Tag, haben Sie kurz Zeit?"));

            var first = http.Requests[0];
            Assert.That(first.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(first.RequestUri, Is.EqualTo(new Uri("https://api.anthropic.com/v1/messages")));
            Assert.That(first.Headers.GetValues("x-api-key").Single(), Is.EqualTo(FakeKey));
            Assert.That(logs, Does.Contain("[SalesClient] Request: POST https://api.anthropic.com/v1/messages (customer_simulation_response)"));
            Assert.That(PlaceholderLines, Does.Not.Contain(state.CustomerMessage));
        }

        [Test]
        public async Task HttpError_FailsTheTurn_WithoutFallbackText_AndKeepsTheState()
        {
            var http = new ScriptedHandler(Status(HttpStatusCode.InternalServerError));
            var logs = new List<string>();
            var session = Create(http, logs);
            var started = await session.StartSessionAsync(new SessionStartRequest(ScenarioId));

            Assert.ThrowsAsync<CustomerSimulationFailedException>(() => session.SendPlayerTurnAsync(new PlayerTurn("Hallo")));

            Assert.That(session.CurrentState, Is.SameAs(started));
            Assert.That(logs, Does.Contain("[SalesClient] Status: 500"));
            Assert.That(logs, Does.Contain("[SalesClient] Fallback used: no"));
            Assert.That(logs.Any(line => line.StartsWith("[SalesClient] Turn failed: LlmProviderException", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public async Task UnreadableResponse_FailsTheTurn_WithFailedDeserialization()
        {
            var http = new ScriptedHandler(Raw(HttpStatusCode.OK, "<html>gateway</html>"));
            var logs = new List<string>();
            var session = Create(http, logs);
            var started = await session.StartSessionAsync(new SessionStartRequest(ScenarioId));

            Assert.ThrowsAsync<CustomerSimulationFailedException>(() => session.SendPlayerTurnAsync(new PlayerTurn("Hallo")));

            Assert.That(session.CurrentState, Is.SameAs(started));
            Assert.That(logs, Does.Contain("[SalesClient] Deserialization successful: no (InvalidResponse)"));
            Assert.That(logs, Does.Contain("[SalesClient] Fallback used: no"));
        }

        [Test]
        public async Task AnswerNotMatchingTheCustomerSchema_FailsTheTurn_WithFailedDeserialization()
        {
            var http = new ScriptedHandler(Message("{\"utterance\":\"Hallo\"}"));
            var logs = new List<string>();
            var session = Create(http, logs);
            var started = await session.StartSessionAsync(new SessionStartRequest(ScenarioId));

            Assert.ThrowsAsync<CustomerSimulationFailedException>(() => session.SendPlayerTurnAsync(new PlayerTurn("Hallo")));

            Assert.That(session.CurrentState, Is.SameAs(started));
            Assert.That(logs, Does.Contain("[SalesClient] Deserialization successful: no (customer answer does not match the schema)"));
        }

        [Test]
        public async Task WithoutApiKey_ThePlaceholderCustomerIsUsed_AndTheReasonIsLogged()
        {
            var http = new ScriptedHandler();
            var logs = new List<string>();
            var session = SalesEngineGameSession.Create(ContentDirectory, _ => null, logs.Add, http);

            await session.StartSessionAsync(new SessionStartRequest(ScenarioId));
            var state = await session.SendPlayerTurnAsync(new PlayerTurn("Guten Tag"));

            Assert.That(session.UsesFallbackCustomer, Is.True);
            Assert.That(http.Requests, Is.Empty);
            Assert.That(PlaceholderLines, Does.Contain(state.CustomerMessage));
            Assert.That(logs, Does.Contain("[SalesClient] Fallback used: yes"));
            Assert.That(logs, Does.Contain("[SalesClient] Fallback reason: SALESSIM_ANTHROPIC_API_KEY is not set (environment or .env)"));
        }

        [Test]
        public async Task Logs_NeverContainTheApiKey()
        {
            var http = new ScriptedHandler(Raw(HttpStatusCode.Unauthorized,
                "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key " + FakeKey + "\"}}"));
            var logs = new List<string>();
            var session = Create(http, logs);
            await session.StartSessionAsync(new SessionStartRequest(ScenarioId));

            Assert.ThrowsAsync<CustomerSimulationFailedException>(() => session.SendPlayerTurnAsync(new PlayerTurn("Hallo")));

            Assert.That(logs, Is.Not.Empty);
            Assert.That(logs.Any(line => line.Contains(FakeKey)), Is.False);
        }

        [Test]
        public async Task HintAndHintMore_ComeFromTheEngineRules_WithoutAnyApiCall()
        {
            var http = new ScriptedHandler();
            var logs = new List<string>();
            var session = Create(http, logs);
            var started = await session.StartSessionAsync(new SessionStartRequest(ScenarioId));

            var hint = await session.RequestGuidanceAsync(GuidanceLevel.Hint);
            var more = await session.RequestGuidanceAsync(GuidanceLevel.HintMore);

            Assert.That(http.Requests, Is.Empty, "Hint and Hint More are deterministic, no AI.");
            Assert.That(hint.Level, Is.EqualTo(GuidanceLevel.Hint));
            Assert.That(more.Level, Is.EqualTo(GuidanceLevel.HintMore));
            Assert.That(hint.Text, Is.Not.Empty);
            Assert.That(more.Text, Is.Not.Empty);
            Assert.That(more.Text, Is.Not.EqualTo(hint.Text), "Hint More is the more concrete level.");
            Assert.That(hint.Focus, Is.EqualTo("OpenConversation"), "Opening stage.");
            Assert.That(more.Focus, Is.EqualTo(hint.Focus), "Both point in the same direction.");
            Assert.That(logs, Does.Contain("[SalesClient] Guidance Orientation: focus OpenConversation, for turn 1"));
            Assert.That(logs, Does.Contain("[SalesClient] Guidance Specific: focus OpenConversation, for turn 1"));
            Assert.That(session.CurrentState, Is.SameAs(started), "Guidance changes no conversation state.");
        }

        [Test]
        public async Task Hint_IsDeterministic_AndRepeatable()
        {
            var session = Create(new ScriptedHandler(), new List<string>());
            await session.StartSessionAsync(new SessionStartRequest(ScenarioId));

            var first = await session.RequestGuidanceAsync(GuidanceLevel.Hint);
            var second = await session.RequestGuidanceAsync(GuidanceLevel.Hint);

            Assert.That(second.Text, Is.EqualTo(first.Text));
        }

        [Test]
        public async Task Example_UsesTheAiPath_AndReturnsTheFullSentence()
        {
            const string sentence = "Guten Tag, Max Berger von Webwerk – haben Sie zwei Minuten, damit ich kurz sage, worum es geht?";
            var http = new ScriptedHandler(Message("{\"sentence\":" + JsonString(sentence) + "}"));
            var logs = new List<string>();
            var session = Create(http, logs);
            var started = await session.StartSessionAsync(new SessionStartRequest(ScenarioId));

            var example = await session.RequestGuidanceAsync(GuidanceLevel.Example);

            Assert.That(example.Level, Is.EqualTo(GuidanceLevel.Example));
            Assert.That(example.Text, Is.EqualTo(sentence));
            Assert.That(http.Requests.Count, Is.EqualTo(1), "Exactly one AI call.");
            Assert.That(http.Requests[0].RequestUri, Is.EqualTo(new Uri("https://api.anthropic.com/v1/messages")));
            Assert.That(logs, Does.Contain("[SalesClient] Request: POST https://api.anthropic.com/v1/messages (example_sentence)"));
            Assert.That(logs, Does.Contain("[SalesClient] Guidance FullExampleSentence: focus OpenConversation, for turn 1"));
            Assert.That(session.CurrentState, Is.SameAs(started));
        }

        [Test]
        public async Task FailedExample_IsReported_AndTheConversationGoesOnNormally()
        {
            var http = new ScriptedHandler(Status(HttpStatusCode.InternalServerError), Message(ValidCustomerAnswer), Status(HttpStatusCode.InternalServerError));
            var logs = new List<string>();
            var session = Create(http, logs);
            var started = await session.StartSessionAsync(new SessionStartRequest(ScenarioId));

            var failure = Assert.ThrowsAsync<GuidanceUnavailableException>(() => session.RequestGuidanceAsync(GuidanceLevel.Example));

            Assert.That(failure.Reason, Is.EqualTo(GuidanceUnavailableReason.GenerationFailed));
            Assert.That(session.CurrentState, Is.SameAs(started));
            Assert.That(logs.Any(line => line.StartsWith("[SalesClient] Guidance FullExampleSentence failed:", StringComparison.Ordinal)), Is.True);

            var hint = await session.RequestGuidanceAsync(GuidanceLevel.Hint);
            Assert.That(hint.Text, Is.Not.Empty);
            var state = await session.SendPlayerTurnAsync(new PlayerTurn("Guten Tag, haben Sie kurz Zeit?"));
            Assert.That(state.CustomerMessage, Is.EqualTo(LlmUtterance));
        }

        [Test]
        public async Task WithoutApiKey_HintsWork_ButTheExampleNeedsTheAi()
        {
            var http = new ScriptedHandler();
            var session = SalesEngineGameSession.Create(ContentDirectory, _ => null, null, http);
            await session.StartSessionAsync(new SessionStartRequest(ScenarioId));

            var hint = await session.RequestGuidanceAsync(GuidanceLevel.Hint);
            var failure = Assert.ThrowsAsync<GuidanceUnavailableException>(() => session.RequestGuidanceAsync(GuidanceLevel.Example));

            Assert.That(hint.Text, Is.Not.Empty);
            Assert.That(failure.Reason, Is.EqualTo(GuidanceUnavailableReason.RequiresAi));
            Assert.That(http.Requests, Is.Empty);
        }

        [Test]
        public async Task AfterTheConversationEnded_GuidanceIsNotAvailable()
        {
            var session = Create(new ScriptedHandler(), new List<string>());
            await session.StartSessionAsync(new SessionStartRequest(ScenarioId));
            await session.EndSessionAsync();

            var failure = Assert.ThrowsAsync<GuidanceUnavailableException>(() => session.RequestGuidanceAsync(GuidanceLevel.Hint));

            Assert.That(failure.Reason, Is.EqualTo(GuidanceUnavailableReason.ConversationOver));
        }

        // --- Player profile in example sentences ---------------------------------------------------

        [Test]
        public async Task Example_GetsThePlayersNameAndCompany_FromTheStartRequest()
        {
            const string sentence = "Guten Tag, Frau Wolff, hier ist Tomasz von tom-gre-it – passt es kurz für zwei Minuten?";
            var http = new ScriptedHandler(Message("{\"sentence\":" + JsonString(sentence) + "}"));
            var session = Create(http, new List<string>());
            await session.StartSessionAsync(new SessionStartRequest(ScenarioId, new PlayerProfile("Tomasz", "tom-gre-it")));

            var example = await session.RequestGuidanceAsync(GuidanceLevel.Example);

            Assert.That(example.Text, Is.EqualTo(sentence), "The engine's sentence is shown as is; Unity replaces nothing.");
            Assert.That(example.Text, Does.Not.Contain("[Name]").And.Not.Contain("[Firma]"));
            var body = Unescape(http.Bodies.Single());
            Assert.That(body, Does.Contain("\"name\": \"Tomasz\""));
            Assert.That(body, Does.Contain("\"company\": \"tom-gre-it\""));
            Assert.That(body, Does.Not.Contain("write the placeholders"));
        }

        [Test]
        public async Task Example_WithoutPlayerProfile_SendsNone_AndARawPlaceholderNeverReachesThePlayer()
        {
            var http = new ScriptedHandler(Message("{\"sentence\":\"Guten Tag, hier ist [Name] von [Firma].\"}"));
            var session = Create(http, new List<string>());
            await session.StartSessionAsync(new SessionStartRequest(ScenarioId));

            var failure = Assert.ThrowsAsync<GuidanceUnavailableException>(() => session.RequestGuidanceAsync(GuidanceLevel.Example));

            Assert.That(failure.Reason, Is.EqualTo(GuidanceUnavailableReason.GenerationFailed), "Rejected by the engine, not shown.");
            var body = Unescape(http.Bodies.Single());
            Assert.That(body, Does.Contain("\"salesperson\": null"));
            Assert.That(body, Does.Contain("Never write placeholders such as [Name] or [Firma]"));
        }

        [Test]
        public async Task IncompletePlayerProfile_FallsBackToNone()
        {
            var http = new ScriptedHandler(Message("{\"sentence\":\"Guten Tag, ich melde mich wegen Ihrer Website.\"}"));
            var session = Create(http, new List<string>());
            await session.StartSessionAsync(new SessionStartRequest(ScenarioId, new PlayerProfile("Tomasz", " ")));

            var example = await session.RequestGuidanceAsync(GuidanceLevel.Example);

            Assert.That(example.Text, Is.EqualTo("Guten Tag, ich melde mich wegen Ihrer Website."));
            Assert.That(Unescape(http.Bodies.Single()), Does.Contain("\"salesperson\": null"));
        }

        // --- Guidance availability: the engine's difficulty rules -----------------------------------

        private static IEnumerable<DifficultyProfile> Difficulties()
        {
            yield return DifficultyProfile.Easy;
            yield return DifficultyProfile.Medium;
            yield return DifficultyProfile.Hard;
        }

        [TestCaseSource(nameof(Difficulties))]
        public async Task Availability_IsTheEnginesDifficultyRule(DifficultyProfile difficulty)
        {
            var session = new SalesEngineGameSession(ContentDirectory, () => new SalesEngine.SalesSimulationEngine(), difficulty, new NoExampleGenerator());
            Assert.That(session.GetGuidanceAvailability(GuidanceLevel.Hint), Is.EqualTo(GuidanceAvailability.NotStarted));
            await session.StartSessionAsync(new SessionStartRequest(ScenarioId));

            foreach (var (level, type) in LevelTypes)
            {
                var allowed = GuidancePolicy.Allows(difficulty, type);
                Assert.That(session.GetGuidanceAvailability(level),
                    Is.EqualTo(allowed ? GuidanceAvailability.Available : GuidanceAvailability.NotAllowedByDifficulty), $"{difficulty.Name} {level}");
            }
        }

        [TestCaseSource(nameof(Difficulties))]
        public async Task DisallowedLevel_IsRefused_AllowedLevels_Work(DifficultyProfile difficulty)
        {
            var session = new SalesEngineGameSession(ContentDirectory, () => new SalesEngine.SalesSimulationEngine(), difficulty, new NoExampleGenerator());
            await session.StartSessionAsync(new SessionStartRequest(ScenarioId));

            foreach (var (level, type) in LevelTypes.Where(entry => entry.Level != GuidanceLevel.Example))
            {
                if (GuidancePolicy.Allows(difficulty, type))
                {
                    Assert.That((await session.RequestGuidanceAsync(level)).Text, Is.Not.Empty, $"{difficulty.Name} {level}");
                }
                else
                {
                    var failure = Assert.ThrowsAsync<GuidanceUnavailableException>(() => session.RequestGuidanceAsync(level));
                    Assert.That(failure.Reason, Is.EqualTo(GuidanceUnavailableReason.NotAllowedByDifficulty), $"{difficulty.Name} {level}");
                }
            }
        }

        [TestCaseSource(nameof(Difficulties))]
        public async Task WithoutAi_TheExampleNeedsTheAi_OnlyWhereTheDifficultyAllowsIt(DifficultyProfile difficulty)
        {
            var session = new SalesEngineGameSession(ContentDirectory, () => new SalesEngine.SalesSimulationEngine(), difficulty);
            await session.StartSessionAsync(new SessionStartRequest(ScenarioId));

            var expected = GuidancePolicy.Allows(difficulty, GuidanceType.FullExampleSentence)
                ? GuidanceAvailability.RequiresAi
                : GuidanceAvailability.NotAllowedByDifficulty;
            Assert.That(session.GetGuidanceAvailability(GuidanceLevel.Example), Is.EqualTo(expected));
        }

        [Test]
        public async Task AfterTheEnd_NoLevelIsAvailable()
        {
            var session = Create(new ScriptedHandler(), new List<string>());
            await session.StartSessionAsync(new SessionStartRequest(ScenarioId));
            await session.EndSessionAsync();

            foreach (var (level, _) in LevelTypes)
            {
                Assert.That(session.GetGuidanceAvailability(level), Is.EqualTo(GuidanceAvailability.ConversationOver));
            }
        }

        private static readonly (GuidanceLevel Level, GuidanceType Type)[] LevelTypes =
        {
            (GuidanceLevel.Hint, GuidanceType.Orientation),
            (GuidanceLevel.HintMore, GuidanceType.Specific),
            (GuidanceLevel.Example, GuidanceType.FullExampleSentence),
        };

        /// <summary>Request bodies are JSON with the prompt inside a string: undo the escaping for readable asserts.</summary>
        private static string Unescape(string body)
        {
            return System.Text.RegularExpressions.Regex.Unescape(body);
        }

        private sealed class NoExampleGenerator : IExampleSentenceGenerator
        {
            public Task<string> GenerateAsync(ExampleSentenceRequest request, CancellationToken cancellationToken)
            {
                throw new AssertionException("Not expected in this test.");
            }
        }

        private static SalesEngineGameSession Create(ScriptedHandler http, List<string> logs)
        {
            return SalesEngineGameSession.Create(
                ContentDirectory, name => name == "SALESSIM_ANTHROPIC_API_KEY" ? FakeKey : null, logs.Add, http);
        }

        private static Func<HttpResponseMessage> Message(string text)
        {
            var body = "{\"id\":\"msg_01\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-sonnet-5-5\"," +
                       "\"content\":[{\"type\":\"text\",\"text\":" + JsonString(text) + "}]," +
                       "\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":10,\"output_tokens\":10}}";
            return Raw(HttpStatusCode.OK, body);
        }

        private static Func<HttpResponseMessage> Status(HttpStatusCode status)
        {
            return Raw(status, "{\"type\":\"error\",\"error\":{\"type\":\"api_error\",\"message\":\"scripted failure\"}}");
        }

        private static Func<HttpResponseMessage> Raw(HttpStatusCode status, string body)
        {
            return () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        private static string JsonString(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        /// <summary>Answers requests in order with the scripted responses; any further request gets HTTP 500.</summary>
        private sealed class ScriptedHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpResponseMessage>> responses;

            public ScriptedHandler(params Func<HttpResponseMessage>[] responses)
            {
                this.responses = new Queue<Func<HttpResponseMessage>>(responses);
            }

            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

            /// <summary>Request bodies, read while the request is alive.</summary>
            public List<string> Bodies { get; } = new List<string>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                lock (Requests)
                {
                    Requests.Add(request);
                    Bodies.Add(request.Content == null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    var next = responses.Count > 0 ? responses.Dequeue() : Status(HttpStatusCode.InternalServerError);
                    return Task.FromResult(next());
                }
            }
        }
    }
}
