using System.Collections.Generic;
using SalesSim.Application;

namespace SalesSim.Tests.PlayMode
{
    /// <summary>
    /// <see cref="IScenarioSource"/> for UI tests: fixed difficulty names, "random" seeds counting up from 1000, two kinds
    /// ("scripted" and "scripted-stand"); scenario ids are <c>{kind}:{seed}</c>.
    /// </summary>
    internal sealed class ScriptedScenarioSource : IScenarioSource
    {
        public const string StandKind = "scripted-stand";

        private int nextSeed = 1000;

        public IReadOnlyList<string> Difficulties { get; } = new[] { "Easy", "Medium", "Hard" };

        public IReadOnlyList<ScenarioKind> Kinds { get; } = new[]
        {
            new ScenarioKind("scripted", "Website & Co."),
            new ScenarioKind(StandKind, "Bewertungsaufsteller (Vor-Ort-Besuch)"),
        };

        public string SelectedKind { get; private set; } = "scripted";

        public void Select(string kindId)
        {
            SelectedKind = kindId;
        }

        public int RandomSeedsPicked { get; private set; }

        public int PickRandomSeed()
        {
            RandomSeedsPicked++;
            return nextSeed++;
        }

        public string ScenarioIdFor(int seed) => $"{SelectedKind}:{seed}";
    }
}
