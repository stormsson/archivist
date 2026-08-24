using System;
using System.Collections.Generic;
using System.Globalization;

namespace Archivist.Generation
{
    /// <summary>
    /// Writing the live values back to <c>config/generation.yml</c>.
    ///
    /// <para>Hand-written, and deliberately not in <c>Tuning.Values.cs</c>: that file is
    /// generated from <c>Tuning.cs</c> and regenerated whenever a constant is added, so
    /// anything with judgement in it does not belong there. What is generated is a list; what
    /// is here is a policy.</para>
    /// </summary>
    public static partial class Tuning
    {
        /// <summary>
        /// Writes back only the values that actually differ from what the file currently says.
        ///
        /// <para><b>Why the file is re-read first.</b> Saving all 102 values would rewrite every
        /// line, and the rewrite would not be a no-op even where nothing changed: round-trip
        /// formatting turns <c>0.20</c> into <c>0.2</c> and <c>594.0</c> into <c>594</c>. Both
        /// are the same number and neither is the same text, so a save with one edit in it would
        /// arrive as a hundred-line diff, and the one line that mattered would be invisible in
        /// review. Comparing against the file first means a save touches exactly the lines the
        /// author changed.</para>
        ///
        /// <para><b>A key the file does not contain is not added</b> — see
        /// <see cref="TuningFile.Save"/>, which treats an absent key as deliberate: someone
        /// deleted it to let the default apply. The consequence, stated because it is
        /// surprising: overriding such a value in the window works for this domain and does not
        /// survive a save. The shipped file lists every parameter, so this only arises in a file
        /// somebody has cut down.</para>
        ///
        /// <para>Returns true with no error when nothing needed writing. A Save button that
        /// reports failure because there was nothing to do would train its user to ignore
        /// it.</para>
        /// </summary>
        public static bool Save(out string error)
        {
            error = null;

            IReadOnlyList<Parameter> live = Parameters;
            if (live == null || live.Count == 0) return true;

            TuningFile.Source onDisk = TuningFile.Load();
            var changed = new List<KeyValuePair<string, string>>();

            for (int i = 0; i < live.Count; i++)
            {
                Parameter p = live[i];

                // Compared through the same parser that will read it back, so "0.20" and 0.2
                // count as equal and a save does not chase its own formatting.
                double fromFile = p.IsInteger
                    ? onDisk.Int(p.Name, (int)Math.Round(p.Default))
                    : onDisk.Double(p.Name, p.Default);

                if (fromFile == p.Value) continue;

                changed.Add(new KeyValuePair<string, string>(p.Name, Format(p)));
            }

            if (changed.Count == 0) return true;

            return TuningFile.Save(changed, out error);
        }

        /// <summary>Invariant culture, always: a decimal point has to mean the same thing in
        /// this file on every machine, which is what lets two people compare a
        /// <see cref="Fingerprint"/> at all.</summary>
        static string Format(Parameter p)
        {
            if (p.IsInteger)
                return ((long)Math.Round(p.Value)).ToString(CultureInfo.InvariantCulture);

            return p.Value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
