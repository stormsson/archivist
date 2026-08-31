using System.Collections.Generic;
using UnityEngine;
using Archivist.Building.Binders;
using Archivist.Building.Collection;
using Archivist.Building.Handling;
using Archivist.Building.Interaction;
using Archivist.Building.Sheets;
using Archivist.Building.Table;

namespace Archivist.Building.Interactables
{
    /// <summary>
    /// The map table: the diegetic way into the board view (C8.1), and now also the place
    /// binders are laid down. Nobody asked for this — it is the game's second, optional
    /// activity (R6.1) — which is why it is a thing in the room you may walk past rather than
    /// a screen the game puts in front of you.
    ///
    /// <para><b>A table is bound by what is lying on it, and carries no serialised
    /// identity.</b> The island named by the first binder on its anchors is a fact about the
    /// room: it needs no minting, survives no domain reload because it was never serialised, and
    /// cannot be shared between two tables by accident. C4.1–C4.4 are enforced here — unbound
    /// tables accept any binder, the first fixes the island, a bound table takes only that
    /// island, and taking the last binder off returns it to unbound.</para>
    ///
    /// <para><b>It does carry a serialised <c>tableId</c></b> (§4.1), which is a different fact
    /// from its binding: the id says <i>which table this is</i> so its board and its binders can
    /// be found again after the game is closed (§9), while the binding says which island is on it
    /// today. It is <b>derived</b> from this table's place in the scene rather than drawn, so an
    /// unsaved scene and a saved one agree — see <see cref="TableId"/> for why a drawn GUID is
    /// the wrong mechanism here, and <see cref="SceneIdentity"/> for how it is arrived at and
    /// pinned.</para>
    ///
    /// <para><b>What the verb is depends on what is in the player's hands</b>, and this is the
    /// one interactable where that is true of the <i>label</i> and not only of availability.
    /// Holding a binder, the key adds it to the table; holding a loose sheet, it files that
    /// sheet into the binder on the table; empty-handed, it opens the board. Three verbs, one
    /// key, because they are three ways of saying the same thing to the same object. See
    /// <see cref="Label"/> for the one piece of coupling this costs.</para>
    ///
    /// <para><b>The board is opened on the table's own island, through its own binders.</b>
    /// <c>TableSession.Open</c> takes an <see cref="ISheetSource"/> so the cabinet's answer
    /// belongs to whoever opened it (§4.3). Feeding it from the ledger instead lists every sheet
    /// ever <i>issued</i> of the last island the room drew — which agrees with the binder in the
    /// player's hands only for the most recent crate opening, and even then lists one sheet too
    /// many, because <c>MapCrate</c>'s <c>looseDebugSheet</c> issues a sixth onto the
    /// floor.</para>
    ///
    /// <para><b>The mode switch is still not here</b> (§8.2). Disabling
    /// <c>FirstPersonController</c>, <c>PlayerInteractor</c> and <c>PlayerHands</c> as
    /// components (C8.4, C8.5) and letting the controller own the cursor (C8.6) is
    /// <c>TableSession</c>'s, because that is a change about the room, not about this table,
    /// and half of it built here would be a second cursor owner. There will eventually be more
    /// than one table in the archive, and only ever one room.</para>
    /// </summary>
    public sealed class CartographyTable : Interactable
    {
        /// <summary>C8.1's verb: what the key does with empty hands.</summary>
        public const string DefaultLabel = "Open Cartography table";

        /// <summary>What the key does while carrying a binder.</summary>
        public const string PlaceBinderLabel = "Add to table";

        /// <summary>What the key does while carrying a loose sheet.</summary>
        public const string FileSheetLabel = "File sheet";

        /// <summary>The child that groups the anchors. Found by name when nothing is wired,
        /// so a table built by a script or reverted to prefab still works.</summary>
        public const string AnchorRootName = "BindingAnchors";

        [Header("Identity")]
        [Tooltip("§4.1: which table this is, so its board can be found again next session. " +
                 "Minted once and never regenerated — changing it hands this table somebody " +
                 "else's board, and clearing it loses this one's.")]
        [SerializeField] string tableId;

