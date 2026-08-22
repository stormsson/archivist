using System.Collections.Generic;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Features
{
    /// <summary>§7. Grid and Sounding are field-derived; Peak/River/Settlement are discrete (§3.1).</summary>
    public enum FeatureClass
    {
        Coast = 0,
        Contour = 1,
        Peak = 2,
        River = 3,
        Settlement = 4,
        Grid = 5,
        Sounding = 6
    }

    public readonly struct FeatureId
    {
        public readonly FeatureClass Class;
        public readonly int Index;
        public FeatureId(FeatureClass cls, int index) { Class = cls; Index = index; }
        public override string ToString() { return Class + "#" + Index; }
    }

    public readonly struct Peak
    {
        public readonly FeatureId Id;
        public readonly V2 Position;
        public readonly int SpotHeightM;     // rounded to the metre (§7.1)
        public readonly string Name;         // null unless in the top PeakNamedCount

        public Peak(FeatureId id, V2 position, int spotHeightM, string name)
        { Id = id; Position = position; SpotHeightM = spotHeightM; Name = name; }

        public Peak WithName(string name) { return new Peak(Id, Position, SpotHeightM, name); }
    }

    public readonly struct Settlement
    {
        public readonly FeatureId Id;
        public readonly V2 Position;
        public readonly double Score;
        public readonly string Name;         // every settlement is named (§7.2 step 6)

        public Settlement(FeatureId id, V2 position, double score, string name)
        { Id = id; Position = position; Score = score; Name = name; }

        public Settlement WithName(string name) { return new Settlement(Id, Position, Score, name); }
    }

    public readonly struct River
    {
        public readonly FeatureId Id;
        public readonly Polyline Course;
        public readonly int SourcePeakIndex;

        public River(FeatureId id, Polyline course, int sourcePeakIndex)
        { Id = id; Course = course; SourcePeakIndex = sourcePeakIndex; }
    }

    /// <summary>Field-derived, so no stable id (§6.3).</summary>
    public readonly struct Sounding
    {
        public readonly V2 Position;
        public readonly int DepthM;          // positive metres below sea, rounded
        public Sounding(V2 position, int depthM) { Position = position; DepthM = depthM; }
    }

    /// <summary>Generated once per island, in a deterministic order, with stable ids (§3.1).</summary>
    public sealed class IslandFeatures
    {
        public IslandFeatures(IReadOnlyList<Peak> peaks, IReadOnlyList<Settlement> settlements, IReadOnlyList<River> rivers)
        {
            Peaks = peaks; Settlements = settlements; Rivers = rivers;
        }

        public IReadOnlyList<Peak> Peaks { get; private set; }
        public IReadOnlyList<Settlement> Settlements { get; private set; }
        public IReadOnlyList<River> Rivers { get; private set; }
    }
}
