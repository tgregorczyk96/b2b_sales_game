using NUnit.Framework;
using SalesSim.Application;
using SalesSim.Game;

namespace SalesSim.Tests.EditMode
{
    public sealed class GuidanceUsageTests
    {
        [Test]
        public void NewRun_IsUnassisted()
        {
            var usage = new GuidanceUsage();

            Assert.That(usage.Total, Is.EqualTo(0));
            Assert.That(usage.IsUnassisted, Is.True);
        }

        [Test]
        public void EachLevel_IsCountedSeparately_IncludingRepeats()
        {
            var usage = new GuidanceUsage();
            usage.Record(GuidanceLevel.Hint);
            usage.Record(GuidanceLevel.Hint);
            usage.Record(GuidanceLevel.HintMore);
            usage.Record(GuidanceLevel.Example);
            usage.Record(GuidanceLevel.Hint);

            Assert.That(usage.HintCount, Is.EqualTo(3));
            Assert.That(usage.HintMoreCount, Is.EqualTo(1));
            Assert.That(usage.ExampleCount, Is.EqualTo(1));
            Assert.That(usage.CountOf(GuidanceLevel.Hint), Is.EqualTo(3));
            Assert.That(usage.Total, Is.EqualTo(5));
            Assert.That(usage.IsUnassisted, Is.False);
        }
    }
}
