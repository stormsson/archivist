using System.Globalization;
using System.Text;
using Archivist.Generation.Determinism;
using Archivist.Generation.Features;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Generation.Analysis
{
    /// <summary>
    /// The single canonical digest of a generated island, for A2-style determinism checks
    /// (§13.2 — "same seed, identical island, across runs").
    ///
    /// <para>This lives in Generation, not in a test or a harness, because it was written
    /// twice: once headless and once in the Unity test assembly, over DIFFERENT field sets.
    /// A digest that omits a field cannot see that field diverge, so the two copies could —
    /// and did — disagree about whether an island was deterministic. One copy here, shared by
    /// every caller, makes "identical island" mean one thing.</para>
    ///
    /// <para>The field set, ordering, <c>"F6"</c> formatting, invariant culture and separators
    /// below are a REGRESSION ANCHOR, reproduced verbatim from the harness's original digest.
    /// The reference island hashes to <c>2933DCFC3DB132D5</c>. Changing any of it — even a
    /// separator — silently rebases every recorded hash and destroys the ability to compare
    /// against an earlier run, so change it only deliberately and re-record the anchors.</para>
    ///
    /// <para>Everything read here comes from ordered lists, never from a dictionary or set, so
    /// no iteration order can leak into the output (§4.1).</para>
    /// </summary>
    public static class IslandDigest
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// Fixed 6-decimal, invariant-culture formatting of a coordinate. Fixed width so a
        /// digest never depends on a value's magnitude, and invariant so a machine with a
        /// comma decimal separator produces the same bytes as one without.
        /// </summary>
        static string F(double d) { return d.ToString("F6", Inv); }

        /// <summary>
        /// The digest of <paramref name="island"/>: FNV-1a 64 over <see cref="Describe"/>.
        /// Contractually stable — never <c>GetHashCode</c>, which is process-randomised (§4.1).
        /// </summary>
        public static ulong Hash(Island island)
        {
            return Determinism.Hash.Fnv1a64(Describe(island));
        }

        /// <summary>
        /// The exact string <see cref="Hash"/> digests.
        ///
        /// <para>Exposed because a bare 64-bit mismatch tells you an island diverged but not
        /// WHERE. With the description in hand a caller can diff two runs and land directly on
        /// the offending field — the coastline vertex, the peak, the sheet centre — instead of
        /// bisecting the generator. It is derived purely from the island, so producing it
        /// costs nothing and perturbs nothing.</para>
        /// </summary>
        public static string Describe(Island isl)
        {
            var sb = new StringBuilder();
            sb.Append(isl.Params.Character).Append('|').Append(F(isl.Params.NominalRadius)).Append('|');
            sb.Append(isl.Name).Append('|');
            for (int i = 0; i < isl.Coastline.Count; i++)
            {
                Polyline p = isl.Coastline[i];
                sb.Append('C').Append(p.Count).Append(p.Closed ? 'c' : 'o');
                for (int v = 0; v < p.Count; v++) sb.Append(F(p[v].X)).Append(',').Append(F(p[v].Y)).Append(';');
            }
            for (int i = 0; i < isl.Features.Peaks.Count; i++)
            {
                Peak k = isl.Features.Peaks[i];
                sb.Append('P').Append(F(k.Position.X)).Append(',').Append(F(k.Position.Y))
                  .Append(',').Append(k.SpotHeightM).Append(',').Append(k.Name ?? "-").Append(';');
            }
            for (int i = 0; i < isl.Features.Settlements.Count; i++)
            {
                Settlement s = isl.Features.Settlements[i];
                sb.Append('S').Append(F(s.Position.X)).Append(',').Append(F(s.Position.Y))
                  .Append(',').Append(s.Name ?? "-").Append(';');
            }
            for (int i = 0; i < isl.Features.Rivers.Count; i++)
                sb.Append('R').Append(isl.Features.Rivers[i].Course.Count).Append(';');
            for (int i = 0; i < isl.Surveys.Count; i++)
            {
                Survey sv = isl.Surveys[i];
                sb.Append('V').Append(sv.Spec.Office).Append(',').Append(sv.Spec.Year).Append(',')
                  .Append(sv.Spec.Scale.Denominator).Append(',').Append(F(sv.Spec.RotationDeg)).Append(',')
                  .Append(sv.SheetCount).Append(';');
                for (int s = 0; s < sv.Sheets.Count; s++)
                    sb.Append(sv.Sheets[s].Number).Append(':').Append(F(sv.Sheets[s].CentreGround.X))
                      .Append(',').Append(F(sv.Sheets[s].CentreGround.Y)).Append(';');
            }
            return sb.ToString();
        }
    }
}
