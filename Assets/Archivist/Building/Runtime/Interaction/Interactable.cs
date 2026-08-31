using UnityEngine;

namespace Archivist.Building.Interaction
{
    /// <summary>
    /// Base for interactable behaviours. Exists so <see cref="PlayerInteractor"/> can find one
    /// with a single <c>GetComponentInParent</c> — a collider anywhere in an object's
    /// hierarchy resolves to the one component that owns the interaction, which is what lets
    /// a rack have thirty slot colliders and still be one interactable.
    /// </summary>
    public abstract class Interactable : MonoBehaviour, IInteractable
    {
        [SerializeField] string label = "Interact";

        public virtual string Label { get { return label; } }

        /// <summary>Available whenever the component is awake. Refuses wordlessly when it is
        /// not: a disabled behaviour is not a state the player did anything to reach, so there
        /// is nothing to tell them about it.</summary>
        public virtual InteractionState CanInteract(PlayerInteractor by)
        {
            return isActiveAndEnabled ? InteractionState.Ready : InteractionState.Unavailable;
        }

        public abstract void Interact(PlayerInteractor by);

        /// <summary>
        /// The player's aim has landed on this, and <see cref="Unaimed"/> when it leaves. Both
        /// do nothing by default, so an interactable that wants no feedback writes nothing.
        ///
        /// <para><b>Pushed by <see cref="PlayerInteractor"/>, which is the division
        /// <see cref="IInteractable"/> already draws</b> — it owns "how far", "which button" and
        /// <i>whether</i> anything is highlighted, because it is the only thing that knows where
        /// the player is looking. What the highlight looks like cannot live there: a shelf slot
        /// glows, a crate might not, and an interactor that knew the difference would grow a
        /// branch per kind of furniture.</para>
        ///
        /// <para><b>Not a substitute for <see cref="CanInteract"/>.</b> The pair fires on the
        /// transition only, so anything that changes <i>while</i> the player keeps aiming — hands
        /// filling, a slot being emptied — has to be answered where it is noticed, which is
        /// <see cref="CanInteract"/> running every frame on the one thing being aimed at.</para>
        /// </summary>
        public virtual void Aimed(PlayerInteractor by) { }

        public virtual void Unaimed() { }

        /// <summary>For subclasses that change their own label as state changes.</summary>
        protected void SetLabel(string value) { label = value; }
    }
}
