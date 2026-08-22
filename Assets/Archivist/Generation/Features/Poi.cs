using Archivist.Generation.Geometry;

namespace Archivist.Generation.Features
{
    /// <summary>
    /// POC-03 P1.1 — a point feature with a type, a position and a stable
    /// <see cref="FeatureId"/>, generated once per island in a deterministic order. The same
    /// contract as <see cref="Peak"/>, <see cref="Settlement"/> and <see cref="River"/>
    /// (POC-01 §3.1).
    ///
    /// <para>Unnamed by design: POC-03 §5 keeps text and labels out of scope, and the island
    /// name is the only label a detail sheet carries (P2.2).</para>
    /// </summary>
    public readonly struct Poi
    {
        public readonly FeatureId Id;
        public readonly V2 Position;
        public readonly PoiKind Kind;

        public Poi(FeatureId id, V2 position, PoiKind kind)
        { Id = id; Position = position; Kind = kind; }

        /// <summary>Spec §1.1 — which of the two families this belongs to.</summary>
        public bool IsRuin { get { return Kind.IsRuin(); } }

        public override string ToString() { return Id + " " + Kind.Label(); }
    }
}
