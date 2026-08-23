namespace Archivist.Building.Interaction
{
    /// <summary>
    /// Anything the player can aim at and act on. The archive is made almost entirely of
    /// things you do something to — a crate you open, a rack you file into, a table you lay
    /// a sheet on — so this is deliberately the narrowest contract that covers all of them:
    /// a label to show, a gate on whether the action is available right now, and the act.
    ///
    /// <para>What an interactable does NOT decide: how the label is drawn, how far the
    /// player must be, what button is pressed, or whether anything is highlighted. All of
    /// that belongs to <see cref="PlayerInteractor"/> and the prompt, so that adding a new
    /// kind of interactable is one class and no changes anywhere else.</para>
    /// </summary>
    public interface IInteractable
    {
        /// <summary>
        /// Verb phrase shown to the player, e.g. "Create map", "File sheet".
        ///
        /// <para><b>The verb and nothing else.</b> It does not change with state. Two
        /// interactables once swapped their label for a status line while they were refusing
        /// — that was the only channel a bare <c>bool</c> <c>CanInteract</c> left them — and
        /// the cost is written up on <see cref="InteractionState"/>. Now that a refusal
        /// carries its own reason, a label that moves is a bug: the player reads the verb to
        /// know what this object <i>is</i>, and an object whose name changes when it is busy
        /// is a different object.</para>
        /// </summary>
        string Label { get; }

        /// <summary>
        /// Whether the act will happen, and why not when it will not. An unavailable
        /// interactable hides nothing — the label still shows, dimmed, now beside its reason —
        /// but the act is refused. A crate mid-generation and a full rack are both "here, but
        /// not now"; what separates them is the reason, which is why this is not a bool.
        ///
        /// <para>Takes the interactor because availability is frequently a fact about the
        /// <i>player</i>, not the object: a sheet is takeable when hands are free, and the
        /// sheet has no business knowing which hands those are. An interactable that stores a
        /// reference to the player is storing something it cannot keep — the field does not
        /// survive a domain reload or a play-mode transition, and comes back null with no
        /// symptom except a verb that quietly refuses.</para>
        /// </summary>
        InteractionState CanInteract(PlayerInteractor by);

        void Interact(PlayerInteractor by);
    }
}
