using UnityEngine;
using Archivist.Building.Binders;
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
    /// <para><b>The verb does not say how full it is.</b> A binder knows its own count and
    /// will happily give it — <see cref="BinderView.SheetCount"/>,
    /// <see cref="BinderView.Summary"/> — but the label names what the key does and nothing
    /// else (<see cref="IInteractable.Label"/>). A verb that reads "Take (5 sheets)" is a verb
    /// that changes when the object does, and the player reads the verb to know what the
    /// object <i>is</i>. Where the count belongs is a spine label on the model or a line in
    /// the binder's own screen; both are somebody else's slice, and neither is the prompt.</para>
    ///
    /// <para>It stores nothing about the player: whoever aims at it brings their own hands.
    /// See <see cref="SheetPickup"/> for what that cost to learn.</para>
    ///
    /// <para><b>A binder on a table stops being a thing you take and becomes the table's
    /// face.</b> <c>PlayerInteractor</c> resolves whatever its ray hits with
    /// <c>GetComponentInParent&lt;Interactable&gt;()</c>, so a binder sitting on an anchor
    /// shadows the table underneath it — and the pile is the obvious place to aim, being the
    /// only part of a table whose state the player can actually see. Left alone, that one spot
    /// would be the one spot that refused a binder. So when this finds a
    /// <see cref="CartographyTable"/> among its parents it hands the verb over: full hands get
    /// the table's own answer ("Add to table", "File sheet", and the table's refusals), and
    /// empty hands still take a binder — but the table's topmost one, through
    /// <see cref="CartographyTable.TakeTop"/>, never necessarily this one. You cannot pull the
    /// bottom folder out of a pile, and one component owning that keypress is what keeps the
    /// stack from growing holes.</para>
    ///
    /// <para>The parent lookup is deliberately <b>not cached</b>. A binder is unparented for
    /// the length of its glide onto the table and only becomes an anchor's child when it
    /// lands, so a reference resolved once would be an answer about where it used to be.</para>
    ///
    /// <para><b>The verb is the serialised field, not a constant.</b> <c>SheetPickup</c> hard-codes
    /// "Take" because nothing ever puts a sheet in the world by hand — the spawner builds every
    /// one at runtime, so there is no Inspector to type a verb into. A binder is an authored
    /// prefab sitting in the Project window, which means the wording is an ordinary thing to
    /// adjust while looking at it. Same pattern as <c>CartographyTable</c>: <see cref="Reset"/>
    /// supplies the default, and whatever is in the field afterwards wins.</para>
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

            return hands.IsEmpty ? InteractionState.Ready : InteractionState.Unavailable;
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
