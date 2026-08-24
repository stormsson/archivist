using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Archivist.Building.Binders;
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
    /// unmapped, and never drawn as a whole — followed by a <b>binder</b> of its sheets.
    ///
    /// <para><b>The island stays hidden on purpose, and not merely for the POC.</b> There is
    /// no world geometry above an island and no spatial relationship between islands (§3.1);
    /// the sea they sit in is never drawn. The island exists as vector data that sheets are
    /// cut from, and the player's only access to it is paper. Showing it would answer the
    /// question the whole game is about.</para>
    ///
    /// <para><b>It used to tip five loose sheets onto the floor, and no longer does.</b> The
    /// cartography table's spec had already settled the model (§13, D-C1): the player's
    /// physical item is the folder, never the sheet. Loose paper was five things to pick up
    /// one at a time and — once racks exist — five things to file individually, for a game
    /// whose unit of work is meant to be a document, not a page. A delivery is now one binder,
    /// which is also one object to carry, one to shelve, and one for a map table to take its
    /// island from (C4.2).</para>
    ///
    /// <para><b>Except for one loose sheet, which is a debug affordance and says so.</b>
    /// Nothing can yet take a sheet <i>out</i> of a binder, so without a sheet somewhere in
    /// the room there is nothing to test "file this into that" against when it is built. It is
    /// a real, issued sheet of the same island — not a fake — so the verb, when it exists,
    /// will be exercised against the real thing. Turn <c>looseDebugSheet</c> off and the crate
    /// delivers a binder and nothing else, which is what it should eventually do.</para>
    ///
    /// <para><b>Two temporary knobs, both for the map table, both to be put back.</b>
    /// <c>bindersPerOpening</c> above 1 delivers several folders of the <i>same</i> island in
    /// one opening, which is how the table's second-binder branch (C4.3) and its no-room
    /// refusal are reached without opening the crate twice — with
    /// <c>openNewIslandEachTime</c> on, opening twice gives two different islands and tests
    /// the refusal instead. When it is above 1 the sheets are <b>one</b> pick dealt into
    /// shares rather than one pick per binder, because two draws from a ledger snapshot taken
    /// before either could hand the same sheet out twice.</para>
    ///
    /// <para><c>everySheetOfTheIsland</c> files the whole survey into the delivery instead of
    /// a handful, so a table can be composed in full rather than from five sheets scattered
    /// across an island. It is the opposite of what the crate is for — the count is what makes
    /// the ledger's exclusion visible, and an archive whose islands arrive complete has no
    /// backlog — which is why it says TEMPORARY in the Inspector. Note the loose debug sheet
    /// still comes out of that total: the binder holds every sheet <i>but one</i>, and filing
    /// the loose one into it completes the island, which is a better test of the filing verb
    /// than a spare sheet from an island nobody has laid out.</para>
    /// </summary>
    public sealed class MapCrate : Interactable
    {
        [Header("Wiring")]
        [SerializeField] IslandGenerator generator;
        [SerializeField] BinderSpawner binders;
        [Tooltip("Only used for the loose debug sheet. A binder needs no rendering at all.")]
        [SerializeField] SheetSpawner spawner;
        [Tooltip("Where the delivery lands. Falls back to this transform.")]
        [SerializeField] Transform dropAnchor;

        [Header("Contents")]
        [Tooltip("Sheets filed into each binder every opening.")]
        [SerializeField] int sheetsPerOpening = 5;

        [Tooltip("TEMPORARY, for testing the map table: how many binders one opening " +
                 "delivers, all of the SAME island. 1 is the design — a delivery is one " +
                 "folder; raise it to reach the table's same-island and no-room branches " +
                 "without opening the crate twice.")]
        [SerializeField, Min(1)] int bindersPerOpening = 1;

        [Tooltip("TEMPORARY, for testing the map table: file EVERY unissued sheet of the " +
                 "island, ignoring the count above, so one delivery is the whole survey and " +
                 "the board can be composed in full. Turn it off to go back to a handful per " +
                 "opening, which is what makes the ledger's exclusion visible.")]
        [SerializeField] bool everySheetOfTheIsland = true;

        [Tooltip("Debug: also drop one loose sheet of the same island on the floor, so there " +
                 "is something to file into a binder once that verb exists. Not part of the " +
                 "design; turn it off and a delivery is a binder alone.")]
        [SerializeField] bool looseDebugSheet = true;

        [Tooltip("Render resolution in pixels per millimetre of paper, for the loose debug " +
                 "sheet only. RenderTuning's own default is 2.7 (~68 dpi), which is in-hand " +
                 "quality; a sheet read from standing height needs far less.")]
        [SerializeField] double pixelsPerPaperMm = 1.2;

        [Tooltip("On: every opening is a new island. Off: keep drawing from the last one, " +
                 "which is what makes the ledger's exclusion visible — sheets never repeat.")]
        [SerializeField] bool openNewIslandEachTime = true;

        bool busy;

        /// <summary>
        /// R1.8: generation takes long enough to be a state, so the crate says so. The reason
        /// travels in the refusal and no longer in the label — this class was the one that
        /// smuggled "Working…" through <c>Label</c> for want of anywhere else to put it, and
        /// <see cref="InteractionState"/> records why that was tolerable exactly once.
        /// </summary>
        public override InteractionState CanInteract(PlayerInteractor by)
        {
            return busy ? InteractionState.Refused("Working…") : base.CanInteract(by);
        }

        public override void Interact(PlayerInteractor by)
        {
            if (busy) return;
            if (generator == null || binders == null)
            {
                Debug.LogError("[MapCrate] Not wired to an IslandGenerator and BinderSpawner.", this);
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

            // One pick for the whole delivery, then dealt out: asking the picker twice would
            // be two draws from a ledger snapshot taken before either, and the second could
            // pick sheets the first had already taken.
            int wantBinders = Mathf.Max(1, bindersPerOpening);

            // 0 asks for the whole island — a sentinel rather than a very large number,
            // because Fill adds one to it for the loose sheet and int.MaxValue + 1 is
            // negative, which would quietly deliver nothing at all.
            int count = everySheetOfTheIsland ? 0 : sheetsPerOpening * wantBinders;
            bool wantLoose = looseDebugSheet && spawner != null;
            double ppmm = pixelsPerPaperMm;

            // Generation and the picking are pure, engine-free C# — Archivist.Generation may
            // not even reference UnityEngine — so they belong on a worker thread. Doing them
            // inline would freeze the room for a third of a second or more on every
            // interaction, which is the one thing T5's "quiet" cannot survive.
            //
            // The generator reference is captured on the main thread and only its
            // thread-safe GetOrGenerate is touched off it; nothing here compares a
            // UnityEngine.Object against null, which is the operation that would not be safe.
            //
            // Note what is NOT in here any more: five rasters. A binder holds identities, so
            // an opening renders one sheet when the debug flag is on and none when it is off.
            IslandGenerator source = generator;
            Task<Opening> job = Task.Run(() => Fill(source, seed, issued, count, wantLoose, drawSeed, ppmm));

            while (!job.IsCompleted) yield return null;

            if (job.IsFaulted)
            {
                Debug.LogException(job.Exception, this);
                busy = false;
                yield break;
            }

            // The island is in the cache — the job has just built it — so this is a dictionary
            // lookup, not a second generation. It happens here, on the main thread, because
            // the ledger is written from nowhere else, and it happens before the empty check
            // because an exhausted island is exactly the one whose count a player will want to
            // see: without this it would sit in the collection list with no denominator.
            generator.Ledger.Describe(generator.GetOrGenerate(seed));

            Opening opening = job.Result;

            if (opening.Filed.Count == 0 && opening.Loose == null)
            {
                // R1.8/R2.9: an island running out of undrawn sheets is a legitimate state.
                Debug.Log($"[MapCrate] Island {seed:X16} has no unissued sheets left.", this);
                busy = false;
                yield break;
            }

            Transform anchor = dropAnchor != null ? dropAnchor : transform;

            // Dealt in order, so the first binder holds the first share. Ceil rather than
            // floor: with fewer sheets than asked for — an island running low — the shares
            // stay full and the last binder simply is not created, which is better than
            // several thin folders of one sheet each.
            var delivered = new List<BinderView>(wantBinders);
            int perBinder = opening.Filed.Count > 0
                ? Mathf.Max(1, Mathf.CeilToInt(opening.Filed.Count / (float)wantBinders))
                : 0;

            for (int first = 0; first < opening.Filed.Count; first += perBinder)
            {
                BinderView binder = binders.Create(seed, opening.IslandName);
                if (binder == null) break;

                int last = Mathf.Min(first + perBinder, opening.Filed.Count);
                for (int i = first; i < last; i++)
                {
                    // R2.10 enforced here and nowhere else: a sheet that is already out is
                    // never issued twice, even if a picker somewhere later gets it wrong.
                    // Filed only if the ledger agrees it is this call that issued it.
                    SheetId id = opening.Filed[i];
                    if (generator.Ledger.MarkIssued(id)) binder.Add(id);
                }

                // Each is placed before the next is made: BinderSpawner.RestingPose probes
                // downward for what is already lying there, so the second binder comes to rest
                // on the first rather than inside it.
                binders.Place(binder, anchor);
                delivered.Add(binder);
            }

            yield return null;

            if (opening.Loose != null && generator.Ledger.MarkIssued(opening.Loose.Id))
                spawner.Place(opening.Loose, 0, 1, anchor);

            IslandHolding holding;
            generator.Ledger.TryGetHolding(seed, out holding);

            var what = new System.Text.StringBuilder();
            for (int i = 0; i < delivered.Count; i++)
            {
                if (i > 0) what.Append(" + ");
                what.Append(delivered[i].Summary);
            }
            if (delivered.Count == 0) what.Append("no binder");

            Debug.Log($"[MapCrate] delivered {what}" +
                      $"{(opening.Loose != null ? " + 1 loose sheet" : "")} — {holding}", this);

            busy = false;
        }

        /// <summary>
        /// What one opening of the crate produces: identities to file, and — while the debug
        /// flag is on — one rendered sheet to leave on the floor.
        ///
        /// <para>Built entirely on the worker thread and handed across, which is why it holds
        /// <see cref="SheetId"/> values and a <see cref="SheetRender"/> rather than anything
        /// from UnityEngine.</para>
        /// </summary>
        public sealed class Opening
        {
            /// <summary>Sheets to file into the binder. Identities only — no geometry, no
            /// raster (R1.1, R1.11).</summary>
            public readonly List<SheetId> Filed;

            /// <summary>The loose debug sheet, or null when there is none.</summary>
            public readonly SheetRender Loose;

            /// <summary>A memo, so the binder can carry a readable island name without
            /// regenerating anything to ask for it.</summary>
            public readonly string IslandName;

            public Opening(string islandName, List<SheetId> filed, SheetRender loose)
            {
                IslandName = islandName;
                Filed = filed;
                Loose = loose;
            }
        }

        /// <summary>
        /// Worker-thread half. Touches no engine API: an island, a pick, and at most one
        /// raster.
        ///
        /// <para>Public because it is the whole pipeline minus the keypress, and a bench can
        /// drive it directly — proving generation and picking without entering play mode.</para>
        ///
        /// <para><paramref name="forBinder"/> of zero or less means <b>every</b> unissued sheet
        /// of the island, the sentinel <see cref="SheetPicker.PickUnissued"/> takes. The loose
        /// sheet then comes out of that total rather than being drawn on top of it — there is
        /// no "one more" once everything has been asked for.</para>
        /// </summary>
        public static Opening Fill(IslandGenerator generator, ulong islandSeed,
                                   HashSet<SheetId> issued, int forBinder, bool wantLoose,
                                   int drawSeed, double pixelsPerPaperMm)
        {
            // Through the generator, never Island.FromSeed directly: the island lands in the
            // cache, so every later question a binder or a sheet asks about itself is a
            // dictionary lookup rather than another third of a second.
            Island island = generator.GetOrGenerate(islandSeed);

            // forBinder <= 0 means the whole island; the loose sheet then comes out of that
            // total rather than being one more on top of it, because there is no "one more"
            // once every sheet has been asked for. Adding to the sentinel would turn it into
            // an ordinary count of 1.
            int wanted = forBinder <= 0 ? 0 : forBinder + (wantLoose ? 1 : 0);
            List<Sheet> picks = SheetPicker.PickUnissued(island, wanted, issued, drawSeed);

            // The loose sheet is the last of the pick, and is NOT one of the binder's: it
            // exists to be filed into the binder later, which it could not be if it were
            // already in there.
            SheetRender loose = null;
            int fileCount = picks.Count;

            if (wantLoose && picks.Count > 0)
            {
                fileCount = picks.Count - 1;
                loose = Render(island, new List<Sheet> { picks[picks.Count - 1] }, pixelsPerPaperMm)[0];
            }

            var filed = new List<SheetId>(fileCount);
            for (int i = 0; i < fileCount; i++) filed.Add(SheetId.Of(picks[i]));

            return new Opening(island.Name, filed, loose);
        }

        /// <summary>
        /// Rasterises an explicit list of sheets. Split out so that a case can be reproduced
        /// by naming its sheets instead of hoping the picker chooses them again — which is the
        /// difference between a bug you can look at and a bug you have to wait for.
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
