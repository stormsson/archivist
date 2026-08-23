using UnityEngine;

namespace Archivist.Building.Placement
{
    /// <summary>
    /// A named spot where something is put down — S3.4's anchor, made visible.
    ///
    /// <para><b>It does nothing at runtime, and that is the whole design.</b> An anchor is a
    /// pose and a footprint; who puts what there is the spawner's business, exactly as
    /// <c>MapCrate.dropAnchor</c> is a bare <c>Transform</c> the crate reads rather than a
    /// component that knows about crates. Nothing here is read by any behaviour: this class
    /// exists so a human can place the transform, and it is stripped of every temptation to
    /// grow a <c>Place()</c> method, because the moment it has one there are two places that
    /// decide where a binder lands.</para>
    ///
    /// <para><b>Why it exists at all: an empty transform cannot be positioned.</b> The anchor
    /// on <c>PF_CartographyTable</c> was an empty <c>GameObject</c> at the prefab origin —
    /// which is floor level, inside the table's legs. Unity draws nothing for an empty
    /// transform, so there was no rectangle to line up with the tabletop, no way to see that
    /// it was 0.81 m too low, and no way to tell whether the thing it holds would hang off the
    /// edge. Dragging it in the scene view meant dragging an invisible point against an
    /// invisible surface. The fix is not a better number; it is drawing the footprint, so the
    /// eye can do what the inspector cannot.</para>
    ///
    /// <para><b>The gizmo ignores <c>lossyScale</c> on purpose.</b> The obvious
    /// <c>Gizmos.matrix = transform.localToWorldMatrix</c> is wrong here, and wrong in the way
    /// that hides the bug: the anchor this was written for carried a scale of 0.1 — someone's
    /// attempt to make an invisible object less obtrusive — so a footprint drawn through that
    /// matrix would have read 41 mm across and looked plausible. Worse, an item parented to
    /// that anchor comes out at a tenth size. The footprint is drawn in metres from position
    /// and rotation only, so what you see is the real ground the item will cover whatever the
    /// scale says, and a scale that is not 1 shows up as a warning rather than as a small
    /// rectangle.</para>
    ///
    /// <para>Conventions, because an anchor with no convention is just a transform: <b>+Y is
    /// up out of the surface</b>, so the anchor sits <i>on</i> the tabletop and an item whose
    /// pivot is its underside needs no vertical fudge; <b>+Z is the facing</b>, the direction
    /// the item's front points, drawn as the arrow.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Archivist/Placement Anchor")]
    public sealed class PlacementAnchor : MonoBehaviour
    {
        /// <summary>A binder at its prefab scale of 1.2 — 0.34 × 0.33 m of card. The default
        /// because this is the first anchor there is, not because an anchor is for
        /// binders.</summary>
        public static readonly Vector2 DefaultFootprint = new Vector2(0.408f, 0.391f);

        [Header("What lands here")]
        [Tooltip("Ground the item covers, in metres: x across the anchor's local X, y along " +
                 "its local Z. Only ever drawn — nothing reads it — so it can be a rough " +
                 "measure of the thing you are placing rather than an exact one. Its job is " +
                 "to tell you, in the scene view, whether the item clears the table edge.")]
        [SerializeField] Vector2 footprint = DefaultFootprint;

        [Tooltip("Height of the item, in metres. Drawn as a wire box so a placed item can be " +
                 "seen not to be inside a shelf above it. Zero draws the flat footprint only.")]
        [SerializeField, Min(0f)] float clearance = 0.04f;

        [Header("Gizmo")]
        [Tooltip("Drawn even when the anchor is not selected. Off makes it selection-only, " +
                 "for a room that has grown enough anchors that they clutter the view.")]
        [SerializeField] bool alwaysVisible = true;

        [SerializeField] Color colour = new Color(0.95f, 0.75f, 0.25f, 1f);

        public Vector2 Footprint { get { return footprint; } }
        public float Clearance { get { return clearance; } }

        /// <summary>Where the item goes and which way it faces. What a spawner should read —
        /// never <c>transform.localPosition</c>, which is meaningless without its parent.</summary>
        public Vector3 Position { get { return transform.position; } }
        public Quaternion Rotation { get { return transform.rotation; } }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!alwaysVisible) return;
            Draw(colour * new Color(1f, 1f, 1f, 0.5f));
        }

        void OnDrawGizmosSelected()
        {
            Draw(colour);
        }

        /// <summary>
        /// The footprint on the surface, the volume above it, and the facing.
        ///
        /// <para>Built from position and rotation only — see the class comment on why the
        /// scale is deliberately thrown away.</para>
        /// </summary>
        void Draw(Color c)
        {
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = c;

            float x = Mathf.Max(footprint.x, 0.001f) * 0.5f;
            float z = Mathf.Max(footprint.y, 0.001f) * 0.5f;

            // The footprint sits ON the plane, not centred in the item: an anchor marks the
            // surface, and the pivot of everything put down in this game is its underside.
            var a = new Vector3(-x, 0f, -z);
            var b = new Vector3( x, 0f, -z);
            var d = new Vector3( x, 0f,  z);
            var e = new Vector3(-x, 0f,  z);

            Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, d);
            Gizmos.DrawLine(d, e); Gizmos.DrawLine(e, a);

            if (clearance > 0f)
            {
                Vector3 up = Vector3.up * clearance;
                Gizmos.DrawLine(a, a + up); Gizmos.DrawLine(b, b + up);
                Gizmos.DrawLine(d, d + up); Gizmos.DrawLine(e, e + up);
                Gizmos.DrawLine(a + up, b + up); Gizmos.DrawLine(b + up, d + up);
                Gizmos.DrawLine(d + up, e + up); Gizmos.DrawLine(e + up, a + up);
            }

            // Facing: an arrow along +Z, leaving the footprint so it is readable from above
            // even when the rectangle is nearly edge-on.
            float tip = z + Mathf.Max(0.06f, z * 0.35f);
            var nose = new Vector3(0f, 0f, tip);
            Gizmos.DrawLine(Vector3.zero, nose);
            Gizmos.DrawLine(nose, new Vector3(-x * 0.25f, 0f, z * 0.7f));
            Gizmos.DrawLine(nose, new Vector3( x * 0.25f, 0f, z * 0.7f));

            Gizmos.matrix = previous;

            // A scale that is not 1 shrinks anything parented here and makes every number in
            // this inspector a lie. Said in the view, where the mistake is made.
            Vector3 s = transform.lossyScale;
            if (Mathf.Abs(s.x - 1f) > 0.001f || Mathf.Abs(s.y - 1f) > 0.001f || Mathf.Abs(s.z - 1f) > 0.001f)
            {
                UnityEditor.Handles.color = Color.red;
                UnityEditor.Handles.Label(transform.position + Vector3.up * (clearance + 0.03f),
                                          $"anchor scale {s.x:0.##},{s.y:0.##},{s.z:0.##} — should be 1");
            }
        }
#endif
    }
}
