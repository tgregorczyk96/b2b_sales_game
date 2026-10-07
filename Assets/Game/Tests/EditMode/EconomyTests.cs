using System;
using NUnit.Framework;
using SalesSim.Application;
using SalesSim.Game;

namespace SalesSim.Tests.EditMode
{
    public sealed class EconomyTests
    {
        private static readonly EconomySettings Settings = EconomySettings.Default;

        private static SalesSessionState Final(
            float trust, float openness, float engagement, float patience, string endReason = "Completed", string commitment = "")
        {
            return new SalesSessionState("s", string.Empty, new SalesIndicators(trust, openness, engagement, patience),
                "Ended", "Continue", true, endReason, commitment);
        }

        // --- Lead quality and value -------------------------------------------------------

        [Test]
        public void PoorLead_IsWorthNothing()
        {
            var lead = LeadEvaluator.Evaluate(Final(20, 30, 10, 20), Settings);

            Assert.That(lead.Quality, Is.EqualTo(LeadQuality.Lost));
            Assert.That(lead.Value, Is.EqualTo(0));
            Assert.That(lead.IsSellable, Is.False);
        }

        [Test]
        public void UntouchedStartValues_AreNotYetALead()
        {
            // Engine start values on Easy: ending right away must not pay.
            var lead = LeadEvaluator.Evaluate(Final(40, 50, 25, 70, "NotStated"), Settings);

            Assert.That(lead.Quality, Is.EqualTo(LeadQuality.Lost));
        }

        [Test]
        public void GoodLead_IsWorthMoreThanAPoorOne()
        {
            var poor = LeadEvaluator.Evaluate(Final(45, 50, 40, 60), Settings);
            var good = LeadEvaluator.Evaluate(Final(80, 75, 80, 70), Settings);

            Assert.That(good.Quality, Is.EqualTo(LeadQuality.Hot));
            Assert.That(good.Value, Is.GreaterThan(poor.Value));
        }

        [TestCase(LeadQuality.Cold, 45f)]
        [TestCase(LeadQuality.Warm, 55f)]
        [TestCase(LeadQuality.Hot, 70f)]
        public void ScoreThresholds_AreInclusive(LeadQuality expected, float score)
        {
            Assert.That(LeadEvaluator.Evaluate(Final(score, score, score, score), Settings).Quality, Is.EqualTo(expected));
        }

        [TestCase("Information", LeadQuality.Warm)]
        [TestCase("FollowUp", LeadQuality.Hot)]
        [TestCase("Appointment", LeadQuality.Qualified)]
        [TestCase("Agreement", LeadQuality.Qualified)]
        public void AgreedNextStep_RaisesTheLeadToAFloor(string commitment, LeadQuality floor)
        {
            var lead = LeadEvaluator.Evaluate(Final(30, 30, 30, 30, "Completed", commitment), Settings);

            Assert.That(lead.Quality, Is.EqualTo(floor));
        }

        [Test]
        public void NoFurtherAction_IsNoNextStep()
        {
            var lead = LeadEvaluator.Evaluate(Final(30, 30, 30, 30, "Completed", "NoFurtherAction"), Settings);

            Assert.That(lead.Quality, Is.EqualTo(LeadQuality.Lost));
        }

        [Test]
        public void NoContactRequested_IsAlwaysLost()
        {
            var lead = LeadEvaluator.Evaluate(Final(90, 90, 90, 90, "NoContactRequested", "Appointment"), Settings);

            Assert.That(lead.Quality, Is.EqualTo(LeadQuality.Lost));
            Assert.That(lead.Value, Is.EqualTo(0));
        }

        [Test]
        public void Rejection_WithoutNextStep_IsCappedAtCold()
        {
            var lead = LeadEvaluator.Evaluate(Final(90, 90, 90, 90, "Rejected"), Settings);

            Assert.That(lead.Quality, Is.EqualTo(LeadQuality.Cold));
        }

