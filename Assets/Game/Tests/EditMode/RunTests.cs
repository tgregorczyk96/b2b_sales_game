using System;
using NUnit.Framework;
using SalesSim.Application;
using SalesSim.Game;

namespace SalesSim.Tests.EditMode
{
    /// <summary>Run metadata, rerun detection, seed input and the no-payout rule for training runs.</summary>
    public sealed class RunTests
    {
        [Test]
        public void FirstRunOfASeed_IsNotARerun_TheSecondIs()
        {
            var runs = new RunRegistry();

            var first = runs.Begin(42, "Easy", "hair-salon:42");
            var second = runs.Begin(42, "Easy", "hair-salon:42");

            Assert.That(first.IsRerun, Is.False);
            Assert.That(second.IsRerun, Is.True);
            Assert.That(second.RunId, Is.EqualTo(first.RunId + 1));
            Assert.That(runs.Last, Is.SameAs(second));
            Assert.That(runs.WasPlayed(42), Is.True);
            Assert.That(runs.WasPlayed(43), Is.False);
        }

        [Test]
        public void ASeedPlayedAtAnotherDifficulty_IsStillARerun()
        {
            var runs = new RunRegistry();
            runs.Begin(7, "Easy", "hair-salon:7");

            Assert.That(runs.Begin(7, "Hard", "hair-salon:7").IsRerun, Is.True);
            Assert.That(runs.Begin(8, "Hard", "hair-salon:8").IsRerun, Is.False);
        }

        [Test]
        public void LastScore_IsKeptPerSeed()
        {
            var runs = new RunRegistry();
            Assert.That(runs.LastScore(1), Is.Null);

            runs.RecordScore(1, 58f);
            runs.RecordScore(2, 30f);
            runs.RecordScore(1, 71f);

            Assert.That(runs.LastScore(1), Is.EqualTo(71f));
            Assert.That(runs.LastScore(2), Is.EqualTo(30f));
        }

        [TestCase("", null)]
        [TestCase("   ", null)]
        [TestCase("0", 0)]
        [TestCase(" 1406361028 ", 1406361028)]
        [TestCase("2147483647", int.MaxValue)]
        public void TryParseSeed_AcceptsEmptyAndNonNegativeNumbers(string text, int? expected)
        {
            Assert.That(RunInfo.TryParseSeed(text, out var seed), Is.True);
            Assert.That(seed, Is.EqualTo(expected));
        }

        [TestCase("-1")]
        [TestCase("+5")]
        [TestCase("12a")]
        [TestCase("1.5")]
        [TestCase("2147483648")]
        public void TryParseSeed_RejectsEverythingElse(string text)
        {
            Assert.That(RunInfo.TryParseSeed(text, out _), Is.False);
        }

        [Test]
        public void NegativeSeed_IsNoRun()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RunInfo(1, -1, "Easy", "x", false));
        }

        [Test]
        public void TrainingLead_IsEvaluatedAsUsual_ButCannotBeSold_AndPaysNothing()
        {
            var evaluation = LeadEvaluator.Evaluate(
                new SalesSessionState("s", string.Empty, new SalesIndicators(70, 72, 75, 60), "Ended", "Continue", true, "Completed", "Appointment"),
                EconomySettings.Default);
            var wallet = new PlayerWallet();
            var training = new LeadSale(evaluation, isTraining: true);

            Assert.That(evaluation.IsSellable, Is.True);
            Assert.That(training.IsTraining, Is.True);
            Assert.That(training.CanSell, Is.False);
            Assert.Throws<InvalidOperationException>(() => training.SellTo(wallet));
            Assert.That(wallet.Balance, Is.EqualTo(0));

            var firstRun = new LeadSale(evaluation);
            Assert.That(firstRun.CanSell, Is.True);
            Assert.That(firstRun.SellTo(wallet), Is.EqualTo(evaluation.Payout));
            Assert.That(wallet.Balance, Is.EqualTo(evaluation.Payout));
        }
    }
}
