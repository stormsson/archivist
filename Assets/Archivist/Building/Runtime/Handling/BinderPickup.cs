using UnityEngine;
using Archivist.Building.Binders;
using Archivist.Building.Collection;
using Archivist.Building.Interactables;
using Archivist.Building.Interaction;

namespace Archivist.Building.Handling
{
    /// <summary>
    /// Makes a binder on the floor something the player can take. The same shape as
    /// <see cref="SheetPickup"/> and for the same reason (S7.9): what a binder <i>is</i> lives
    /// on <see cref="BinderView"/>, what can be <i>done</i> to it lives here, so a binder in a
    /// rack or on the map table can carry a different verb without touching the view.
    ///
    /// <para><b>The verb does not say how full it is.</b> A binder knows its own count
    /// (<see cref="BinderView.SheetCount"/>), but the label names what the key does and nothing
    /// else. "Take (5 sheets)" is a verb that changes when the object does, and the player reads
    /// the verb to know what the object <i>is</i>. The count belongs on a spine label or in the
    /// binder's own screen, not in the prompt.</para>
    ///
    /// <para>It stores nothing about the player: whoever aims at it brings their own hands.
    /// See <see cref="SheetPickup"/> for what that cost to learn.</para>
    ///
    /// <para><b>A binder on a table stops being a thing you take and becomes the table's
    /// face.</b> <c>PlayerInteractor</c> resolves a hit with
    /// <c>GetComponentInParent&lt;Interactable&gt;()</c>, so a binder on an anchor shadows the
    /// table underneath — and the pile is the obvious place to aim, being the only part of a
    /// table whose state the player can see, so that one spot would be the one spot that refused
    /// a binder. Finding a <see cref="CartographyTable"/> among its parents, this hands the verb
    /// over: full hands get the table's answer, empty hands take the table's <i>topmost</i>
    /// binder through <see cref="CartographyTable.TakeTop"/>, never necessarily this one.</para>
    ///
    /// <para>The parent lookup is deliberately <b>not cached</b>. A binder is unparented for
    /// the length of its glide onto the table and only becomes an anchor's child when it
    /// lands, so a reference resolved once would be an answer about where it used to be.</para>
    ///
    /// <para><b>Merging is rack work, so it lives here and not on the table</b> (Q3.3).
    /// Holding a binder and aiming at another of the same island pours this one into the one in
    /// the hands and destroys the empty folder. It is <b>silent</b> — Q3.4 makes merging
    /// tidiness and nothing else, so nothing counts it, nothing rewards it, and nothing anywhere
    /// suggests it. A player who never merges is playing the game as designed; the shelves are
    /// simply longer.</para>
    ///
    /// <para><b>Which way round, and why this way.</b> The binder on the floor goes into the
    /// binder in the hands, not the other way about. That keeps the hands full afterwards — one
    /// gesture, one object still held, nothing to pick back up — and it means the surviving
    /// number is the one the player chose to carry over.</para>
    ///
    /// <para><b>The verb is a serialised field, not a constant.</b> <c>SheetPickup</c> hard-codes
    /// "Take" because the spawner builds every sheet at runtime and there is no Inspector to type
    /// into; a binder is an authored prefab, so the wording is an ordinary thing to adjust while
    /// looking at it. <see cref="Reset"/> supplies the default and the field wins afterwards.
    /// </para>
    /// </summary>
    public sealed class BinderPickup : Interactable
    {
        BinderView view;

        /// <summary>Resolved lazily — Awake never runs for objects made in edit mode.</summary>
        BinderView View
        {
            get
            {
                if (view == null) view = GetComponent<BinderView>();
                return view;
            }
        }

        /// <summary>What a freshly added component says, before anyone changes it.</summary>
        public const string DefaultLabel = "Take binder";

        /// <summary>Q3.3's verb. Names the act and not its result: "Merge" is what the key does,
        /// where "Combine 4 sheets" would be a label that changes as the object does.</summary>
        public const string MergeLabel = "Merge binders";

        // Set by CanInteract, read by Label, for the same reason speakingForTable is.
        bool merging;

