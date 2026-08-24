using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Archivist.Generation
{
    /// <summary>
    /// Finds <c>config/generation.yml</c>, reads it, and writes values back into it without
    /// destroying it.
    ///
    /// <para><b>It is an override sheet, not a source of truth.</b> Missing file, missing key,
    /// unreadable value — each falls back to the compiled default and records a line in
    /// <see cref="Source.Problems"/>. That is not politeness. This assembly is built to run
    /// headless (<c>Tools/run-acceptance.sh</c>) and inside a shipped player, and neither has
    /// any business failing to start because a YAML file was not on disk. A generator that
    /// refuses to generate is worse than one that generates the documented default.</para>
    ///
    /// <para><b>No UnityEngine, so no <c>Resources</c> and no <c>StreamingAssets</c></b>
    /// (CLAUDE.md — the rule that lets the acceptance suite run without an editor). The file is
    /// found by walking up the directory tree instead, which works from an editor, a test
    /// runner and a shell alike, and quietly finds nothing inside a shipped player — where the
    /// compiled defaults are the right answer anyway.</para>
    ///
    /// <para><b>Saving rewrites values in place, line by line.</b> The obvious implementation —
    /// serialise the parameter table and overwrite the file — was rejected: it would throw away
    /// the section banners, the ordering, and the inline notes like <c>#&#160;+/-&#160;8%</c>
    /// and <c>#&#160;coastline wiggle wavelength</c> that are the only explanation some of
    /// these numbers carry outside <c>Tuning.cs</c>. A config file that loses its comments the
    /// first time anyone presses Save is a config file nobody will comment. So a save edits the
    /// number on each key's own line and leaves every other character of the file alone.</para>
    /// </summary>
    public static class TuningFile
    {
        /// <summary>Environment variable naming the file outright, checked before any search so
        /// one run can be pointed at one file without moving anything.</summary>
        public const string EnvVar = "ARCHIVIST_GENERATION_CONFIG";

        /// <summary>What the search looks for in each directory on the way up.</summary>
        public const string RelativePath = "config/generation.yml";

        /// <summary>How far up to walk. Bounded rather than "until the root" because a symlink
        /// loop or a pathological path must not turn a missing config file into a hang at
        /// type-initialisation time, which is the least debuggable moment there is.</summary>
        const int MaxDepth = 12;

        /// <summary>
        /// One file, read: the values it named and everything wrong with it.
        ///
        /// <para>Lookup is by key alone. The scope in the file is checked and complained about
        /// but never used to resolve a name — <c>Tuning</c> is the authority on which scope a
        /// parameter belongs to, so a file somebody has reorganised still applies, and only
        /// says so.</para>
        /// </summary>
        public sealed class Source
        {
            readonly Dictionary<string, string> values;
            readonly List<string> problems;

            /// <summary>The file these came from, or null when none was found.</summary>
            public string Path { get; private set; }

            public IReadOnlyList<string> Problems { get { return problems; } }

            internal Source(string path, Dictionary<string, string> values, List<string> problems)
            {
                Path = path; this.values = values; this.problems = problems;
            }

            /// <summary>The named value, or <paramref name="fallback"/> when the file did not
            /// name it or named something that will not parse.</summary>
            public double Double(string key, double fallback)
            {
                string raw;
                if (!values.TryGetValue(key, out raw)) return fallback;

                double v;
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    return v;

                problems.Add($"{key}: '{raw}' is not a number");
                return fallback;
            }

            public int Int(string key, int fallback)
            {
                string raw;
                if (!values.TryGetValue(key, out raw)) return fallback;

                int v;
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    return v;

                problems.Add($"{key}: '{raw}' is not a whole number");
                return fallback;
            }
        }

        /// <summary>Reads the file the search finds, or hands back an empty source when there
        /// is none. Never throws: an unreadable file is a problem line, not an exception at
        /// type-initialisation.</summary>
        public static Source Load()
        {
            var values = new Dictionary<string, string>();
            var problems = new List<string>();

            string path = Locate();
            if (path == null) return new Source(null, values, problems);

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                problems.Add($"could not read {path}: {e.Message}");
                return new Source(null, values, problems);
            }

            List<Yaml.Entry> entries = Yaml.Read(lines);
            for (int i = 0; i < entries.Count; i++)
            {
                Yaml.Entry entry = entries[i];

                if (values.ContainsKey(entry.Key))
                {
                    // Last wins, matching how a reader would read it top to bottom — but said
                    // out loud, because a duplicated key is how a file quietly stops meaning
                    // what its author thinks it means.
                    problems.Add($"{path}:{entry.Line}: '{entry.Key}' appears more than once; the later value wins");
                }

                values[entry.Key] = entry.Value;
            }

            return new Source(path, values, problems);
        }

        /// <summary>
        /// Writes the given values into the file, changing nothing but the numbers.
        ///
        /// <para>A key the file does not mention is <b>not</b> appended. The file ships listing
        /// every parameter, so a missing key means someone deleted it deliberately to let the
        /// default apply, and re-adding it under a save would undo that silently.</para>
        /// </summary>
        public static bool Save(IEnumerable<KeyValuePair<string, string>> values, out string error)
        {
            error = null;

            string path = Locate();
            if (path == null)
            {
                error = $"no {RelativePath} found to write to";
                return false;
            }

            var wanted = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> pair in values) wanted[pair.Key] = pair.Value;

            try
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                    lines[i] = Rewrite(lines[i], wanted);

                File.WriteAllLines(path, lines);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error = $"could not write {path}: {e.Message}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// One line, with its number replaced if we have a new one for its key.
        ///
        /// <para>Indentation before the key and everything from the <c>#</c> onward are copied
        /// through untouched, so a save preserves both the layout and the note.</para>
        /// </summary>
        static string Rewrite(string line, Dictionary<string, string> wanted)
        {
            if (line == null) return string.Empty;

            int hash = line.IndexOf('#');
            string body = hash >= 0 ? line.Substring(0, hash) : line;
            string comment = hash >= 0 ? line.Substring(hash) : string.Empty;

            int colon = body.IndexOf(':');
            if (colon < 0) return line;

            string key = body.Substring(0, colon).Trim();
            string after = body.Substring(colon + 1);
            if (key.Length == 0 || after.Trim().Length == 0) return line;   // a scope header

            string replacement;
            if (!wanted.TryGetValue(key, out replacement)) return line;

            // The spacing between the colon and the number is kept: these files are read in
            // columns, and a save that reflowed them would show as a diff on every line.
            int lead = 0;
            while (lead < after.Length && after[lead] == ' ') lead++;

            int trail = after.Length;
            while (trail > lead && after[trail - 1] == ' ') trail--;

            return body.Substring(0, colon + 1) + after.Substring(0, lead) + replacement
                 + after.Substring(trail) + comment;
        }

        /// <summary>
        /// The environment variable, then <c>config/generation.yml</c> in the working directory
        /// and every directory above it, then the same walk from wherever this assembly was
        /// loaded.
        ///
        /// <para>Two walks because the working directory is the project root under the
        /// acceptance script and a shell, and something else entirely under a test runner or an
        /// editor, where the assembly's own location is the reliable one.</para>
        /// </summary>
        public static string Locate()
        {
            string named = Environment.GetEnvironmentVariable(EnvVar);
            if (!string.IsNullOrEmpty(named)) return File.Exists(named) ? named : null;

            string found = WalkUp(SafeCurrentDirectory());
            if (found != null) return found;

            return WalkUp(AppDomain.CurrentDomain.BaseDirectory);
        }

        static string SafeCurrentDirectory()
        {
            try { return Directory.GetCurrentDirectory(); }
            catch (Exception) { return null; }
        }

        static string WalkUp(string from)
        {
            if (string.IsNullOrEmpty(from)) return null;

            DirectoryInfo dir;
            try { dir = new DirectoryInfo(from); }
            catch (Exception) { return null; }

            for (int depth = 0; dir != null && depth < MaxDepth; depth++, dir = dir.Parent)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, RelativePath);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
