using System.Collections.Generic;

namespace SalesSim.Application
{
    /// <summary>A kind of run the player can choose: an opaque id from the engine plus a German label.</summary>
    public sealed class ScenarioKind
    {
        public ScenarioKind(string id, string label)
        {
            Id = id ?? string.Empty;
            Label = label ?? string.Empty;
        }

        public string Id { get; }

        public string Label { get; }
    }

    /// <summary>
    /// What a run can be started with, as the engine defines it: its difficulty presets, random seeds from its training
    /// pool, and the scenario id for a seed. Unity never generates scenarios or decides what a difficulty means.
    /// </summary>
    public interface IScenarioSource
    {
        /// <summary>The engine's run difficulty names (e.g. "Easy", "Medium", "Hard"), easiest first.</summary>
        IReadOnlyList<string> Difficulties { get; }

        /// <summary>A new random seed, picked the way the engine picks training scenarios. Any seed ≥ 0 is valid.</summary>
        int PickRandomSeed();

        /// <summary>The opaque scenario id that regenerates the origin of <paramref name="seed"/>.</summary>
        string ScenarioIdFor(int seed);

        /// <summary>
        /// What can be sold in a run, as the engine's generation pools define it (e.g. a website by phone or visit, or the review
        /// stand on site). Ids are opaque; <see cref="Select"/> switches the pool <see cref="PickRandomSeed"/> and
        /// <see cref="ScenarioIdFor"/> use. Sources with a single kind return one entry.
        /// </summary>
        IReadOnlyList<ScenarioKind> Kinds { get; }

        /// <summary>The kind currently used for new runs.</summary>
        string SelectedKind { get; }

        /// <summary>Uses <paramref name="kindId"/> (one of <see cref="Kinds"/>) for the next runs.</summary>
        void Select(string kindId);
    }
}
