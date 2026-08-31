using UnityEngine;
using Archivist.Building.Binders;
using Archivist.Building.Handling;
using Archivist.Building.Interaction;

namespace Archivist.Building.Shelving
{
    /// <summary>
    /// One filing position on a shelf: a pose, a volume to aim at, and the two verbs that reach
    /// it. Built by <see cref="Shelf"/> and never by hand — its row and column are the key the
    /// save writes, so a slot that nothing generated is a slot nothing can name.
    ///
    /// <para><b>The slot is the aim target even when it is full.</b> Its box surrounds the binder
    /// standing in it, so the player's ray meets the box first and <c>PlayerInteractor</c>
    /// resolves it here — the binder is a <i>child</i>, and <c>GetComponentInParent</c> never
    /// walks downward. That is the design and not an accident of geometry: Q3.3 puts merging at
    /// the map table, so a binder in a rack must not offer <c>BinderPickup</c>'s merge, and
    /// shadowing it is what stops that. The two verbs a rack does have are answered here
    /// instead.</para>
    ///
    /// <para><b>Filing into a taken slot is refused with words, not dimmed.</b>
    /// <c>BinderPickup</c> makes the opposite call for full hands on the floor and says why —
    /// "a binder of another island is not a mistake being corrected, it is two unrelated
    /// objects". This is the other case: the player is putting a thing where a thing already is,
    /// and that is worth naming.</para>
    ///
    /// <para><b>Occupancy is the child list.</b> Nothing here counts, reserves or remembers.
    /// <c>RoomPaper.Capture</c> makes the argument in full — a private tally is "a second account
    /// of the room that is wrong the first time something takes a path this class has not heard
    /// of" — and a shelf is forty chances to be that wrong.</para>
    ///
    /// <para><b>The collider is solid, never a trigger.</b> <c>PlayerInteractor.Probe</c> raycasts
    /// with <c>QueryTriggerInteraction.Ignore</c>, so a trigger volume is invisible to the player
    /// and the slot could never be aimed at. It follows that the volume also blocks movement,
    /// which is why <see cref="Shelf"/> lays it <i>behind</i> the shelf face rather than out into
    /// the aisle.</para>
    ///
    /// <para><b>Any binder goes in any slot.</b> R4.5 makes placement state rather than score and
    /// R4.9 forbids a correctness readout, so nothing here asks what island a binder is or
    /// whether it belongs. The only refusal is physical.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShelfSlot : Interactable
    {
        public const string FileLabel = "File binder";
        public const string TakeLabel = "Take binder";

        /// <summary>Said out loud, because filing onto an occupied slot is a mistake and not
        /// merely an unavailable moment.</summary>
        public const string TakenReason = "there is already a binder here";

        /// <summary>
        /// The child that lights up under the player's aim — one name for both of them: the box
        /// <see cref="Shelf"/> builds on an empty slot, and the one this makes on a binder standing
        /// in a full one.
        ///
        /// <para>Unlike the debug cube it sits beside, this is <b>not</b> a debug affordance: it
        /// is the only thing that says which of a hundred identical slots the player is pointing
        /// at, so it survives into the game after the cubes are turned off.</para>
        /// </summary>
        public const string AimName = "Aim";

        /// <summary>Green for an act that will happen, red for one that is refused, and
        /// <b>nothing at all</b> for a slot with nothing to do. An empty slot aimed at with empty
        /// hands stays dark: the light means "this will happen if you press", so lighting a slot
        /// that would do nothing spends the only signal there is on the one case that has no
        /// answer. It is the same rule the prompt follows, where <c>Unavailable</c> without a
        /// reason says nothing rather than saying nothing loudly.</summary>
        static readonly Color ReadyTint   = new Color(0.35f, 0.90f, 0.45f, 0.40f);
        static readonly Color RefusedTint = new Color(0.90f, 0.28f, 0.24f, 0.40f);

        /// <summary>
        /// The turn that stands a binder up, spine out — R4.2's "folders and volumes → shelves,
        /// spine readable" and R4.3's one readable face.
        ///
        /// <para><b>This is the one assumption in the shelf about the binder's mesh.</b>
        /// <c>PF_Binder</c> is a flat folder lying in its own XZ plane, so a quarter turn about
        /// Z stands its long edge upright and leaves its thickness running across the shelf.
        /// When the binder model changes, this is the line to revisit — and the only one:
        /// everything else about where a binder lands is measured off its collider at the moment
        /// it is filed, so a taller or thicker folder needs no other edit.</para>
        /// </summary>
        public static readonly Quaternion Standing = Quaternion.Euler(0f, 0f, 90f);

        [Tooltip("Set by Shelf when the slots are built. The save names a slot by these two " +
                 "numbers, so they are the one thing here that must not be edited by hand.")]
        [SerializeField] int row;
        [SerializeField] int column;

        BoxCollider volume;
        Shelf shelf;

        MeshRenderer aim;
        MeshRenderer glowing;
        MaterialPropertyBlock tint;
        bool aimed;

        // Set by CanInteract and read by Label, the way BinderPickup does it and for the same
        // reason: PlayerInteractor asks CanInteract first and Label second, and Label has no
        // player to ask.
        bool taking;

        public int Row { get { return row; } }
        public int Column { get { return column; } }

        /// <summary>The shelf this belongs to. Walked rather than serialised so a slot dragged
        /// under another shelf answers for where it actually is.</summary>
        public Shelf Shelf
        {
            get
            {
                if (shelf == null) shelf = GetComponentInParent<Shelf>();
                return shelf;
            }
        }

        /// <summary>The box the player aims at. Resolved lazily — <c>Awake</c> never runs for
        /// objects made in edit mode.</summary>
        public BoxCollider Volume
        {
            get
            {
                if (volume == null) volume = GetComponent<BoxCollider>();
                return volume;
            }
        }

        /// <summary>The binder filed here, or null. Read off the children every time; see the
        /// class comment on why there is no field behind this.</summary>
        public BinderView Occupant
        {
            get
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    var binder = transform.GetChild(i).GetComponent<BinderView>();
                    if (binder != null) return binder;
                }
                return null;
            }
        }

        public bool IsEmpty { get { return Occupant == null; } }

        /// <summary>
        /// Whether this slot is something the player can hit at all. <see cref="Shelf"/> turns off
        /// the slots that could not answer anyway — an empty one while the hands hold no binder —
        /// so a rack full of gaps stops being a wall of targets.
        ///
        /// <para><b>It is the collider, so it is also the wall.</b> A slot volume is solid because
        /// <c>PlayerInteractor</c> ignores triggers, which means switching it off opens a hole the
        /// player can walk into unless the furniture carries a collider of its own. That is the
        /// furniture's job and not this one's — a slot standing in for a bookcase's geometry would
        /// be a rack whose solidity came and went with what the player was carrying.</para>
        ///
        /// <para>Anything lit goes dark on the way out: the aim cannot rest on a collider that no
        /// longer exists, and a glow nothing can extinguish would be left burning.</para>
        /// </summary>
        public void SetReachable(bool reachable)
        {
            BoxCollider box = Volume;
            if (box == null || box.enabled == reachable) return;

            box.enabled = reachable;
            if (!reachable) Unaimed();
        }

        /// <summary>Names the slot for a log and for the Hierarchy. Row and column as the player
        /// would count them, from one.</summary>
        public string Describe()
        {
            return "slot r" + (row + 1) + "c" + (column + 1);
        }

        /// <summary>Written by <see cref="Shelf"/> as it builds. Public because the builder is
        /// the only caller and a private setter would mean an editor-only friend.</summary>
        public void Configure(int atRow, int atColumn, float width, float height, float depth)
        {
            row = atRow;
            column = atColumn;

            BoxCollider box = Volume;
            if (box == null) box = volume = gameObject.AddComponent<BoxCollider>();

            // Behind the face, not through it. The anchor is the bottom-front-centre of the
            // opening, so the volume runs back along -Z and up along +Y from it — which keeps
            // a solid collider out of the aisle the player walks down.
            box.size = new Vector3(width, height, depth);
            box.center = new Vector3(0f, height * 0.5f, -depth * 0.5f);
            box.isTrigger = false;
        }

        // ---- the verbs ----------------------------------------------------------------------

        public override string Label { get { return taking ? TakeLabel : FileLabel; } }

        /// <summary>The inherited label field is never read: which verb a slot offers depends on
        /// what is in it and what is in the player's hands, so <see cref="Label"/> answers from
        /// state. The commoner of the two is written into the field anyway, so the Inspector shows
        /// something true rather than a stale "Interact" nothing honours.</summary>
        void Reset() { SetLabel(FileLabel); }

        /// <summary>
        /// The verb, and — while this is the slot being aimed at — the light on it.
        ///
        /// <para><b>The highlight is refreshed here rather than in <see cref="Aimed"/></b>
        /// because this is the method that runs every frame on the aimed slot, and the answer
        /// moves without the aim moving: fill your hands while staring at an empty slot and the
        /// light has to go from neutral to green under a pointer that never left.</para>
        /// </summary>
        public override InteractionState CanInteract(PlayerInteractor by)
        {
            InteractionState state = Decide(by);
            if (aimed) Light(state);
            return state;
        }

        InteractionState Decide(PlayerInteractor by)
        {
            taking = false;
            if (!isActiveAndEnabled) return InteractionState.Unavailable;

            PlayerHands hands = HandsOf(by);
            if (hands == null) return InteractionState.Unavailable;

            BinderView held = hands.Held as BinderView;
            if (held != null)
                return IsEmpty ? InteractionState.Ready : InteractionState.Refused(TakenReason);

            // Holding paper rather than a binder: nothing to say. A sheet is filed into a binder
            // (D-B2) and never onto a shelf, and a caption explaining that would be the game
            // commenting on a thing the player has not tried to do.
            if (!hands.IsEmpty) return InteractionState.Unavailable;

            if (IsEmpty) return InteractionState.Unavailable;

            taking = true;
            return InteractionState.Ready;
        }

        // ---- the light ------------------------------------------------------------------------

        /// <summary>Lazily found, and tolerated missing: a slot built before the aim child existed
        /// still files binders, it just does not glow. A rebuild gives it one.</summary>
        MeshRenderer Aim
        {
            get
            {
                if (aim == null)
                {
                    Transform child = transform.Find(AimName);
                    if (child != null) aim = child.GetComponent<MeshRenderer>();
                }
                return aim;
            }
        }

        public override void Aimed(PlayerInteractor by)
        {
            aimed = true;
            Light(Decide(by));
        }

        public override void Unaimed()
        {
            aimed = false;
            Darken();
        }

        void OnDisable() { Unaimed(); }

        /// <summary>Puts out whatever this slot last lit — which may be a binder's box rather than
        /// its own, and may be a binder that has since been carried away.</summary>
        void Darken()
        {
            if (glowing != null) glowing.enabled = false;
            glowing = null;
        }

        /// <summary>
        /// Lights the thing the act is about, in the colour of the answer.
        ///
        /// <para><b>An occupied slot lights its binder, not itself.</b> The empty slot is a place,
        /// so the place is what glows; a full one is a <i>binder</i>, and every verb there — take
        /// it, or be refused by it — is about that object. Lighting the slot's own box on top of a
        /// binder also washes the binder out, which hides the one thing the player was looking
        /// at.</para>
        ///
        /// <para>Only ever one box at a time, and the previous one is put out before the next is
        /// lit, so a binder taken out from under the aim does not leave its glow behind while the
        /// now-empty slot lights up as well.</para>
        ///
        /// <para>A <see cref="MaterialPropertyBlock"/> rather than a material per colour or per
        /// slot: every box in the room shares one asset, and tinting it per renderer neither
        /// instantiates that asset — which would leak a material the moment a slot is looked at —
        /// nor asks the builder to hand out a palette.</para>
        /// </summary>
        void Light(InteractionState state)
        {
            // Nothing to do here: dark, not dim. See the tints for why.
            if (!state.Available && string.IsNullOrEmpty(state.Reason)) { Darken(); return; }

            BinderView occupant = Occupant;
            MeshRenderer lit = occupant != null ? GlowOn(occupant) : Aim;

            if (!ReferenceEquals(lit, glowing)) Darken();
            if (lit == null) return;

            if (tint == null) tint = new MaterialPropertyBlock();
            lit.GetPropertyBlock(tint);
            tint.SetColor("_BaseColor", state.Available ? ReadyTint : RefusedTint);
            lit.SetPropertyBlock(tint);

            lit.enabled = true;
            glowing = lit;
        }

        /// <summary>
        /// The binder's own glow box, made the first time one is aimed at.
        ///
        /// <para><b>Built at runtime, unlike everything else on a shelf.</b> A binder is spawned
        /// from <c>PF_Binder</c> and never authored into a scene (Q7.3), so there is no edit-time
        /// moment at which to add this — and it is pure presentation: nothing reads it, nothing
        /// saves it, and <c>DontSave</c> keeps it out of any scene written to disk.</para>
        ///
        /// <para>Shaped from the binder's collider rather than its mesh, for the reason
        /// <see cref="PoseFor"/> measures the same box: it is the one description of the binder's
        /// size that survives the model being replaced.</para>
        ///
        /// <para>The mesh and material are borrowed from this slot's own aim box, so the two
        /// glows cannot drift apart and nothing has to hold a second material reference.</para>
        /// </summary>
        MeshRenderer GlowOn(BinderView binder)
        {
            Transform existing = binder.transform.Find(AimName);
            if (existing != null) return existing.GetComponent<MeshRenderer>();

            MeshRenderer template = Aim;
            if (template == null) return null;

            var box = binder.Body as BoxCollider;
            if (box == null) return null;

            var filter = template.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return null;

            var go = new GameObject(AimName);
            go.hideFlags = HideFlags.DontSave;
            go.layer = binder.gameObject.layer;

            go.transform.SetParent(binder.transform, false);
            go.transform.localPosition = box.center;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = box.size * 1.02f;

            go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;

            var made = go.AddComponent<MeshRenderer>();
            made.sharedMaterial = template.sharedMaterial;
            made.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            made.receiveShadows = false;
            made.enabled = false;

            return made;
        }

        public override void Interact(PlayerInteractor by)
        {
            PlayerHands hands = HandsOf(by);
            if (hands == null) return;

            Shelf on = Shelf;
            if (on == null) return;

            BinderView held = hands.Held as BinderView;
            if (held != null) { on.File(held, hands, this); return; }

            if (hands.IsEmpty) on.Take(this, hands);
        }

        static PlayerHands HandsOf(PlayerInteractor by)
        {
            return by != null ? by.GetComponent<PlayerHands>() : null;
        }

        // ---- where a binder stands ------------------------------------------------------------

        /// <summary>
        /// Where a binder comes to rest in this slot: standing, centred in the opening, its
        /// underside on the slot's floor.
        ///
        /// <para><b>Measured off the binder, not off a constant.</b> The pose cannot be the
        /// anchor alone: <c>PF_Binder</c>'s pivot is its contact point while it lies flat (S1.4),
        /// and once <see cref="Standing"/> has turned it that point is halfway up its side — a
        /// binder placed at the anchor would stand half-sunk through the shelf board. So the
        /// binder's own collider is rotated, and the pivot offset by where that puts its box.
        /// The arithmetic costs a few lines once and stays right when the model changes, which
        /// a measured offset in a field would not.</para>
        ///
        /// <para>A binder with no box collider gets the bare anchor pose: visibly wrong, in the
        /// place where it is wrong, rather than an exception thrown inside a placement.</para>
        /// </summary>
        public void PoseFor(BinderView binder, out Vector3 position, out Quaternion rotation)
        {
            rotation = transform.rotation * Standing;
            position = transform.position;

            BoxCollider box = Volume;
            if (box == null || binder == null) return;

            var body = binder.Body as BoxCollider;
            if (body == null) return;

            Vector3 scale = binder.transform.lossyScale;
            Vector3 centre = rotation * Vector3.Scale(body.center, scale);
            Vector3 half = Abs(Vector3.Scale(body.size * 0.5f, scale));

            Vector3 up = transform.up;

            // The opening's centre in world metres, then straight down to its floor — which is
            // the anchor's own plane, because the volume runs up from the anchor by exactly its
            // height. Dropping to the floor and lifting again keeps the binder centred across
            // and in depth for free: only the vertical coordinate changes.
            Vector3 middle = transform.TransformPoint(box.center);
            Vector3 floor = middle - up * (box.size.y * 0.5f);

            // Where the binder's BOX must end up: on the floor, lifted by its own reach upward.
            Vector3 seat = floor + up * Extent(rotation, half, up);

            // The transform goes wherever puts the box there — the two differ by however far
            // the pivot sits from the box's centre once the binder is stood up.
            position = seat - centre;
        }

        /// <summary>How far a box of the given half-extents, turned by <paramref name="turn"/>,
        /// reaches from its centre along <paramref name="axis"/>.</summary>
        static float Extent(Quaternion turn, Vector3 half, Vector3 axis)
        {
            return Mathf.Abs(Vector3.Dot(turn * new Vector3(half.x, 0f, 0f), axis))
                 + Mathf.Abs(Vector3.Dot(turn * new Vector3(0f, half.y, 0f), axis))
                 + Mathf.Abs(Vector3.Dot(turn * new Vector3(0f, 0f, half.z), axis));
        }

        static Vector3 Abs(Vector3 v)
        {
            return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }

#if UNITY_EDITOR
        /// <summary>The opening, drawn whether or not the debug volumes are on — so a shelf whose
        /// cubes are hidden can still be lined up against the furniture it belongs to.</summary>
        void OnDrawGizmosSelected()
        {
            BoxCollider box = Volume;
            if (box == null) return;

            // Scale included, like Shelf's own preview and unlike PlacementAnchor's: this draws
            // the collider that exists, and a scaled parent has already shrunk it.
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = IsEmpty ? new Color(0.95f, 0.75f, 0.25f, 1f)
                                   : new Color(0.35f, 0.75f, 0.45f, 1f);
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = previous;
        }
#endif
    }
}
