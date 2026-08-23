namespace Archivist.Building.Interaction
{
    /// <summary>
    /// Whether an interactable will act right now, and — when it will not — the short phrase
    /// that says why. The answer to <see cref="IInteractable.CanInteract"/>.
    ///
    /// <para><b>This replaced a bare <c>bool</c>, and the timing was the whole decision.</b>
    /// A bool leaves an interactable that wants to explain a refusal nowhere to put the
    /// explanation, so it encodes one in its label instead — <c>MapCrate</c> did exactly that
    /// (<c>busy ? busyLabel : base.Label</c>), which made "the crate is working" and "your
    /// hands are full" the same state as far as the UI could tell: dim the text. That is fine
    /// at one class and it is not fine at N, because each class invents its own encoding and a
    /// later UI wanting a spinner, an icon or a reason has to unpick all of them. The seam was
    /// therefore left alone deliberately until a <i>second</i> interactable needed to refuse
    /// for a reason. <c>CartographyTable</c> ("No folders on this table", C8.1) is that
    /// second one, so the widening happened here rather than earlier — one class to convert,
    /// not all of them.</para>
    ///
    /// <para><b>Refusing silently stays possible, and is the default.</b> Not every "no" has
    /// something worth saying: a disabled component, or a sheet that will not be taken because
    /// the player's hands are full (S7.6 asks for no message there, only a dimmed verb). So
    /// <see cref="Reason"/> is allowed to be null, and <c>default(InteractionState)</c> is
    /// exactly <see cref="Unavailable"/> — an unavailable, wordless refusal. A struct whose
    /// zero value means "available" would let a forgotten return path quietly grant an
    /// interaction; this way the cheapest mistake is the safe one.</para>
    ///
    /// <para>A struct rather than a class so an answer produced every frame by
    /// <c>PlayerInteractor</c>, for every aimed-at object, allocates nothing.</para>
    /// </summary>
    public readonly struct InteractionState
    {
        /// <summary>True when the act will actually happen if the key is pressed.</summary>
        public readonly bool Available;

        /// <summary>Why the act is refused: a short phrase in the player's language, shown
        /// beside the verb. Null when <see cref="Available"/>, and null for a refusal with
        /// nothing to explain. Never a sentence — it shares one line with the verb.</summary>
        public readonly string Reason;

        InteractionState(bool available, string reason)
        {
            Available = available;
            Reason = reason;
        }

        /// <summary>Yes.</summary>
        public static InteractionState Ready { get { return new InteractionState(true, null); } }

        /// <summary>No, with nothing to say about it — the label dims and that is the whole
        /// message. What the old <c>false</c> meant.</summary>
        public static InteractionState Unavailable
        {
            get { return new InteractionState(false, null); }
        }

        /// <summary>No, and here is why. The reason reaches the player, so write it as a
        /// phrase the archive would use, not as a diagnostic.</summary>
        public static InteractionState Refused(string reason)
        {
            return new InteractionState(false, reason);
        }

        public override string ToString()
        {
            if (Available) return "available";
            return string.IsNullOrEmpty(Reason) ? "refused" : "refused: " + Reason;
        }
    }
}
