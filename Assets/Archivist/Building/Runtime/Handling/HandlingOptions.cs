using UnityEngine;

namespace Archivist.Building.Handling
{
    /// <summary>
    /// Tuning for how it feels to carry and place things — §3.5 of the requirements, which is
    /// the minute-to-minute game and the one part §5.1 says is not cuttable.
    ///
    /// <para>An asset rather than fields on <see cref="PlayerHands"/> for two reasons. These
    /// are <b>feel</b> values, found by playing and changed often, and an asset can be edited
    /// while in play mode and kept. And they are values several components will share: R5.2's
    /// carried-speed fraction belongs to the controller as much as to the hands, and putting
    /// it here later means neither owns it.</para>
    ///
    /// <para>Deliberately small. It holds what has actually been needed, in the same spirit as
    /// <c>Tuning</c> and <c>RenderTuning</c> — every constant in one place, and defaults that
    /// are starting points rather than findings. The obvious future members are named in §3.5:
    /// carried speed as a fraction of walk speed (R5.2), settle duration (R5.3), and whatever
    /// stack pickup turns out to need (R5.1).</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Archivist/Handling Options", fileName = "HandlingOptions")]
    public sealed class HandlingOptions : ScriptableObject
    {
        /// <summary>Used when no options asset is wired, so a missing asset costs the right
        /// feel rather than the ability to carry anything.</summary>
        public const float DefaultSheetTurnDegreesPerSecond = 120f;

        public const float DefaultSheetTakeSeconds = 0.28f;

        [Header("Carried sheet")]
        [Tooltip("Degrees per second while Q or E is held. 120 turns a sheet fully in three seconds.")]
        [SerializeField, Min(1f)] float sheetTurnDegreesPerSecond = DefaultSheetTurnDegreesPerSecond;

        [Tooltip("Seconds for a sheet to travel from the floor into the hands. Long enough " +
                 "to be read as a movement, short enough not to be waited on.")]
        [SerializeField, Min(0f)] float sheetTakeSeconds = DefaultSheetTakeSeconds;

        [Header("Falling sheet")]
        [Tooltip("Terminal speed in metres per second. Paper reaches this almost at once and " +
                 "then falls at it — that IS air resistance, as far as the eye is concerned.")]
        [SerializeField, Min(0.05f)] float fallSpeed = 1.1f;

        [Tooltip("How far the sheet slides sideways at the widest part of its swing, in metres.")]
        [SerializeField, Min(0f)] float fallSwayMetres = 0.08f;

        [Tooltip("Swings per second. Slower reads as heavier paper.")]
        [SerializeField, Min(0f)] float fallSwayHz = 1.2f;

        [Tooltip("How far the sheet tips at the widest part of its swing, in degrees.")]
        [SerializeField, Min(0f)] float fallTiltDegrees = 16f;

        public float SheetTurnDegreesPerSecond { get { return sheetTurnDegreesPerSecond; } }
        public float SheetTakeSeconds { get { return sheetTakeSeconds; } }
        public float FallSpeed { get { return fallSpeed; } }
        public float FallSwayMetres { get { return fallSwayMetres; } }
        public float FallSwayHz { get { return fallSwayHz; } }
        public float FallTiltDegrees { get { return fallTiltDegrees; } }
    }
}
