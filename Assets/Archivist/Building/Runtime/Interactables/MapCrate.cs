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
        [SerializeField] IslandGenerator generator;
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
        public override bool CanInteract(PlayerInteractor by) { return !busy && isActiveAndEnabled; }

        public override void Interact(PlayerInteractor by)
        {
            if (busy) return;
            if (generator == null || spawner == null)
            {
                Debug.LogError("[MapCrate] Not wired to an IslandGenerator and SheetSpawner.", this);
                return;
            }
            StartCoroutine(Open());
        }

        IEnumerator Open()
        {
            busy = true;

            ulong seed = (openNewIslandEachTime || generator.LastIslandSeed == 0)
                ? generator.ReserveNextIslandSeed()
                : generator.LastIslandSeed;

            // Snapshot on the main thread, before any work: the picker must not read a
            // structure the main thread can write.
            HashSet<SheetId> issued = generator.Ledger.Snapshot(seed);
            int drawSeed = unchecked((int)(seed ^ ((ulong)issued.Count * 0x9E3779B97F4A7C15UL)));

            int count = sheetsPerOpening;
            double ppmm = pixelsPerPaperMm;

            // Generation and five renders are pure, engine-free C# — Archivist.Generation may
            // not even reference UnityEngine — so they belong on a worker thread. Doing them
            // inline would freeze the room for a second or more on every interaction, which is
            // the one thing T5's "quiet" cannot survive.
            //
            // The generator reference is captured on the main thread and only its
            // thread-safe GetOrGenerate is touched off it; nothing here compares a
            // UnityEngine.Object against null, which is the operation that would not be safe.
            IslandGenerator source = generator;
            Task<List<SheetRender>> job = Task.Run(() => Draw(source, seed, issued, count, drawSeed, ppmm));

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
                if (!generator.Ledger.MarkIssued(render.Id)) continue;

                spawner.Place(render, i, batch.Count, anchor);
                yield return null;   // one texture upload per frame
            }

            Debug.Log($"[MapCrate] {batch[0].IslandName} ({seed:X16}) — issued {batch.Count} sheets, " +
                      $"{generator.Ledger.IssuedCount(seed)} out in total.", this);

            busy = false;
        }

        /// <summary>
        /// Worker-thread half. Touches no engine API: an island, a pick, and N rasters.
        ///
        /// <para>Public because it is the whole pipeline minus the keypress, and the editor
        /// validator drives it directly — proving generation, picking and rendering without
        /// entering play mode.</para>
        /// </summary>
        public static List<SheetRender> Draw(IslandGenerator generator, ulong islandSeed,
                                             HashSet<SheetId> issued, int count,
                                             int drawSeed, double pixelsPerPaperMm)
        {
            // Through the generator, never Island.FromSeed directly: the island lands in the
            // cache, so every later question a spawned sheet asks about itself is a dictionary
            // lookup rather than another third of a second.
            Island island = generator.GetOrGenerate(islandSeed);
            List<Sheet> picks = SheetPicker.PickUnissued(island, count, issued, drawSeed);
            return Render(island, picks, pixelsPerPaperMm);
        }

        /// <summary>
        /// Rasterises an explicit list of sheets. Split out from <see cref="Draw"/> so that a
        /// case can be reproduced by naming its sheets instead of hoping the picker chooses
        /// them again — which is the difference between a bug you can look at and a bug you
        /// have to wait for.
        /// </summary>
        public static List<SheetRender> Render(Island island, IList<Sheet> sheets,
                                               double pixelsPerPaperMm)
        {
            var rendered = new List<SheetRender>(sheets.Count);
            for (int i = 0; i < sheets.Count; i++)
            {
                Sheet sheet = sheets[i];
                RenderRequest request = RenderRequest.ForSheet(sheet, pixelsPerPaperMm);
                ImageBuffer image = IslandRenderer.Render(island, request);
                rendered.Add(new SheetRender(SheetId.Of(sheet), sheet, island.Name, image));
            }
            return rendered;
        }
    }
}
