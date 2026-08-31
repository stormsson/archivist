using System.Collections.Generic;
using UnityEngine;
using Archivist.Building.Binders;
using Archivist.Building.Collection;
using Archivist.Building.Handling;

namespace Archivist.Building.Shelving
{
    /// <summary>
    /// A grid of filing positions, and the two verbs that move a binder in and out of one.
    /// R4.2's shelving: folders stand spine-out, one to a slot.
    ///
    /// <para><b>A shelf is one grid, not one piece of furniture.</b> The component sits on the
    /// empty marking the grid's top-left corner, and a bookcase with two banks of different
    /// sizes carries two of these. That is what keeps <c>(row, column)</c> a key: two grids under
    /// one shelf would both call a slot <c>(0,0)</c> and the save would name two places at
    /// once.</para>
    ///
    /// <para><b>The slots are built at edit time and saved with the scene</b> — they are not
    /// made in <c>Awake</c>. Three reasons, and the first is the one that decided it: an invisible
    /// grid cannot be lined up against the furniture it belongs to, checked against the S3.6 reach
    /// band, or seen to clear the shelf above, which is the whole argument <c>PlacementAnchor</c>
    /// was written to make. The second is that a slot can then be excepted by hand. The third is
    /// that authored slots exist before any component runs, so a restore can look one up without
    /// caring what order things woke in.</para>
    ///
    /// <para><b>Rebuilding wipes.</b> It destroys every slot it finds and makes them again from
    /// the numbers, so hand-work is lost — that is the accepted cost of keeping the six fields
    /// honest, and the dialog is the guard. Rebuild while the numbers are being chosen; stop once
    /// they are.</para>
    ///
    /// <para><b>No merging here</b> (Q3.3). A rack files and unfiles, and nothing else happens on
    /// one; comparing and merging are the map table's work. <see cref="ShelfSlot"/>'s volume
    /// shadowing its binder is what enforces that, rather than a flag somebody has to remember to
    /// check.</para>
    ///
    /// <para><b>Nothing here reads a binder's dimensions.</b> The slot's numbers are authored by
    /// eye and the standing pose is measured off the binder at the moment it is filed, so a new
    /// binder model changes one constant in <see cref="ShelfSlot.Standing"/> and nothing in this
    /// file.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Archivist/Shelf")]
    public sealed class Shelf : MonoBehaviour
    {
        /// <summary>The debug cube's name under each slot. One name, so hiding them is a lookup
        /// rather than a second marker component on an object that draws and does nothing.
        /// </summary>
        public const string VolumeName = "Volume";

        [Header("The grid")]
        [Tooltip("Rows, running DOWN from this transform.")]
        [SerializeField, Min(0)] int rowAmount = 4;

        [Tooltip("Slots per row, running RIGHT from this transform.")]
        [SerializeField, Min(0)] int slotsPerRow = 12;

        [Tooltip("Width of one slot, in metres — a binder's thickness plus enough that two " +
                 "neighbours do not touch. Nothing constrains the binder to it: this decides " +
                 "spacing and the volume the player aims at.")]
        [SerializeField, Min(0.001f)] float slotWidth = 0.05f;

        [Tooltip("Height of the opening, in metres. A standing binder is centred in it and " +
                 "rests on its floor.")]
        [SerializeField, Min(0.001f)] float slotHeight = 0.44f;

        [Tooltip("How far the slot runs back into the shelf, in metres. The volume lies behind " +
                 "the face — a solid collider in the aisle would block the player.")]
        [SerializeField, Min(0.001f)] float depth = 0.42f;

        [Tooltip("Space BETWEEN two rows, edge to edge. Row pitch is slotHeight + this.")]
        [SerializeField] float verticalGap = 0.06f;

        [Tooltip("Space BETWEEN two slots, edge to edge. Slot pitch is slotWidth + this.")]
        [SerializeField] float slotHorizontalGap = 0.004f;

        [Header("Debug")]
        [Tooltip("Draws each slot as a translucent cube. The volume the player aims at is the " +
                 "collider and is unaffected — turning this off hides the marker, not the slot.")]
        [SerializeField] bool showDebugVolumes = true;

        [SerializeField] bool logFiling = true;