        [Test]
        public void BetterIndicators_NeverGiveAWorseResult()
        {
            var steps = new[] { 0f, 10f, 25f, 40f, 45f, 50f, 55f, 60f, 70f, 85f, 100f };
            foreach (var reason in new[] { "Completed", "Rejected", "Interrupted", "NotStated" })
            {
                foreach (var commitment in new[] { "", "Information", "FollowUp", "Appointment" })
                {
                    foreach (var baseline in steps)
                    {
                        var before = LeadEvaluator.Evaluate(Final(baseline, baseline, baseline, baseline, reason, commitment), Settings);
                        for (var indicator = 0; indicator < 4; indicator++)
                        {
                            foreach (var higher in steps)
                            {
                                if (higher < baseline)
                                {
                                    continue;
                                }

                                var v = new[] { baseline, baseline, baseline, baseline };
                                v[indicator] = higher;
                                var after = LeadEvaluator.Evaluate(Final(v[0], v[1], v[2], v[3], reason, commitment), Settings);
                                Assert.That(after.Quality, Is.GreaterThanOrEqualTo(before.Quality),
                                    $"{reason}/{commitment}: indicator {indicator} {baseline}→{higher}");
                                Assert.That(after.Value, Is.GreaterThanOrEqualTo(before.Value));
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void Evaluation_IsDeterministic()
        {
            var state = Final(62, 58, 66, 50, "Interrupted", "FollowUp");

            var first = LeadEvaluator.Evaluate(state, Settings);
            var second = LeadEvaluator.Evaluate(state, Settings);

            Assert.That(second.Quality, Is.EqualTo(first.Quality));
            Assert.That(second.Value, Is.EqualTo(first.Value));
            Assert.That(second.Score, Is.EqualTo(first.Score));
        }

        // --- Closer and wallet ------------------------------------------------------------

        [Test]
        public void CloserShare_IsDeductedFromTheValue()
        {
            var lead = LeadEvaluator.Evaluate(Final(30, 30, 30, 30, "Completed", "Appointment"), Settings);

            Assert.That(lead.Value, Is.EqualTo(1000));
            Assert.That(lead.CloserCut, Is.EqualTo(300));
            Assert.That(lead.Payout, Is.EqualTo(700));
        }

        [Test]
        public void CloserShare_IsConfigurable()
        {
            var settings = new EconomySettings { closerShare = 0.5f, baseLeadValue = 800 };

            var lead = LeadEvaluator.Evaluate(Final(80, 80, 80, 80), settings);

            Assert.That(lead.Value, Is.EqualTo(480)); // Hot: 800 × 0.6
            Assert.That(lead.CloserCut, Is.EqualTo(240));
            Assert.That(lead.Payout, Is.EqualTo(240));
        }

        [Test]
        public void SellingALead_CreditsThePayout()
        {
            var wallet = new PlayerWallet();
            wallet.Credit(150);
            var sale = new LeadSale(LeadEvaluator.Evaluate(Final(80, 80, 80, 80), Settings));

            var paid = sale.SellTo(wallet);

            Assert.That(paid, Is.EqualTo(420)); // Hot: 600 − 30 %
            Assert.That(wallet.Balance, Is.EqualTo(570));
            Assert.That(sale.IsSold, Is.True);
            Assert.That(sale.CanSell, Is.False);
        }

        [Test]
        public void ALead_CannotBeSoldTwice()
        {
            var wallet = new PlayerWallet();
            var sale = new LeadSale(LeadEvaluator.Evaluate(Final(80, 80, 80, 80), Settings));
            sale.SellTo(wallet);

            Assert.Throws<InvalidOperationException>(() => sale.SellTo(wallet));
            Assert.That(wallet.Balance, Is.EqualTo(420));
        }

        [Test]
        public void AWorthlessLead_CannotBeSold()
        {
            var wallet = new PlayerWallet();
            var sale = new LeadSale(LeadEvaluator.Evaluate(Final(10, 10, 10, 10), Settings));

            Assert.That(sale.CanSell, Is.False);
            Assert.Throws<InvalidOperationException>(() => sale.SellTo(wallet));
            Assert.That(wallet.Balance, Is.EqualTo(0));
        }

        [Test]
        public void Wallet_RejectsNegativeCredits()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerWallet().Credit(-1));
        }

        // --- Character mood heuristic ------------------------------------------------------

        [TestCase(5f, 5f, 0f, 0f, CustomerMood.VeryPositive)]
        [TestCase(2f, 1f, 0f, 0f, CustomerMood.Positive)]
        [TestCase(1f, 0f, 0f, -1f, CustomerMood.Neutral)]
        [TestCase(0f, 0f, -1f, -2f, CustomerMood.Negative)]
        [TestCase(-5f, 0f, -3f, -2f, CustomerMood.VeryNegative)]
        public void Mood_FollowsTheEngineIndicatorChange(float trust, float openness, float engagement, float patience, CustomerMood expected)
        {
            var before = new SalesIndicators(40, 50, 25, 70);
            var after = new SalesIndicators(40 + trust, 50 + openness, 25 + engagement, 70 + patience);

            Assert.That(CustomerReaction.MoodAfterTurn(before, after), Is.EqualTo(expected));
        }
    }
}
