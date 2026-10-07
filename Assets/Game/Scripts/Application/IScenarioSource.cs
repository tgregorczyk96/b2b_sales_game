using System.Collections.Generic;

namespace SalesSim.Application
{
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
    }
}