        /// <summary>
        /// This shelf's name in the save. Serialised, derived when it is not, and minted by hand
        /// when two shelves must not share one — <c>CartographyTable.TableId</c> carries the full
        /// argument, and it applies here unchanged: a GUID that is drawn but never written to
        /// disk is a different id after the next domain reload, and the symptom is not a missing
        /// id but a binder standing in a slot this shelf has never heard of.
        /// </summary>
        [SerializeField, HideInInspector] string shelfId;

        readonly List<ShelfSlot> slots = new List<ShelfSlot>();

        PlayerHands hands;
        bool armed;         // the hands held a binder when reach was last worked out
        bool reachStale = true;

        public string ShelfId
        {
            get { return string.IsNullOrEmpty(shelfId) ? SceneIdentity.Derive(this) : shelfId; }
        }

        public int RowAmount { get { return rowAmount; } }
        public int SlotsPerRow { get { return slotsPerRow; } }
        public float SlotWidth { get { return slotWidth; } }
        public float SlotHeight { get { return slotHeight; } }
        public float Depth { get { return depth; } }

        void Awake() { ApplyDebugVolumes(); }

        /// <summary>
        /// Keeps the reachable set in step with what the player is carrying.
        ///
        /// <para><b>One comparison a frame, not a hundred.</b> What decides a slot's reach is a
        /// single fact — is there a binder in the hands — so this watches that and walks the slots
        /// only when the answer moves. Slots polling the player individually would be the cost
        /// this exists to avoid.</para>
        ///
        /// <para>Occupancy moves it too, and is not watched: filing and taking both change what
        /// the hands hold in the same gesture, so the flag those set is caught by the same
        /// comparison rather than by a second one.</para>
        /// </summary>
        void Update()
        {
            bool holding = Hands != null && Hands.Held is BinderView;
            if (!reachStale && holding == armed) return;

            armed = holding;
            reachStale = false;
            ApplyReach();
        }

        /// <summary>The room's hands. A scene singleton in practice, found once — a shelf that
        /// looked every frame would be doing the search this is meant to save.</summary>
        PlayerHands Hands
        {
            get
            {
                if (hands == null) hands = FindFirstObjectByType<PlayerHands>();
                return hands;
            }
        }

        /// <summary>
        /// Switches off every slot the player could not act on: with no binder in hand, only the
        /// full ones answer, so aiming down a half-empty rack meets the binders and nothing else.
        ///
        /// <para>Holding a binder makes every slot reachable, because an empty one is then the
        /// interesting target. Holding a loose sheet makes none of the empty ones reachable — a
        /// sheet is filed into a binder and never onto a shelf (D-B2) — which falls out of the
        /// same rule rather than needing its own.</para>
        /// </summary>
        public void ApplyReach()
        {
            bool holding = Hands != null && Hands.Held is BinderView;

            IReadOnlyList<ShelfSlot> here = Slots;
            for (int i = 0; i < here.Count; i++)
            {
                ShelfSlot slot = here[i];
                if (slot != null) slot.SetReachable(holding || !slot.IsEmpty);
            }
        }

        // ---- the slots ------------------------------------------------------------------------

        /// <summary>Every slot under this shelf, in the order the Hierarchy holds them. Cached:
        /// slots are authored, so nothing creates or destroys one while the game is running. The
        /// cache is dropped the moment it contains a hole, which is what a slot deleted by hand
        /// in edit mode leaves behind.</summary>
        public IReadOnlyList<ShelfSlot> Slots
        {
            get
            {
                if (slots.Count == 0 || HasHole()) Rescan();
                return slots;
            }
        }

        bool HasHole()
        {
            for (int i = 0; i < slots.Count; i++) if (slots[i] == null) return true;
            return false;
        }

        /// <summary>Re-reads the slots off the Hierarchy. Called by the builder, and by anything
        /// that has just changed the children.</summary>
        public void Rescan()
        {
            slots.Clear();
            GetComponentsInChildren(true, slots);
            reachStale = true;
        }

        /// <summary>The slot at a row and column, or null — which is what a save naming a slot
        /// that a later rebuild removed gets, and why that case has to be handled rather than
        /// asserted away.</summary>
        public ShelfSlot SlotAt(int row, int column)
        {
            IReadOnlyList<ShelfSlot> here = Slots;
            for (int i = 0; i < here.Count; i++)
            {
                ShelfSlot slot = here[i];
                if (slot != null && slot.Row == row && slot.Column == column) return slot;
            }
            return null;
        }

