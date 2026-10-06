using System;
using System.IO;
using SalesSim.Application;
using SalesSim.EngineAdapter;
using SalesSim.Presentation;
using UnityEngine;

namespace SalesSim.Infrastructure
{
    /// <summary>
    /// Composition root of SalesTestScene: chooses the ISalesGameSession implementation and hands it to the presentation.
    /// Uses the real Sales Engine, for now without an LLM (the engine's placeholder customer answers); falls back to
    /// <see cref="NotConnectedSalesGameSession"/> (with an error in the console) when the engine cannot be set up.
    /// </summary>
    public sealed class SalesTestSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private SalesConversationController conversationController;
        [Tooltip("Origin scenario as '<generation pool>:<seed>'; interpreted by the Sales Engine.")]
        [SerializeField] private string scenarioId = "hair-salon:1406361028";

        private void Start()
        {
            conversationController.Initialize(CreateSession(), scenarioId);
        }

        private ISalesGameSession CreateSession()
        {
            try
            {
                var contentDirectory = Path.Combine(UnityEngine.Application.streamingAssetsPath, "SalesEngine", "content");
                return SalesEngineGameSession.CreateWithoutLlm(contentDirectory);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Sales Engine could not be set up, using the offline session instead: {exception.Message}", this);
                return new NotConnectedSalesGameSession();
            }
        }
    }
}
