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

        /// <summary>For subclasses that change their own label as state changes.</summary>
        protected void SetLabel(string value) { label = value; }
    }
}