        /// <summary>Where slot (row, column)'s anchor stands, in this shelf's local metres: the
        /// bottom-centre of its opening. The transform is the grid's top-left CORNER, so slot
        /// (0,0) hangs below and to the right of it by half a slot.</summary>
        public Vector3 AnchorLocal(int row, int column)
        {
            float x = column * (slotWidth + slotHorizontalGap) + slotWidth * 0.5f;
            float y = -(row * (slotHeight + verticalGap)) - slotHeight;
            return new Vector3(x, y, 0f);
        }

        /// <summary>Shows or hides every debug cube without touching a collider. Live: the field
        /// can be flipped while the game is running and the shelf follows.</summary>
        public void ApplyDebugVolumes()
        {
            IReadOnlyList<ShelfSlot> here = Slots;
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] == null) continue;

                Transform cube = here[i].transform.Find(VolumeName);
                if (cube == null) continue;

                var renderer = cube.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.enabled = showDebugVolumes;
            }
        }

        // ---- filing ----------------------------------------------------------------------------

        /// <summary>
        /// Puts the held binder into a slot. Any binder into any slot: R4.5 makes placement state
        /// rather than score and R4.9 forbids a readout, so nothing here asks whether it belongs.
        ///
        /// <para><b>The binder is parented before the glide, not on arrival.</b> The journey takes
        /// a third of a second — long enough to pick up another binder and aim at the same slot —
        /// and occupancy is read off the children, so a binder in flight that belonged to nobody
        /// would leave the slot reading empty and two binders would land in one space.
        /// <c>ItemPlace</c> animates the world pose, so a parent changes nothing about the path.
        /// It has to happen <i>after</i> <see cref="PlayerHands.HandOver"/>, which unparents to
        /// the world on its way out.</para>
        ///
        /// <para><c>Archive.Note</c> on the landing rather than on the gesture (C9.2): the pose
        /// the file wants exists a third of a second after the player let go.</para>
        /// </summary>
        public bool File(BinderView binder, PlayerHands hands, ShelfSlot slot)
        {
            if (binder == null || hands == null || slot == null) return false;
            if (slot.Shelf != this) return false;
            if (hands.Held != (ICarryable)binder) return false;
            if (!slot.IsEmpty) return false;

            Vector3 position;
            Quaternion rotation;
            slot.PoseFor(binder, out position, out rotation);

            if (!hands.HandOver(position, rotation, item => Archive.Note())) return false;

            Seat(binder, slot);
            reachStale = true;

            if (logFiling)
                Debug.Log($"[Shelf] filed {binder.Summary} into {slot.Describe()}", this);

            return true;
        }

        /// <summary>Takes the binder in a slot back into empty hands. <c>PlayerHands.Take</c>
        /// reparents to the hold anchor and notes the archive itself, so there is nothing to
        /// unparent here and nothing is changed until the hands have agreed to it.</summary>
        public bool Take(ShelfSlot slot, PlayerHands hands)
        {
            if (slot == null || hands == null || !hands.IsEmpty) return false;

            BinderView binder = slot.Occupant;
            if (binder == null) return false;
            if (!hands.Take(binder)) return false;
            reachStale = true;

            if (logFiling)
                Debug.Log($"[Shelf] took {binder.Summary} from {slot.Describe()}", this);

            return true;
        }

        /// <summary>
        /// A binder read back out of the save, into the slot it was filed in (§9).
        ///
        /// <para><b>The pose is recomputed, never restored.</b> <c>CartographyTable.Restore</c>
        /// takes the pose from the file because a runtime jitter decided the angle and nothing
        /// could work it out again. A shelved binder has no jitter: where it stands is a function
        /// of its slot and its own box, so recomputing is not merely equivalent, it is better —
        /// the shelf can be moved, or its numbers changed, and every binder on it comes back
        /// standing where the new grid says rather than hanging where the old one did. That is
        /// the whole reason the save names a slot instead of a place.</para>
        /// </summary>
        public bool Restore(BinderView binder, int row, int column)
        {
            if (binder == null) return false;

            ShelfSlot slot = SlotAt(row, column);
            if (slot == null || !slot.IsEmpty) return false;

            Vector3 position;
            Quaternion rotation;
            slot.PoseFor(binder, out position, out rotation);

            binder.transform.SetPositionAndRotation(position, rotation);
            Seat(binder, slot);
            reachStale = true;

            if (logFiling)
                Debug.Log($"[Shelf] restored {binder.Summary} into {slot.Describe()}", this);

            return true;
        }

        /// <summary>The binder becomes the slot's child, which is what makes the slot occupied —
        /// there is no second record to keep in step.</summary>
        static void Seat(ICarryable item, ShelfSlot slot)
        {
            if (item == null || item.Root == null || slot == null) return;
            item.Root.SetParent(slot.transform, worldPositionStays: true);
        }

        // ---- identity ---------------------------------------------------------------------------