        [Header("Where binders land")]
        [Tooltip("The empty whose children are the binder anchors, lowest first. Capacity is " +
                 "its child count — adding a slot is a prefab edit, made by eye with the " +
                 "PlacementAnchor gizmo. Left empty, the child named 'BindingAnchors' is used.")]
        [SerializeField] Transform bindingAnchors;

        [Tooltip("Degrees either side of the anchor's own yaw. Rolled fresh on every " +
                 "placement, so a binder put back down lies differently.")]
        [SerializeField, Min(0f)] float rotationJitterDegrees = 20f;

        [Tooltip("One console line per binder placed, filed or taken. On by default while " +
                 "this is the newest thing in the room.")]
        [SerializeField] bool logHandling = true;

        // What is on the table, lowest anchor first. Kept in step by Place/TakeTop rather than
        // recomputed, and pruned of nulls on every read — see Prune.
        readonly List<BinderView> placed = new List<BinderView>();

        // Set by CanInteract, read by Label. See Label for why this is a field and not an
        // argument.
        string verb = DefaultLabel;

        // ---- identity ------------------------------------------------------------------------

        /// <summary>
        /// The key this table's board and its binders are stored under (§4.1, C1.7).
        ///
        /// <para><b>Never empty, and never a fresh answer.</b> An unminted table derives the same
        /// id its <c>OnValidate</c> would have minted, from where it sits in the scene — so a
        /// table works before anybody has saved the scene, and works the same way after. That
        /// property is the whole point: a <see cref="System.Guid.NewGuid"/> that is minted into a
        /// component and never written to disk is a <i>different</i> id on the next domain
        /// reload, and the symptom is not "my table has no id" but a binder standing on its
        /// anchor that the table has never heard of, and a board nothing can open.</para>
        ///
        /// <para>The serialised field still wins when it is set, which is what makes the id
        /// survive the table later being renamed or reparented — and it is why saving the scene
        /// after adding a table is worth doing rather than required.</para>
        /// </summary>
        public string TableId
        {
            get { return string.IsNullOrEmpty(tableId) ? SceneIdentity.Derive(this) : tableId; }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            SceneIdentity.Pin(this, ref tableId);
        }

        [ContextMenu("Mint a new table id")]
        void MintTableId()
        {
            SceneIdentity.Mint(this, ref tableId);
        }
#endif

        // ---- what is on the table ----------------------------------------------------------

        /// <summary>How many binders this table can hold: the anchor count, read from the
        /// prefab and never a constant, so capacity is authored rather than coded.</summary>
        public int Capacity
        {
            get
            {
                Transform anchors = Anchors;
                return anchors != null ? anchors.childCount : 0;
            }
        }

        /// <summary>How many binders are on it.</summary>
        public int BinderCount { get { Prune(); return placed.Count; } }

        /// <summary>
        /// Whether the binders on this table hold the island's whole-island chart (R2.2a) — the
        /// one sheet a board cannot open without (R6.8a).
        ///
        /// <para>Read off the contents rather than remembered, like everything else about a
        /// table's binding (B1.2): a chart arrives when the binder holding it is put down and
        /// leaves when that binder is taken away, and nothing has to be told.</para>
        ///
        /// <para>There is exactly one per island (Q2.3), so this stops at the first.</para>
        /// </summary>
        public bool HasChart
        {
            get
            {
                Prune();
                for (int b = 0; b < placed.Count; b++)
                {
                    BinderView binder = placed[b];
                    if (binder == null) continue;

                    IReadOnlyList<SheetId> held = binder.Contents;
                    for (int i = 0; i < held.Count; i++)
                        if (held[i].WholeIsland) return true;
                }
                return false;
            }
        }

        /// <summary>No anchor left free.</summary>
        public bool IsFull { get { Prune(); return placed.Count >= Capacity; } }

        /// <summary>The binders on it, lowest anchor first. Read-only: the pile changes
        /// through <see cref="Place"/> and <see cref="TakeTop"/> and nowhere else, so the
        /// one-island rule cannot be walked around.</summary>
        public IReadOnlyList<BinderView> Binders { get { Prune(); return placed; } }

