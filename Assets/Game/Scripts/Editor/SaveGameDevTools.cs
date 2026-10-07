using SalesSim.Infrastructure;
using UnityEditor;
using UnityEngine;

namespace SalesSim.Editor
{
    /// <summary>Development only: inspect and reset the savegame. Not part of the game's UI.</summary>
    public static class SaveGameDevTools
    {
        [MenuItem("Sales Sim/Dev/Spielstand zurücksetzen")]
        public static void ResetSaveGame()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Save] Stop Play mode first: the running game would write its state again.");
                return;
            }

            var store = JsonFileGameStateStore.ForGame(Debug.Log, Debug.LogWarning);
            store.Delete();
            Debug.Log($"[Save] Savegame deleted ({store.FilePath}); the next start is a new game.");
        }

        [MenuItem("Sales Sim/Dev/Spielstand-Ordner öffnen")]
        public static void RevealSaveGame()
        {
            EditorUtility.RevealInFinder(Application.persistentDataPath);
        }
    }
}
