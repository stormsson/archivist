using UnityEngine;
using UnityEngine.InputSystem;

namespace Archivist.Building.Interaction
{
    /// <summary>
    /// The player's half of the interaction contract: aim, reach, and the button. One ray
    /// per frame from the eye; whatever it hits is asked for an <see cref="Interactable"/>.
    ///
    /// <para>Proximity and aim are the same test on purpose — a ray with a maximum distance
    /// is both — and the ray is blocked by walls and furniture, so nothing is reachable
    /// through geometry without a special case.</para>
    /// </summary>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [Header("Reach")]
        [Tooltip("Metres. Both 'close enough' and 'aimed at' are this one test.")]
        [SerializeField] float reach = 2.5f;
        [SerializeField] LayerMask blockers = ~0;

        [Header("Wiring")]
        [SerializeField] Transform eye;
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] InteractionPrompt prompt;

        InputAction interactAction;
        Interactable current;
        string bindingHint = "E";

        /// <summary>What the player is aimed at this frame, or null.</summary>
        public Interactable Current { get { return current; } }

        public Transform Eye { get { return eye; } }

        void Awake()
        {
            var map = inputActions.FindActionMap("Player", throwIfNotFound: true);
            interactAction = map.FindAction("Interact", throwIfNotFound: true);
        }

        void OnEnable()
        {
            // FirstPersonController owns the map's lifetime; enabling the single action is
            // idempotent and keeps this component independent of the order they wake in.
            interactAction.Enable();

            string display = interactAction.GetBindingDisplayString();
            if (!string.IsNullOrEmpty(display)) bindingHint = display;
        }

        void Update()
        {
            Interactable found = Probe();

            if (found != current)
            {
                current = found;
                Refresh();
            }
            else if (current != null)
            {
                Refresh();   // CanInteract can change while aimed, e.g. a crate starts working
            }

            if (current != null && current.CanInteract && interactAction.WasPressedThisFrame())
                current.Interact(this);
        }

        Interactable Probe()
        {
            if (eye == null) return null;

            RaycastHit hit;
            bool blocked = Physics.Raycast(eye.position, eye.forward, out hit, reach,
                                           blockers, QueryTriggerInteraction.Ignore);
            if (!blocked) return null;

            // GetComponentInParent, not GetComponent: the collider that stopped the ray is
            // often a child of the thing that owns the interaction.
            return hit.collider.GetComponentInParent<Interactable>();
        }

        void Refresh()
        {
            if (prompt == null) return;

            if (current == null) prompt.Hide();
            else prompt.Show(current.Label, bindingHint, current.CanInteract);
        }

        void OnDisable()
        {
            current = null;
            if (prompt != null) prompt.Hide();
        }
    }
}