        /// <summary>The table this binder is lying on, or null while it is on the floor or in
        /// flight. Asked every time — see the class comment on why it is not cached.</summary>
        CartographyTable OnTable { get { return GetComponentInParent<CartographyTable>(); } }

        // Set by CanInteract, read by Label, exactly as CartographyTable does it and for the
        // same reason: PlayerInteractor.Refresh asks CanInteract first and Label second, and
        // Label has no player to ask.
        bool speakingForTable;

        /// <summary>The table's verb while this binder is on one, so the prompt over a pile
        /// names what the key will actually do.</summary>
        public override string Label
        {
            get
            {
                if (merging) return MergeLabel;
                if (!speakingForTable) return base.Label;

                CartographyTable table = OnTable;
                return table != null ? table.Label : base.Label;
            }
        }

        /// <summary>S7.6: full hands make this "here, but not now" — the label dims, silently.
        /// The player is holding the thing that is stopping them, so a caption saying so would
        /// tell them what their own hands already do. That holds only on the floor; on a table,
        /// full hands are the interesting case and the table answers them.</summary>
        public override InteractionState CanInteract(PlayerInteractor by)
        {
            speakingForTable = false;
            merging = false;
            if (!isActiveAndEnabled) return InteractionState.Unavailable;

            PlayerHands hands = HandsOf(by);
            if (hands == null) return InteractionState.Unavailable;

            CartographyTable table = OnTable;
            if (table != null)
            {
                if (!hands.IsEmpty)
                {
                    speakingForTable = true;
                    return table.CanInteract(by);
                }

                // Empty-handed on a table: still "Take binder", but the table decides which.
                return table.BinderCount > 0
                    ? InteractionState.Ready
                    : InteractionState.Unavailable;
            }

            if (hands.IsEmpty) return InteractionState.Ready;

            // Full hands, on the floor: a binder of the same island is a merge, and anything
            // else is the ordinary "here, but not now" dim. Deliberately NOT a refusal with a
            // reason — a binder of another island is not a mistake being corrected, it is two
            // unrelated objects, and the game does not comment on those.
            if (Held(hands) != null && CanMergeInto(Held(hands)))
            {
                merging = true;
                return InteractionState.Ready;
            }

            return InteractionState.Unavailable;
        }

        /// <summary>The binder in the hands, or null if they are empty or holding paper.</summary>
        static BinderView Held(PlayerHands hands)
        {
            return hands != null ? hands.Held as BinderView : null;
        }

        /// <summary>Same island, not the same object, and this one is on the floor rather than
        /// on a table — a table's pile answers for itself.</summary>
        bool CanMergeInto(BinderView target)
        {
            return View != null && target != null
                && !ReferenceEquals(target, View)
                && target.IslandSeed == View.IslandSeed;
        }

        public override void Interact(PlayerInteractor by)
        {
            PlayerHands hands = HandsOf(by);
            if (hands == null) return;

            CartographyTable table = OnTable;
            if (table != null)
            {
                if (!hands.IsEmpty) { table.Interact(by); return; }

                table.TakeTop(hands);
                return;
            }

            if (View == null) return;

            BinderView held = Held(hands);
            if (held != null)
            {
                if (!CanMergeInto(held)) return;
                if (!held.Merge(View)) return;

                // The emptied folder goes. It is not a binder any more — it names an island and
                // holds nothing, which is indistinguishable from a fresh one, and leaving it on
                // the floor would mean merging a pile of five left five objects behind.
                Destroy(gameObject);
                Archive.Note();

                Debug.Log($"[Binder] merged into {held.Describe()}", this);
                return;
            }

            if (hands.Take(View))
                Debug.Log($"[Binder] took {View.Describe()}", this);
        }

        static PlayerHands HandsOf(PlayerInteractor by)
        {
            return by != null ? by.GetComponent<PlayerHands>() : null;
        }

        /// <summary>Gives a freshly added component its verb, so the prompt never reads
        /// <c>Interactable</c>'s "Interact" placeholder. Through <c>SetLabel</c> because the
        /// base keeps the label in a serialised field a subclass cannot re-default.</summary>
        void Reset()
        {
            SetLabel(DefaultLabel);
        }
    }
}
