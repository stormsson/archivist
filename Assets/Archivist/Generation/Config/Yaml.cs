using System.Collections.Generic;

namespace Archivist.Generation
{
    /// <summary>
    /// A reader for the one shape of YAML this project writes: a section, an indented run of
    /// <c>key: value</c>, comments, blank lines. Nothing else.
    ///
    /// <para><b>Why not a YAML library.</b> There is no YAML parser in the .NET profile Unity
    /// ships, so using one means taking a package dependency into
    /// <c>Archivist.Generation</c> — the assembly whose whole discipline is that it references
    /// nothing, not even UnityEngine, so that the acceptance suite can run it headless. A
    /// dependency here would be the first crack in that. Against roughly eighty lines, on a
    /// file format that is one hundred and two numbers in nineteen groups, the trade is not
    /// close.</para>
    ///
    /// <para><b>What it deliberately does not support</b>, so that nobody writes a config file
    /// expecting it to work: anchors, aliases, multi-line scalars, quoting, lists, nested
    /// mappings deeper than one level, and multiple documents. A file using any of them does
    /// not fail — it produces entries that <see cref="Tuning"/> reports as unknown keys, which
    /// is a line in <c>Problems</c> naming the file and the line number. Silence would be the
    /// bad outcome; an error message is the point.</para>
    ///
    /// <para><b>Indentation is not significant, and that is on purpose.</b> Real YAML decides
    /// nesting by column, which is exactly the rule that makes hand-edited YAML fail in ways
    /// people cannot see. Here a line ending in a colon opens a section and every
    /// <c>key: value</c> after it belongs to that section however it is indented. The section
    /// is only ever used to tell the author they have filed a key somewhere surprising: a key
    /// is matched by its name alone, so getting the section wrong costs a warning, never a
    /// wrong number.</para>
    /// </summary>
    public static class Yaml
    {
        /// <summary>One <c>key: value</c>, and enough context to complain usefully about it.</summary>
        public readonly struct Entry
        {
            /// <summary>The section it appeared under, or null at the top of the file.</summary>
            public readonly string Section;

            public readonly string Key;
            public readonly string Value;

            /// <summary>1-based, so it matches what an editor shows.</summary>
            public readonly int Line;

            public Entry(string section, string key, string value, int line)
            {
                Section = section; Key = key; Value = value; Line = line;
            }

            public override string ToString()
            {
                return Section == null ? $"{Key}: {Value}" : $"{Section}.{Key}: {Value}";
            }
        }

        /// <summary>
        /// Every <c>key: value</c> in the file, in order, tagged with the section it sat
        /// under. Never null and never throws: a malformed line is skipped rather than
        /// reported, because the caller can only sensibly complain about keys it knows, and it
        /// cannot know about a line that has no key on it.
        /// </summary>
        public static List<Entry> Read(IReadOnlyList<string> lines)
        {
            var entries = new List<Entry>();
            if (lines == null) return entries;

            string section = null;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = Strip(lines[i]);
                if (line.Length == 0) continue;

                int colon = line.IndexOf(':');
                if (colon < 0) continue;

                string key = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (key.Length == 0) continue;

                // A colon with nothing after it opens a section. A key whose value is genuinely
                // empty is not a thing this file format has — every key here is a number.
                if (value.Length == 0) { section = key; continue; }

                entries.Add(new Entry(section, key, value, i + 1));
            }

            return entries;
        }

        /// <summary>
        /// Drops a trailing comment and surrounding space.
        ///
        /// <para>A bare <c>#</c> anywhere starts a comment, with no quoting rule to respect,
        /// because every value in this file is a number and no number contains a hash. That
        /// keeps the whole of quoting out of the reader, which is most of what a real YAML
        /// parser is.</para>
        /// </summary>
        static string Strip(string line)
        {
            if (line == null) return string.Empty;

            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash);

            return line.Trim();
        }
    }
}
