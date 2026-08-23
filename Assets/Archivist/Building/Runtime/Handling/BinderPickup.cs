using UnityEngine;
using Archivist.Building.Binders;
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

        /// <summary>S7.6: full hands make this "here, but not now" — the label dims, silently.
        /// The player is holding the thing that is stopping them, so a caption saying so would
        /// tell them what their own hands already do.</summary>
        public override InteractionState CanInteract(PlayerInteractor by)
        {
            PlayerHands hands = HandsOf(by);
            return isActiveAndEnabled && hands != null && hands.IsEmpty
                ? InteractionState.Ready
                : InteractionState.Unavailable;
        }

        public override void Interact(PlayerInteractor by)
        {
            PlayerHands hands = HandsOf(by);
            if (hands == null || View == null) return;

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
