using System;
using System.IO;
using NUnit.Framework;
using SalesSim.Infrastructure;

namespace SalesSim.Tests.EditMode
{
    /// <summary>
    /// How keys reach the game: the git-ignored .env is read fresh on every load, and a process environment variable of
    /// the same name wins over it (which can hide a new key in .env). Only fake values.
    /// </summary>
    public sealed class LocalSettingsTests
    {
        private const string Name = "SALESSIM_UNITY_TEST_SETTING";
        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "salessim-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable(Name, null);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(Name, null);
            Directory.Delete(directory, true);
        }

        [Test]
        public void DotEnv_IsRead_WithQuotesAndCommentsHandled()
        {
            var path = Write("# comment", $"{Name}=\"sk-ant-test-fake\"", "BROKEN LINE");

            var settings = LocalSettings.Load(path);

            Assert.That(settings.Get(Name), Is.EqualTo("sk-ant-test-fake"));
            Assert.That(settings.SourceOf(Name), Is.EqualTo(".env"));
        }

        [Test]
        public void ChangedDotEnv_IsUsed_OnTheNextLoad()
        {
            var path = Write($"{Name}=old-expired-key");
            Assert.That(LocalSettings.Load(path).Get(Name), Is.EqualTo("old-expired-key"));

            Write($"{Name}=new-key");

            Assert.That(LocalSettings.Load(path).Get(Name), Is.EqualTo("new-key"), "A restart (new load) picks up the new key.");
        }

        [Test]
        public void EnvironmentVariable_WinsOverDotEnv_AndTheSourceSaysSo()
        {
            var path = Write($"{Name}=key-from-file");
            Environment.SetEnvironmentVariable(Name, "key-from-environment");

            var settings = LocalSettings.Load(path);

            Assert.That(settings.Get(Name), Is.EqualTo("key-from-environment"));
            Assert.That(settings.SourceOf(Name), Is.EqualTo("environment (overrides .env)"));
            Assert.That(settings.SourceOf(Name), Does.Not.Contain("key-from"), "The source never contains a value.");
        }

        [Test]
        public void MissingFileOrValue_IsNotSet()
        {
            var settings = LocalSettings.Load(Path.Combine(directory, "missing.env"));

            Assert.That(settings.Get(Name), Is.Null);
            Assert.That(settings.SourceOf(Name), Is.EqualTo("not set"));
        }

        private string Write(params string[] lines)
        {
            var path = Path.Combine(directory, ".env");
            File.WriteAllLines(path, lines);
            return path;
        }
    }
}
