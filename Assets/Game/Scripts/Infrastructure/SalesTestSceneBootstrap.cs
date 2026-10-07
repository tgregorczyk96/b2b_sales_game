using System;
using System.IO;
using SalesSim.Application;
using SalesSim.EngineAdapter;
using SalesSim.Presentation;
using UnityEngine;

namespace SalesSim.Infrastructure
{
    /// <summary>
    /// Composition root of SalesTestScene: chooses the ISalesGameSession and IScenarioSource implementations and hands
    /// them to the run loop, which starts with the run setup. Uses the real Sales Engine; its customer is played by
    /// Anthropic when SALESSIM_ANTHROPIC_API_KEY is set (environment or the project's git-ignored .env), otherwise by the
    /// engine's placeholder. Falls back to <see cref="NotConnectedSalesGameSession"/> (with an error in the console) when
    /// the engine cannot be set up.
    /// </summary>
    public sealed class SalesTestSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private SalesRunLoop runLoop;
        [Tooltip("The engine's generation pool the seeds are played from.")]
        [SerializeField] private string generationPool = EngineScenarioSource.DefaultPool;
        [Tooltip("The player's own name; the engine uses it e.g. for the introduction in example sentences.")]
        [SerializeField] private string playerName = "Tomasz";
        [Tooltip("The company/brand the player sells for.")]
        [SerializeField] private string playerCompany = "tom-gre-it";

        private void Start()
        {
            var (session, scenarios) = CreateEngine();
            runLoop.Configure(session, scenarios, new PlayerProfile(playerName, playerCompany));
        }

        private (ISalesGameSession, IScenarioSource) CreateEngine()
        {
            try
            {
                var contentDirectory = Path.Combine(UnityEngine.Application.streamingAssetsPath, "SalesEngine", "content");
                var projectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath);
                var settings = LocalSettings.Load(Path.Combine(projectRoot, ".env"));
                var session = SalesEngineGameSession.Create(contentDirectory, settings.Get, message => Debug.Log(message));
                var scenarios = new EngineScenarioSource(contentDirectory, generationPool);
                // The seed window takes seconds to build; do it while the player looks at the setup.
                System.Threading.Tasks.Task.Run(scenarios.Prepare);
                return (session, scenarios);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Sales Engine could not be set up, using the offline session instead: {exception.Message}", this);
                return (new NotConnectedSalesGameSession(), new OfflineScenarioSource());
            }
        }
    }
}
