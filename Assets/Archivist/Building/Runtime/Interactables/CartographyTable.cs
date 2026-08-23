using UnityEngine;
using Archivist.Building.Interaction;
using Archivist.Building.Table;

namespace Archivist.Building.Interactables
{
    /// <summary>
    /// The map table: the diegetic way into the board view (C8.1). Aim at it, press the key,
    /// and the map composition UI opens. Nobody asked for this — it is the game's second,
    /// optional activity (R6.1) — which is why it is a thing in the room you may walk past
    /// rather than a screen the game puts in front of you.
    ///
    /// <para><b>Deliberately just a verb.</b> A table id, an island binding, and the soft
    /// bind/unbind rules of C4.1–C4.4 were all built here and then <b>removed</b>. They were
    /// not wrong, they were early: nothing yet reads a table id, because <c>BoardStore</c> is
    /// not wired to anything, and nothing yet binds an island, because the folder model that
    /// would do the binding does not exist (§13). What they bought in the meantime was a
    /// serialized identity that had to be minted exactly once and never again — and that
    /// turned out to be genuinely hard to get right in the editor. Two guards were not enough:
    /// <c>PrefabUtility.LoadPrefabContents</c> loads a prefab into a <i>preview scene</i>,
    /// where <c>IsPartOfPrefabAsset</c> is false and <c>GetCurrentPrefabStage</c> is null, so
    /// the id was minted into the prefab asset itself — twice — and every instance would have
    /// inherited it and shared one board. Separately, <c>OnValidate</c> never fired at all on
    /// an instance created through scripting, leaving a table with no id.</para>
    ///
    /// <para><b>The lesson worth keeping, for whoever adds identity back:</b> a serialized
    /// identity minted in <c>OnValidate</c> needs <c>EditorSceneManager.IsPreviewSceneObject</c>
    /// as well as the prefab-asset and prefab-stage checks, and it needs a manual mint as an
    /// escape hatch, because the automatic paths fail silently and the symptom — two tables
    /// quietly sharing a board — does not look like an identity bug. The generator that made
    /// those ids readable (<c>drift-salt-glen-berg-12</c>, so two of them can be told apart at
    /// a glance in a log or a save file) is worth restoring with it.</para>
    ///
    /// <para><b><see cref="Interact"/> is one call into <see cref="TableSession"/>, and that
    /// is the whole of it.</b> It was a stub until slice S2, which built the mode switch of
    /// §8.2 — disable <c>FirstPersonController</c>, <c>PlayerInteractor</c> and
    /// <c>PlayerHands</c> as components (C8.4, C8.5), enable the <c>Table</c> and <c>UI</c>
    /// maps, and let the controller's own <c>OnEnable</c>/<c>OnDisable</c> go on owning the
    /// cursor (C8.6). S2 built it <i>there</i> and not here for the reason the stub was
    /// waiting on: that is a change about the room, not about this table, and half of it
    /// built here would be a second cursor owner and a second place that knows how the player
    /// is switched off. There will eventually be more than one table in the archive, and only
    /// ever one room.</para>
    ///
    /// <para>Which island opens is likewise not this class's to say: the session asks the
    /// generator for the island the archive last drew (C8.2). A table that named its own
    /// island would need the binding described above, and the paragraph above is the record
    /// of why that binding is not here yet.</para>
    /// </summary>
    public sealed class CartographyTable : Interactable
    {
        /// <summary>C8.1's verb, and the whole of it — availability is said by
        /// <see cref="CanInteract"/>, never by the label. That separation is the point of
        /// <see cref="InteractionState"/>; see <c>notes.md</c> on the pattern it replaced.</summary>
        public const string DefaultLabel = "Open Cartography table";

        /// <summary>
        /// Always available while the component is enabled.
        ///
        /// <para>It refuses nothing today because there is nothing yet to refuse for: the
        /// table has no contents, so "no folders on this table" would be a permanent state
        /// dressed up as a temporary one. <c>MapCrate</c> still refuses while it works, so
        /// <see cref="InteractionState"/>'s reason channel is exercised and does not rot.</para>
        /// </summary>
        public override InteractionState CanInteract(PlayerInteractor by)
        {
            return base.CanInteract(by);
        }

        public override void Interact(PlayerInteractor by)
        {
            // Found rather than serialised: the session is a scene singleton, and a reference
            // dragged onto every table would be one more thing to get wrong per table for no
            // choice the designer actually has.
            TableSession session = TableSession.InScene;
            if (session == null)
            {
                Debug.LogError("[CartographyTable] No TableSession in the scene.", this);
                return;
            }

            session.OpenCurrentIsland();
        }

        /// <summary>Gives a freshly added component C8.1's verb, so the prompt never reads
        /// <c>Interactable</c>'s "Interact" placeholder. Through <c>SetLabel</c> because the
        /// base keeps the label in a serialised field a subclass cannot re-default — and the
        /// field stays the authority afterwards, so a table that has been given a different
        /// verb keeps it.</summary>
        void Reset()
        {
            SetLabel(DefaultLabel);
        }
    }
}
