using System;
using System.Collections.Generic;
using System.IO;

namespace SalesSim.Infrastructure
{
    /// <summary>
    /// Local settings such as API keys, under the Sales Engine's variable names (see .env.example): the process
    /// environment first, then <c>KEY=VALUE</c> lines of the git-ignored <c>.env</c>. Values are only handed on, never logged.
    /// </summary>
    public sealed class LocalSettings
    {
        private readonly Dictionary<string, string> fileValues;

        private LocalSettings(Dictionary<string, string> fileValues)
        {
            this.fileValues = fileValues;
        }

        /// <summary>Reads <paramref name="dotEnvPath"/> if it exists; a missing file just means "environment only".</summary>
        public static LocalSettings Load(string dotEnvPath)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (File.Exists(dotEnvPath))
            {
                foreach (var rawLine in File.ReadAllLines(dotEnvPath))
                {
                    var line = rawLine.Trim();
                    var separator = line.IndexOf('=');
                    if (line.Length == 0 || line[0] == '#' || separator <= 0)
                    {
                        continue;
                    }

                    values[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim().Trim('"');
                }
            }

            return new LocalSettings(values);
        }

        public string Get(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return fileValues.TryGetValue(name, out var fileValue) && fileValue.Length > 0 ? fileValue : null;
        }
    }
}
