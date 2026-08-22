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
        [SerializeField] Color available = new Color(0.96f, 0.95f, 0.92f, 1f);
        [SerializeField] Color unavailable = new Color(0.62f, 0.61f, 0.58f, 1f);

        void Awake() { Hide(); }

        public void Show(string verb, string bindingHint, bool canInteract)
        {
            if (label != null)
            {
                label.text = string.IsNullOrEmpty(bindingHint) ? verb : $"[{bindingHint}]   {verb}";
                label.color = canInteract ? available : unavailable;
            }
            if (group != null) group.alpha = 1f;
        }

        public void Hide()
        {
            if (group != null) group.alpha = 0f;
        }
    }
}