        /// <summary>The island this table is laid out for, or 0 while unbound (C4.1, C4.2).
        /// Taken from the <i>first</i> binder, not the newest: the one that bound it is the
        /// one that will be last off.</summary>
        public ulong BoundSeed
        {
            get
            {
                Prune();
                return placed.Count > 0 ? placed[0].IslandSeed : 0UL;
            }
        }

        /// <summary>Its island's name, for saying so out loud. Falls back to the seed, because
        /// a refusal that names nothing is worse than one that names a number.</summary>
        public string BoundIslandName
        {
            get
            {
                Prune();
                if (placed.Count == 0) return string.Empty;

                string named = placed[0].IslandName;
                return string.IsNullOrEmpty(named) ? placed[0].IslandSeed.ToString("X16") : named;
            }
        }

        // ---- the verb ----------------------------------------------------------------------

        /// <summary>
        /// The verb for whatever the player is carrying — which means this reads a field that
        /// <see cref="CanInteract"/> wrote.
        ///
        /// <para><b>Why that is safe.</b> <c>Label</c> has no <c>PlayerInteractor</c> to ask,
        /// and finding the hands from here would be a scene-wide search every frame the player is
        /// aimed at a table. <c>PlayerInteractor.Refresh</c> calls <c>CanInteract</c> and then
        /// <c>Label</c>, in that order, every frame, so the answer is never older than the state
        /// it describes; reversing that order costs one frame of lag and no more. This field
        /// names what the key does, never whether it can be pressed — that is
        /// <see cref="InteractionState"/>'s job and stays in <see cref="CanInteract"/>.</para>
        /// </summary>
        public override string Label { get { return verb; } }

        /// <summary>
        /// Three verbs and their refusals.
        ///
        /// <para>An unrecognised carried item dims silently rather than refusing: laying
        /// something else on the map table is a verb that does not exist yet (§13), and a
        /// worded refusal would promise that it does.</para>
        /// </summary>
        public override InteractionState CanInteract(PlayerInteractor by)
        {
            InteractionState basic = base.CanInteract(by);
            if (!basic.Available)
            {
                verb = DefaultLabel;
                return basic;
            }

            Prune();

            PlayerHands hands = HandsOf(by);
            ICarryable held = hands != null ? hands.Held : null;

            var binder = held as BinderView;
            if (binder != null)
            {
                verb = PlaceBinderLabel;

                if (placed.Count >= Capacity)
                    return InteractionState.Refused("No room on this table");

                // C4.3. Named after the TABLE's island rather than the binder's: the player can
                // read the pile in front of them and has no way at all to read what is in their
                // own hands.
                if (placed.Count > 0 && binder.IslandSeed != placed[0].IslandSeed)
                    return InteractionState.Refused(LaidOutFor);

                return InteractionState.Ready;
            }

            var sheet = held as SheetView;
            if (sheet != null)
            {
                verb = FileSheetLabel;

                if (placed.Count == 0)
                    return InteractionState.Refused("No binder on this table");

                if (sheet.Id.IslandSeed != placed[0].IslandSeed)
                    return InteractionState.Refused(LaidOutFor);

                if (placed[0].Contains(sheet.Id))
                    return InteractionState.Refused("Already filed");

                return InteractionState.Ready;
            }

            verb = DefaultLabel;

            if (held != null) return InteractionState.Unavailable;

            // C8.1, finally able to be true. It refused nothing before this because the table
            // had no contents to refuse for, and "no binders on this table" would have been a
            // permanent state dressed up as a temporary one.
            if (placed.Count == 0)
                return InteractionState.Refused("Nothing on this table");

            // R6.8a. A board is the island's chart with quarters laid on it (Q4.4), so without
            // the chart there is no board — only the mounting sheet, an island-shaped blank with
            // a name in the header, which is what F-R18.4 found you could open.
            if (!HasChart)
                return InteractionState.Refused("No chart of this island");

            return InteractionState.Ready;
        }

