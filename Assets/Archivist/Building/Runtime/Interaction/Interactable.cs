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
        public virtual bool CanInteract { get { return isActiveAndEnabled; } }

        public abstract void Interact(PlayerInteractor by);

        /// <summary>For subclasses that change their own label as state changes.</summary>
        protected void SetLabel(string value) { label = value; }
    }
}
