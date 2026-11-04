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

        public const string PrototypeKind = "scripted-prototype";

        /// <summary>A visit with both offers (stand and website), like the engine's <c>sales-visit</c>.</summary>
        public const string MultiVisitKind = "scripted-multi-visit";

        /// <summary>A phone call with both offers, like the engine's <c>sales-call</c>.</summary>
        public const string MultiCallKind = "scripted-multi-call";

        /// <summary>The engine's contact types: visit and phone call, both with stand and website.</summary>
        public static IReadOnlyList<ScenarioKind> ContactTypes { get; } = new[]
        {
            new ScenarioKind(MultiVisitKind, "Vor-Ort-Besuch (Aufsteller & Website)"),
            new ScenarioKind(MultiCallKind, "Telefonat (Aufsteller & Website)"),
        };

        /// <param name="kinds">The kinds offered; <c>null</c> keeps the three product kinds the older UI tests use.</param>
        public ScriptedScenarioSource(IReadOnlyList<ScenarioKind> kinds = null)
        {
            if (kinds != null)
            {
                Kinds = kinds;
                SelectedKind = kinds[0].Id;
            }
        }

        private int nextSeed = 1000;

        public IReadOnlyList<string> Difficulties { get; } = new[] { "Easy", "Medium", "Hard" };

        public IReadOnlyList<ScenarioKind> Kinds { get; } = new[]
        {
            new ScenarioKind("scripted", "Website & Co."),
            new ScenarioKind(StandKind, "Bewertungsaufsteller (Vor-Ort-Besuch)"),
            new ScenarioKind(PrototypeKind, "Website-Prototyp (Vor-Ort-Besuch)"),
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
