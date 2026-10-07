using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SalesSim.Application;
using SalesSim.Game;
using SalesSim.Infrastructure;

namespace SalesSim.Tests.EditMode
{
    /// <summary>The savegame file and the run registry's save/restore, in a temporary folder per test.</summary>
    public sealed class SaveGameTests
    {
        private string folder;
        private string path;
        private List<string> logs;
        private List<string> warnings;

        [SetUp]
        public void CreateFolder()
        {
            folder = Path.Combine(Path.GetTempPath(), "salessim-save-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            path = Path.Combine(folder, JsonFileGameStateStore.FileName);
            logs = new List<string>();
            warnings = new List<string>();
        }

        [TearDown]
        public void DeleteFolder()
        {
            Directory.Delete(folder, true);
        }

        private JsonFileGameStateStore Store() => new JsonFileGameStateStore(path, logs.Add, warnings.Add);

        [Test]
        public void MissingFile_IsANewGame_WithZeroBalance()
        {
            var data = Store().Load();

            Assert.That(data.balance, Is.EqualTo(0));
            Assert.That(data.playedSeeds, Is.Empty);
            Assert.That(data.hasLastRun, Is.False);
            Assert.That(RunRegistry.Restore(data).Last, Is.Null);
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void SaveAndReload_KeepsBalance_PlayedSeeds_LastRun_AndScores()
        {
            var runs = new RunRegistry();
            runs.Begin(42, "Medium", "hair-salon:42");
            runs.RecordScore(42, 58.4f);
            runs.Begin(7, "Hard", "hair-salon:7");
            var data = new SaveData { balance = 700 };
            runs.WriteTo(data);
            Store().Save(data);

            var loaded = Store().Load();
            var restored = RunRegistry.Restore(loaded);

            Assert.That(loaded.balance, Is.EqualTo(700));
            Assert.That(loaded.version, Is.EqualTo(SaveData.CurrentVersion));
            Assert.That(restored.WasPlayed(42) && restored.WasPlayed(7), Is.True);
            Assert.That(restored.WasPlayed(8), Is.False);
            Assert.That(restored.PlayedSeedCount, Is.EqualTo(2));
            Assert.That(restored.LastScore(42), Is.EqualTo(58.4f));
            Assert.That(restored.LastScore(7), Is.Null);
            Assert.That(restored.Last.Seed, Is.EqualTo(7));
            Assert.That(restored.Last.Difficulty, Is.EqualTo("Hard"));
            Assert.That(restored.Last.ScenarioId, Is.EqualTo("hair-salon:7"));
        }

        [Test]
        public void AfterARestart_APlayedSeedIsStillARerun_AndItsLeadCannotBeSold()
        {
            var first = new RunRegistry();
            first.Begin(42, "Easy", "hair-salon:42");
            var data = new SaveData();
            first.WriteTo(data);
            Store().Save(data);

            var restored = RunRegistry.Restore(Store().Load());
            var replay = restored.Begin(42, "Hard", "hair-salon:42");
            var fresh = restored.Begin(43, "Easy", "hair-salon:43");

            Assert.That(replay.IsRerun, Is.True);
            Assert.That(fresh.IsRerun, Is.False);
            Assert.That(replay.RunId, Is.EqualTo(2), "Run numbers continue.");
            var lead = new LeadSale(GoodLead(), isTraining: replay.IsRerun);
            Assert.That(lead.CanSell, Is.False);
        }

        [Test]
        public void SellingIncreasesTheBalance_AndTheWalletContinuesFromTheSave()
        {
            var wallet = new PlayerWallet();
            new LeadSale(GoodLead()).SellTo(wallet);
            Store().Save(new SaveData { balance = wallet.Balance });

            var restored = new PlayerWallet(Store().Load().balance);

            Assert.That(wallet.Balance, Is.GreaterThan(0));
            Assert.That(restored.Balance, Is.EqualTo(wallet.Balance));
        }

        [TestCase("{ this is not json")]
        [TestCase("")]
        [TestCase("null")]
        [TestCase("[1, 2, 3]")]
        public void DamagedFile_DoesNotCrash_StartsANewGame_AndKeepsTheFile(string content)
        {
            File.WriteAllText(path, content);

            var data = Store().Load();

            Assert.That(data.balance, Is.EqualTo(0));
            Assert.That(data.playedSeeds, Is.Empty);
            Assert.That(warnings.Single(), Does.Contain("unreadable").And.Contain("new game"));
            Assert.That(File.Exists(path), Is.False, "The damaged file is moved aside ...");
            Assert.That(Directory.GetFiles(folder, "savegame.corrupt-*.json"), Has.Length.EqualTo(1), "... and kept.");
        }

        [Test]
        public void DamagedFile_WithAReadableBackup_ContinuesFromTheBackup()
        {
            Store().Save(new SaveData { balance = 300 });
            Store().Save(new SaveData { balance = 500 }); // the 300 state is now the backup
            File.WriteAllText(path, "{ broken");

            var data = Store().Load();

            Assert.That(data.balance, Is.EqualTo(300));
            Assert.That(warnings.Single(), Does.Contain("backup"));
        }

        [Test]
        public void Writing_ReplacesTheFile_AndKeepsThePreviousStateAsBackup()
        {
            Store().Save(new SaveData { balance = 100 });
            Store().Save(new SaveData { balance = 200 });

            Assert.That(Store().Load().balance, Is.EqualTo(200));
            Assert.That(File.ReadAllText(path + ".bak"), Does.Contain("\"balance\": 100"));
            Assert.That(File.Exists(path + ".tmp"), Is.False, "No half-written file is left.");
        }

        [Test]
        public void UnknownFields_AreIgnored()
        {
            File.WriteAllText(path,
                "{ \"version\": 1, \"balance\": 900, \"playedSeeds\": [5], \"futureFeature\": { \"x\": 1 }, \"badges\": [\"a\"] }");

            var data = Store().Load();

            Assert.That(data.balance, Is.EqualTo(900));
            Assert.That(data.playedSeeds, Is.EqualTo(new[] { 5 }));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void NewerFormat_LoadsTheKnownFields_AndKeepsACopyBeforeItIsOverwritten()
        {
            File.WriteAllText(path, "{ \"version\": 99, \"balance\": 250, \"playedSeeds\": [1, 2] }");

            var data = Store().Load();

            Assert.That(data.balance, Is.EqualTo(250));
            Assert.That(data.playedSeeds, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(warnings.Single(), Does.Contain("newer"));
            Assert.That(File.Exists(Path.Combine(folder, "savegame.v99.json")), Is.True);
        }

        [Test]
        public void NegativeBalance_IsTreatedAsZero()
        {
            File.WriteAllText(path, "{ \"version\": 1, \"balance\": -50 }");

            Assert.That(Store().Load().balance, Is.EqualTo(0));
            Assert.That(new PlayerWallet(-50).Balance, Is.EqualTo(0));
        }

        [Test]
        public void Delete_RemovesTheSave_SoTheNextStartIsANewGame()
        {
            Store().Save(new SaveData { balance = 100 });
            Store().Save(new SaveData { balance = 200 });

            Store().Delete();

            Assert.That(Directory.GetFiles(folder), Is.Empty);
            Assert.That(Store().Load().balance, Is.EqualTo(0));
        }

        private static LeadEvaluation GoodLead()
        {
            return LeadEvaluator.Evaluate(
                new SalesSessionState("s", string.Empty, new SalesIndicators(70, 72, 75, 60), "Ended", "Continue", true, "Completed", "Appointment"),
                EconomySettings.Default);
        }
    }
}
