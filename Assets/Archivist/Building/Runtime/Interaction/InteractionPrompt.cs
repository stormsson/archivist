using UnityEngine;
using UnityEngine.UI;

namespace Archivist.Building.Interaction
{
    /// <summary>
    /// Draws the aim label. Kept behind its own component so the presentation can change —
    /// worldspace label, diegetic, nothing at all — without any interactable knowing.
    ///
    /// <para>T2 forbids counters and scores; a verb under the reticle is not one. This is the
    /// only screen text the POC has.</para>
    /// </summary>
    public sealed class InteractionPrompt : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] Text label;
        // Far apart on purpose. The first pair differed by about a third of a stop, which
        // is legible in a colour picker and not on a screen — a whole debugging session went
        // into arguing about which state was showing. Available is white; unavailable is
        // nearly black. Nobody should ever have to measure a pixel to tell these apart.
        [SerializeField] Color available = new Color(1f, 1f, 1f, 1f);
        [SerializeField] Color unavailable = new Color(0.16f, 0.16f, 0.15f, 1f);

        void Awake() { Hide(); }

        /// <summary>
        /// Draws one aimed-at object. <paramref name="reason"/> is the refusal's own words —
        /// null when available, and null for a refusal with nothing to explain — and it is
        /// appended to the verb rather than replacing it. Replacing it is what the old
        /// bool-shaped contract forced interactables to do for themselves (see
        /// <see cref="InteractionState"/>), and the verb is the half the player needs kept:
        /// "Working…" alone does not say what is working.
        /// </summary>
        public void Show(string verb, string bindingHint, bool canInteract, string reason)
        {
            if (label != null)
            {
                string line = string.IsNullOrEmpty(bindingHint) ? verb : $"[{bindingHint}]   {verb}";
                if (!canInteract && !string.IsNullOrEmpty(reason)) line += $" — {reason}";

                label.text = line;
                label.color = canInteract ? available : unavailable;
            }
            if (group != null) group.alpha = 1f;
        }

        public void Hide()
        {
            if (group != null) group.alpha = 0f;

            // Cleared, not merely faded. An invisible label still holding the last verb
            // reads as truth to anything that inspects it later — during one debugging
            // session it claimed the player was aiming at the crate while they were aiming
            // at a sheet.
            if (label != null) label.text = "";
        }
    }
}