        public override void Interact(PlayerInteractor by)
        {
            PlayerHands hands = HandsOf(by);
            ICarryable held = hands != null ? hands.Held : null;

            var binder = held as BinderView;
            if (binder != null) { Place(binder, hands); return; }

            var sheet = held as SheetView;
            if (sheet != null) { File(sheet, hands); return; }

            OpenBoard();
        }

        // ---- putting things down -----------------------------------------------------------

        /// <summary>
        /// Lays a binder on the next free anchor, binding the table if it was the first.
        ///
        /// <para>The anchor is <b>reserved before the travel starts</b>, not on arrival. The
        /// glide takes a third of a second, which is long enough for a player to pick up a
        /// second binder and aim again, and two binders sent to one anchor would land inside
        /// each other with nothing to say which was on top.</para>
        /// </summary>
        public bool Place(BinderView binder, PlayerHands hands)
        {
            Prune();

            if (binder == null || hands == null || hands.Held != (ICarryable)binder) return false;
            if (placed.Count >= Capacity) return false;
            if (placed.Count > 0 && binder.IslandSeed != placed[0].IslandSeed) return false;

            Transform anchor = AnchorAt(placed.Count);
            if (anchor == null) return false;

            // Runtime random, deliberately, and deliberately not a named sub-stream: this is
            // presentation, and R1.11's determinism contract is about what an island IS. It
            // does break with BinderSpawner.Place, which jitters from binder.Number so a
            // reported floor layout is reproducible — here the same binder put back down is
            // meant to lie differently, which is the whole point.
            float jitter = Random.Range(-rotationJitterDegrees, rotationJitterDegrees);
            Quaternion rotation = anchor.rotation * Quaternion.Euler(0f, jitter, 0f);

            bool binding = placed.Count == 0;
            placed.Add(binder);

            // C9.2 on arrival rather than on the gesture: the anchor is reserved when the binder
            // leaves the hands, and the pose the file wants exists a third of a second later.
            // On the landing and not inside Seat, because Restore seats too and a save taken
            // during a load would write a room that is halfway back.
            if (!hands.HandOver(anchor.position, rotation, item => { Seat(item, anchor); Archive.Note(); }))
            {
                placed.Remove(binder);
                return false;
            }

            if (logHandling)
                Debug.Log($"[Table] placed {binder.Summary} on anchor {placed.Count}/{Capacity}" +
                          $"{(binding ? " — table now laid out for " + BoundIslandName : "")}", this);

            return true;
        }

        /// <summary>
        /// A binder read back out of the save, onto the anchor it was lying on (§9).
        ///
        /// <para><b>Not <see cref="Place"/>.</b> Place takes the binder out of the player's
        /// hands, reserves the next free anchor and glides it there over a third of a second,
        /// rolling a fresh jitter on the way — three things that are right for a gesture and
        /// wrong for a restore, the last of them because the angle it lay at is in the file and
        /// a new roll would not be it. The pile is rebuilt in the order the file lists, so the
        /// anchor is taken as given rather than counted.</para>
        ///
        /// <para>The island check is <see cref="Add"/>-shaped and deliberate: C4.3 says a table
        /// carries one island, and a save that claimed otherwise is a save that would bind this
        /// table to whichever binder happened to be written first.</para>
        /// </summary>
        public bool Restore(BinderView binder, int anchorIndex, Vector3 position, Quaternion rotation)
        {
            Prune();

            if (binder == null) return false;
            if (placed.Count > 0 && binder.IslandSeed != placed[0].IslandSeed) return false;

            Transform anchor = AnchorAt(anchorIndex);
            if (anchor == null) return false;

            binder.transform.SetPositionAndRotation(position, rotation);
            Seat(binder, anchor);
            placed.Add(binder);

            if (logHandling)
                Debug.Log($"[Table] restored {binder.Summary} on anchor {anchorIndex + 1}/{Capacity}", this);

            return true;
        }

