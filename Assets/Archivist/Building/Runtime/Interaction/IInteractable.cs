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
        /// <summary>Verb phrase shown to the player, e.g. "Create map", "File sheet".</summary>
        string Label { get; }

        /// <summary>
        /// False hides nothing — the label still shows, greyed — but the act is refused.
        /// A crate mid-generation and a full rack are both "here, but not now".
        /// </summary>
        bool CanInteract { get; }

        void Interact(PlayerInteractor by);
    }
}
