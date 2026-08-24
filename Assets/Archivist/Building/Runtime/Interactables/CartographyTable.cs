using System.Collections.Generic;
using UnityEngine;
using Archivist.Building.Binders;
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
    /// <para><b>This class used to be one line, deliberately, and no longer is.</b> The record
    /// of why it was emptied is worth keeping, because only half of it has been reversed. A
    /// table id and an island binding were both built here and removed: not wrong, early.
    /// Nothing read the id, because <c>BoardStore</c> was wired to nothing; nothing bound an
    /// island, because the folder model that would do the binding did not exist (§13). What
    /// they cost in the meantime was a serialised identity that had to be minted exactly once,
    /// which turned out to be genuinely hard —
    /// <c>PrefabUtility.LoadPrefabContents</c> loads a prefab into a <i>preview scene</i>,
    /// where <c>IsPartOfPrefabAsset</c> is false and <c>GetCurrentPrefabStage</c> is null, so
    /// the id was minted into the prefab asset itself, twice, and every instance inherited it
    /// and shared one board; and <c>OnValidate</c> never fired at all on an instance created
    /// through scripting, leaving a table with no id.</para>
    ///
    /// <para><b>The binding is back. The identity is not, and should not be.</b> A table is
    /// bound by <i>what is lying on it</i> — the island named by the first binder on its
    /// anchors — which is a fact about the room that needs no minting, survives no domain
    /// reload because it was never serialised, and cannot be shared between two tables by
    /// accident. C4.1–C4.4 are enforced here now: unbound tables accept any binder, the first
    /// binder fixes the island, a bound table takes only that island, and taking the last
    /// binder off returns it to unbound. If a serialised <c>tableId</c> is ever needed again
    /// (it will be, the day board state is persisted), the lesson above still stands: it needs
    /// <c>EditorSceneManager.IsPreviewSceneObject</c> alongside the prefab-asset and
    /// prefab-stage checks, and a manual mint as an escape hatch, because the automatic paths
    /// fail silently and the symptom — two tables quietly sharing a board — does not look like
    /// an identity bug.</para>
    ///
    /// <para><b>What the verb is depends on what is in the player's hands</b>, and this is the
    /// one interactable where that is true of the <i>label</i> and not only of availability.
    /// Holding a binder, the key adds it to the table; holding a loose sheet, it files that
    /// sheet into the binder on the table; empty-handed, it opens the board. Three verbs, one
    /// key, because they are three ways of saying the same thing to the same object. See
    /// <see cref="Label"/> for the one piece of coupling this costs.</para>
    ///
    /// <para><b>The board is opened on the table's own island, through its own binders.</b>
    /// <c>TableSession.Open</c> takes an <see cref="ISheetSource"/> so that the cabinet's
    /// answer belongs to whoever opened it (§4.3). Before this, the table called
    /// <c>OpenCurrentIsland</c> and the cabinet was fed by the ledger — every sheet ever
    /// <i>issued</i> of the last island the room drew. That looked correct and was not: it
    /// agreed with the binder in the player's hands only for the binder from the most recent
    /// crate opening, and even then listed one sheet too many, because <c>MapCrate</c>'s
    /// <c>looseDebugSheet</c> issues a sixth sheet onto the floor.</para>
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
        /// <para><b>Why that is safe, and what would break it.</b> <c>Label</c> has no
        /// <c>PlayerInteractor</c> to ask, and finding the hands from here would be a
        /// scene-wide search every frame the player is aimed at a table.
        /// <c>PlayerInteractor.Refresh</c> calls <c>CanInteract</c> and then <c>Label</c>, in
        /// that order, every frame — so the answer is never older than the state it describes.
        /// If that order ever reverses the label lags by one frame; it does not lie about
        /// availability, which is <see cref="InteractionState"/>'s job and stays entirely in
        /// <see cref="CanInteract"/>. That separation is why C8.1 took <c>MapCrate</c>'s busy
        /// state <i>out</i> of its label, and it is kept here: this field names what the key
        /// does, never whether it can be pressed.</para>
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

            if (!hands.HandOver(anchor.position, rotation, item => Seat(item, anchor)))
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
        /// Files a loose sheet into the first binder on the table, and destroys the paper.
        ///
        /// <para><b>The sheet is consumed, and that is the point.</b> A sheet is a pure
        /// function of its island's seed (R1.1, R1.11), so the identity is the whole of it —
        /// keeping the <c>SheetView</c> as well would be the same document existing twice, once
        /// as paper and once as a record, which is what C4.5 exists to forbid. It travels to
        /// the binder first rather than blinking out of the hands, so filing looks like filing.
        /// Worth knowing: this is irreversible in-world. There is no verb for taking a sheet
        /// back out of a binder, and §13 puts moving sheets between folders out of scope.</para>
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
        /// <para>LIFO because the anchors are a stack: you cannot pull the bottom folder out of
        /// a pile, and the alternatives are worse than the restriction. Re-packing the pile
        /// downward means binders sliding about on their own; leaving the gap means one hanging
        /// in the air. It also keeps occupancy hole-free, which is what lets "the next free
        /// anchor" simply be the count. C4.3 means the pile is never mixed anyway, so there is
        /// nothing in it worth singling out.</para>
        ///
        /// <para>Taking the last one returns the table to unbound (C4.4). C4.4 also says the
        /// board state is discarded, which costs nothing today and must be honoured the day it
        /// does: <c>BoardStore</c> is still wired to nothing, so no board state is persisted at
        /// all and closing the table already loses it.</para>
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

            if (logHandling)
                Debug.Log($"[Table] took {top.Summary}; {placed.Count} left" +
                          $"{(placed.Count == 0 ? " — table unbound" : "")}", this);

            return true;
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

            session.Open(seed, new BinderSheetSource(this));
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
