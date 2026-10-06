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
using SalesSim.Application;
using SalesSim.EngineAdapter;

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

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                lock (Requests)
                {
                    Requests.Add(request);
                    var next = responses.Count > 0 ? responses.Dequeue() : Status(HttpStatusCode.InternalServerError);
                    return Task.FromResult(next());
                }
            }
        }
    }
}