        /// <summary>
        /// Files a loose sheet into the first binder on the table, and destroys the paper.
        ///
        /// <para><b>The sheet is consumed, and that is the point.</b> A sheet is a pure function
        /// of its island's seed (R1.1, R1.11), so the identity is the whole of it; keeping the
        /// <c>SheetView</c> too would be the same document existing twice, which is what C4.5
        /// forbids. It travels to the binder first rather than blinking out of the hands, so
        /// filing looks like filing. This is irreversible in-world: there is no verb for taking a
        /// sheet back out, and §13 puts moving sheets between folders out of scope.</para>
        ///
        /// <para>The island check is <see cref="BinderView.Add"/>'s, not a second copy of it
        /// here. It refuses a foreign island and a sheet already inside, and it does it by
        /// returning false rather than throwing, because both are things a player will
        /// legitimately try.</para>
        /// </summary>
        public bool File(SheetView sheet, PlayerHands hands)
        {
            Prune();

            if (sheet == null || hands == null || hands.Held != (ICarryable)sheet) return false;
            if (placed.Count == 0) return false;

            BinderView binder = placed[0];
            if (!binder.Add(sheet.Id)) return false;

            Transform anchor = AnchorAt(0);
            Vector3 to = anchor != null ? anchor.position : binder.transform.position;
            Quaternion rotation = anchor != null ? anchor.rotation : binder.transform.rotation;

            if (!hands.HandOver(to, rotation, Consume))
            {
                // Put the identity back: the paper is still in the player's hands, so the
                // binder must not claim to hold it.
                binder.Remove(sheet.Id);
                return false;
            }

            if (logHandling)
                Debug.Log($"[Table] filed {sheet.Id} into {binder.Summary}", this);

            return true;
        }

        /// <summary>
        /// Takes the topmost binder back into the hands — never one from underneath.
        ///
        /// <para>LIFO because the anchors are a stack: re-packing the pile downward means
        /// binders sliding about on their own, and leaving the gap means one hanging in the air.
        /// It also keeps occupancy hole-free, which is what lets "the next free anchor" simply be
        /// the count. C4.3 means the pile is never mixed anyway.</para>
        ///
        /// <para>Taking the last one returns the table to unbound and <b>discards its board</b>
        /// (C4.4) — the arrangement, the assemblies and the parked assemblies, saved or not.
        /// Emptying a table is the deliberate act of clearing it, and it is the only one: closing
        /// the table keeps everything (§9).</para>
        /// </summary>
        public bool TakeTop(PlayerHands hands)
        {
            Prune();

            if (hands == null || !hands.IsEmpty || placed.Count == 0) return false;

            BinderView top = placed[placed.Count - 1];
            if (top == null) return false;

            // Take reparents to the hold anchor itself, worldPositionStays, so there is
            // nothing to unparent here — and nothing is mutated until it has agreed to take it.
            if (!hands.Take(top)) return false;

            placed.RemoveAt(placed.Count - 1);

            if (placed.Count == 0) Discard();

            if (logHandling)
                Debug.Log($"[Table] took {top.Summary}; {placed.Count} left" +
                          $"{(placed.Count == 0 ? " — table unbound" : "")}", this);

            return true;
        }

        /// <summary>
        /// C4.4's second half: an unbound table has no board. There is nothing to clear — a
        /// board is a view of the binders on the table (Q4.1), so the last binder leaving takes
        /// the board with it — but the save still has to hear that a binder moved.
        /// </summary>
        void Discard()
        {
            Archive.Note();
        }

        // ---- the board ---------------------------------------------------------------------

        /// <summary>
        /// Hands the room over to the board, on this table's island and through this table's
        /// binders.
        ///
        /// <para>The session is found rather than serialised: it is a scene singleton, and a
        /// reference dragged onto every table would be one more thing to get wrong per table
        /// for no choice the designer actually has. The source is built fresh per opening and
        /// holds the table rather than a list, so the cabinet sees the pile as it is now.</para>
        /// </summary>
        void OpenBoard()
        {
            TableSession session = TableSession.InScene;
            if (session == null)
            {
                Debug.LogError("[CartographyTable] No TableSession in the scene.", this);
                return;
            }

            ulong seed = BoundSeed;
            if (seed == 0) return;   // CanInteract has already said so, in words.
            if (!HasChart) return;   // R6.8a, and the same: refused above, in words.

            session.Open(seed, new BinderSheetSource(this), TableId);
        }