#if UNITY_EDITOR
        /// <summary>Pins this shelf's id, and keeps the debug cubes in step with the
        /// checkbox.</summary>
        void OnValidate()
        {
            ApplyDebugVolumes();
            SceneIdentity.Pin(this, ref shelfId);
        }

        [ContextMenu("Mint a new shelf id")]
        void MintShelfId()
        {
            SceneIdentity.Mint(this, ref shelfId);
        }

        /// <summary>Installed by <c>ShelfGridBuilder</c> at editor load. Building a grid is
        /// edit-time work — dialogs, <c>Undo</c>, project assets — and lives in an assembly this
        /// one cannot reference, but the button belongs on the component the numbers are on.
        /// </summary>
        public static System.Action<Shelf> Builder;

        /// <summary>
        /// Destroys every slot under this shelf and builds the grid again from the six numbers.
        ///
        /// <para><b>Destructive on purpose</b> — see the class comment. The alternative, an
        /// additive rebuild that leaves existing slots alone, makes the numbers a lie after the
        /// first press.</para>
        /// </summary>
        [ContextMenu("Rebuild slots")]
        public void RebuildSlots()
        {
            if (Builder != null) Builder(this);
        }

        /// <summary>
        /// The grid the numbers describe, whether or not it has been built — so the six fields can
        /// be tuned against the furniture before anything is generated, and a shelf whose slots
        /// were wiped still shows where they would come back.
        ///
        /// <para><b>Drawn through <c>localToWorldMatrix</c>, scale included</b>, which is the
        /// opposite of what <c>PlacementAnchor</c> does and deliberately so. That gizmo draws a
        /// footprint in metres, so a scaled anchor must look wrong; this one is a promise about
        /// what <see cref="RebuildSlots"/> will make, and what it makes is children — which a
        /// scaled parent shrinks. A preview that ignored the scale would show a grid nothing was
        /// ever going to build.</para>
        /// </summary>
        void OnDrawGizmosSelected()
        {
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.95f, 0.75f, 0.25f, 0.65f);

            Vector3 centre, size;
            ShelfSlot.SlotBox(slotWidth, slotHeight, depth, out centre, out size);

            for (int r = 0; r < rowAmount; r++)
                for (int c = 0; c < slotsPerRow; c++)
                    Gizmos.DrawWireCube(AnchorLocal(r, c) + centre, size);

            Gizmos.matrix = previous;
            WarnIfScaled();
        }

        /// <summary>
        /// Says so, in the view, when this shelf is not standing at 1:1.
        ///
        /// <para>A scaled shelf makes every field above it a lie — type 0.44 into
        /// <c>slotHeight</c> under a parent at 0.6 and the slot is 0.264 m — and the lie is not
        /// cosmetic: a binder keeps its own world size when it is seated (<c>SetParent</c> with
        /// <c>worldPositionStays</c>), so it cannot shrink to fit and simply will not go in. S1.1
        /// already calls a scale factor other than 1 a defect rather than a preference, and S1.4
        /// says a prefab root is always <c>(1,1,1)</c>; this is where that gets noticed, because
        /// the Inspector cannot show it.</para>
        /// </summary>
        void WarnIfScaled()
        {
            Vector3 s = transform.lossyScale;
            if (Mathf.Abs(s.x - 1f) <= 0.001f && Mathf.Abs(s.y - 1f) <= 0.001f
                && Mathf.Abs(s.z - 1f) <= 0.001f) return;

            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 0.1f,
                $"shelf scale {s.x:0.##},{s.y:0.##},{s.z:0.##} — should be 1 (S1.1). " +
                $"Every metre below is really {s.y:0.##} m.");
        }
#endif
    }
}
