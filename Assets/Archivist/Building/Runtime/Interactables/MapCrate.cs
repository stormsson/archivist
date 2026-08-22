using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Archivist.Building.Collection;
using Archivist.Building.Interaction;
using Archivist.Building.Sheets;
using Archivist.Generation;
using Archivist.Generation.Sheets;
using Archivist.Render;

namespace Archivist.Building.Interactables
{
    /// <summary>
    /// The crate. Aim at it, press the key, and an island comes into existence — unseen,
    /// unmapped, and never drawn as a whole — followed by a handful of its sheets on the
    /// floor.
    ///
    /// <para><b>The island stays hidden on purpose, and not merely for the POC.</b> There is
    /// no world geometry above an island and no spatial relationship between islands (§3.1);
    /// the sea they sit in is never drawn. The island exists as vector data that sheets are
    /// cut from, and the player's only access to it is paper. Showing it would answer the
    /// question the whole game is about.</para>
    /// </summary>
    public sealed class MapCrate : Interactable
    {
        [Header("Wiring")]
        [SerializeField] CollectionService collection;
        [SerializeField] SheetSpawner spawner;
        [Tooltip("Where the pile lands. Falls back to this transform.")]
        [SerializeField] Transform dropAnchor;

        [Header("Contents")]
        [SerializeField] int sheetsPerOpening = 5;

        [Tooltip("Render resolution in pixels per millimetre of paper. RenderTuning's own " +
                 "default is 2.7 (~68 dpi), which is in-hand quality; a sheet read from " +
                 "standing height needs far less.")]
        [SerializeField] double pixelsPerPaperMm = 1.2;

        [Tooltip("On: every opening is a new island. Off: keep drawing from the last one, " +
                 "which is what makes the ledger's exclusion visible — sheets never repeat.")]
        [SerializeField] bool openNewIslandEachTime = true;

        [Header("Labels")]
        [SerializeField] string busyLabel = "Working...";

        bool busy;

        public override string Label { get { return busy ? busyLabel : base.Label; } }
        public override bool CanInteract { get { return !busy && isActiveAndEnabled; } }

        public override void Interact(PlayerInteractor by)
        {
            if (busy) return;
            if (collection == null || spawner == null)
            {
                Debug.LogError("[MapCrate] Not wired to a CollectionService and SheetSpawner.", this);
                return;
            }
            StartCoroutine(Open());
        }

        IEnumerator Open()
        {
            busy = true;

            ulong seed = (openNewIslandEachTime || collection.LastIslandSeed == 0)
                ? collection.ReserveNextIslandSeed()
                : collection.LastIslandSeed;

            // Snapshot on the main thread, before any work: the picker must not read a
            // structure the main thread can write.
            HashSet<SheetId> issued = collection.Ledger.Snapshot(seed);
            int drawSeed = unchecked((int)(seed ^ ((ulong)issued.Count * 0x9E3779B97F4A7C15UL)));

            int count = sheetsPerOpening;
            double ppmm = pixelsPerPaperMm;

            // Generation (~0.5 s) and five renders are pure, engine-free C# — Archivist.Generation
            // may not even reference UnityEngine — so they belong on a worker thread. Doing
            // them inline would freeze the room for a second or more on every interaction,
            // which is the one thing T5's "quiet" cannot survive.
            Task<List<SheetRender>> job = Task.Run(() => Draw(seed, issued, count, drawSeed, ppmm));

            while (!job.IsCompleted) yield return null;

            if (job.IsFaulted)
            {
                Debug.LogException(job.Exception, this);
                busy = false;
                yield break;
            }

            List<SheetRender> batch = job.Result;
            if (batch.Count == 0)
            {
                // R1.8/R2.9: an island running out of undrawn sheets is a legitimate state.
                Debug.Log($"[MapCrate] Island {seed:X16} has no unissued sheets left.", this);
                busy = false;
                yield break;
            }

            Transform anchor = dropAnchor != null ? dropAnchor : transform;

            for (int i = 0; i < batch.Count; i++)
            {
                SheetRender render = batch[i];

                // R2.10 enforced here and nowhere else: a sheet that is already out is never
                // issued twice, even if a picker somewhere later gets it wrong.
                if (!collection.Ledger.MarkIssued(render.Id)) continue;

                spawner.Place(render, i, batch.Count, anchor);
                yield return null;   // one texture upload per frame
            }

            Debug.Log($"[MapCrate] {batch[0].IslandName} ({seed:X16}) — issued {batch.Count} sheets, " +
                      $"{collection.Ledger.IssuedCount(seed)} out in total.", this);

            busy = false;
        }

        /// <summary>
        /// Worker-thread half. Touches no engine API: an island, a pick, and N rasters.
        ///
        /// <para>Public because it is the whole pipeline minus the keypress, and the editor
        /// validator drives it directly — proving generation, picking and rendering without
        /// entering play mode.</para>
        /// </summary>
        public static List<SheetRender> Draw(ulong islandSeed, HashSet<SheetId> issued, int count,
                                      int drawSeed, double pixelsPerPaperMm)
        {
            Island island = Island.FromSeed(islandSeed);
            List<Sheet> picks = SheetPicker.PickUnissued(island, count, issued, drawSeed);

            var rendered = new List<SheetRender>(picks.Count);
            for (int i = 0; i < picks.Count; i++)
            {
                Sheet sheet = picks[i];
                RenderRequest request = RenderRequest.ForSheet(sheet, pixelsPerPaperMm);
                ImageBuffer image = IslandRenderer.Render(island, request);
                rendered.Add(new SheetRender(SheetId.Of(sheet), sheet, island.Name, image));
            }
            return rendered;
        }
    }
}
