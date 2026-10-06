using SalesSim.Presentation;
using UnityEngine;

namespace SalesSim.Infrastructure
{
    /// <summary>
    /// Composition root of SalesTestScene: chooses the ISalesGameSession implementation and hands it to the presentation.
    /// Swap <see cref="NotConnectedSalesGameSession"/> for the real engine adapter once it exists.
    /// </summary>
    public sealed class SalesTestSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private SalesConversationController conversationController;
        [SerializeField] private string scenarioId = "test-scenario";

        private void Start()
        {
            conversationController.Initialize(new NotConnectedSalesGameSession(), scenarioId);
        }
    }
}