        // ---- anchors -------------------------------------------------------------------------

        Transform Anchors
        {
            get
            {
                if (bindingAnchors == null) bindingAnchors = transform.Find(AnchorRootName);
                return bindingAnchors;
            }
        }

        /// <summary>The <paramref name="index"/>th anchor in sibling order — which is authoring
        /// order, which on <c>PF_CartographyTable</c> is lowest first.</summary>
        Transform AnchorAt(int index)
        {
            Transform anchors = Anchors;
            if (anchors == null || index < 0 || index >= anchors.childCount) return null;
            return anchors.GetChild(index);
        }

        /// <summary>
        /// Picks up whatever is already sitting on the anchors.
        ///
        /// <para>It is allowed to be wrong for one frame, and normally is.
        /// <c>BinderSpawner.Awake</c> calls <c>ClearAll()</c> on every binder in the scene at
        /// start — binder contents are never serialised, so one that survived a scene load
        /// would hold sheets nothing remembers issuing — and <c>Destroy</c> is deferred to the
        /// end of the frame. A binder saved under an anchor is therefore alive when this runs
        /// and null shortly afterwards, whichever order the two <c>Awake</c>s happen in. That
        /// is what <see cref="Prune"/> is for, and it is why this does not try to be clever
        /// about ordering.</para>
        /// </summary>
        void Awake()
        {
            placed.Clear();

            int capacity = Capacity;
            for (int i = 0; i < capacity; i++)
            {
                Transform anchor = AnchorAt(i);
                if (anchor == null) continue;

                BinderView binder = anchor.GetComponentInChildren<BinderView>();
                if (binder != null) placed.Add(binder);
            }
        }

        /// <summary>
        /// Drops binders that have stopped existing.
        ///
        /// <para>Nulls only — deliberately not "anything no longer parented to an anchor",
        /// which looks like the stronger check and would break the feature: a binder in flight
        /// has been unparented by <c>PlayerHands.HandOver</c> and does not become a child of
        /// its anchor until it lands.</para>
        /// </summary>
        void Prune()
        {
            for (int i = placed.Count - 1; i >= 0; i--)
                if (placed[i] == null) placed.RemoveAt(i);
        }

        // ---- landings ------------------------------------------------------------------------

        /// <summary>Parents a landed binder to its anchor, so the pile travels with the table
        /// and so <c>GetComponentInParent</c> finds this table from anything sitting on
        /// it — which is what makes a placed binder able to speak for the table it is on.</summary>
        void Seat(ICarryable item, Transform anchor)
        {
            if (item == null || item.Root == null || anchor == null) return;
            item.Root.SetParent(anchor, worldPositionStays: true);
        }

        /// <summary>Destroys a filed sheet once it has reached the binder, and tells the floor
        /// it is gone so <c>SheetSpawner</c>'s record of the floor does not keep a hole where
        /// it used to be.</summary>
        void Consume(ICarryable item)
        {
            var view = item as SheetView;
            if (view == null) return;

            SheetSpawner floor = FindAnyObjectByType<SheetSpawner>();
            if (floor != null) floor.Forget(view);

            if (Application.isPlaying) Destroy(view.gameObject);
            else DestroyImmediate(view.gameObject);

            // Filing moves a sheet from the floor into a binder and destroys the paper — the one
            // gesture that changes both halves of the room at once, and irreversible in-world.
            Archive.Note();
        }

        static PlayerHands HandsOf(PlayerInteractor by)
        {
            return by != null ? by.GetComponent<PlayerHands>() : null;
        }

        string LaidOutFor { get { return "This table is laid out for " + BoundIslandName; } }

        /// <summary>Gives a freshly added component C8.1's verb, so the prompt never reads
        /// <c>Interactable</c>'s "Interact" placeholder. The serialised label is only the
        /// starting point now — <see cref="Label"/> answers from what is in the hands.</summary>
        void Reset()
        {
            SetLabel(DefaultLabel);
        }
    }
}
