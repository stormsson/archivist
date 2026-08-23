using UnityEngine;
using Archivist.Building.Interaction;
using Archivist.Building.Sheets;

namespace Archivist.Building.Handling
{
    /// <summary>
    /// Makes a sheet on the floor something the player can take. Separate from
    /// <see cref="SheetView"/> on purpose: the view is what a sheet <i>is</i>, this is what
    /// can be <i>done</i> to it, and S7.9 keeps those apart so a sheet in a rack or on the
    /// map table can carry a different verb without touching the view at all.
    ///
    /// <para><b>It stores nothing about the player.</b> An earlier version was handed the
    /// hands at spawn time and kept them in a plain field. That field does not survive a
    /// domain reload or a play-mode transition, so it came back null with no symptom other
    /// than a verb that silently refused — the label still appeared, so it read as a dead
    /// key rather than as a broken reference. Whoever is aiming brings their own hands.</para>
    /// </summary>
    public sealed class SheetPickup : Interactable
    {
        SheetView view;

        /// <summary>Resolved lazily — Awake never runs for objects made in edit mode.</summary>
        SheetView View
        {
            get
            {
                if (view == null) view = GetComponent<SheetView>();
                return view;
            }
        }

        public override string Label { get { return "Take"; } }

        /// <summary>
        /// S7.6: full hands make this "here, but not now" — the label dims and the act is
        /// refused. No message, no error state.
        /// </summary>
        public override bool CanInteract(PlayerInteractor by)
        {
            PlayerHands hands = HandsOf(by);
            return isActiveAndEnabled && hands != null && hands.IsEmpty;
        }

        public override void Interact(PlayerInteractor by)
        {
            PlayerHands hands = HandsOf(by);
            if (hands == null || View == null) return;

            hands.Take(View);
        }

        static PlayerHands HandsOf(PlayerInteractor by)
        {
            return by != null ? by.GetComponent<PlayerHands>() : null;
        }
    }
}
