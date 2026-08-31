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
    /// The crate. Aim at it, press the key, and an island comes into existence — unseen and
    /// never drawn as a whole (§3.1) — followed by a <b>binder</b> of its sheets.
    ///
    /// <para><b>A delivery is one binder</b> (§13, D-C1): the player's physical item is the
    /// folder, never the sheet, for a game whose unit of work is a document rather than a page.
    /// One binder is also one object to carry, one to shelve, and one for a map table to take
    /// its island from (C4.2).</para>
    ///
    /// <para><b>What it delivers is a debug set, not the game's supply</b> (Q7.2; Q7.1 has the
    /// collection already in the room). <c>everySheetOfTheIsland</c>, <c>looseDebugSheet</c> and
    /// <c>openNewIslandEachTime</c> are debug settings and each defaults to on, so each hides
    /// something the game is about: an archive whose islands arrive complete has no backlog, and
    /// nothing in the design puts a loose sheet on the floor.</para>
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

        /// <summary>What paper this crate delivers is drawn at. Read by <c>RoomPaper</c> so a
        /// sheet restored from a save is the same paper as the one that came out of here, and
        /// not a second, sharper copy of it.</summary>
        public double PixelsPerPaperMm { get { return pixelsPerPaperMm; } }

        [Tooltip("On: every opening is a new island. Off: keep drawing from the last one, " +
                 "which is what makes the ledger's exclusion visible — sheets never repeat.")]
        [SerializeField] bool openNewIslandEachTime = true;

        bool busy;

        /// <summary>
        /// R1.8: generation takes long enough to be a state, so the crate says so. The reason
        /// travels in the refusal, never in the label — see <see cref="InteractionState"/>.
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
            // 0 asks for the whole island — a sentinel rather than a very large number,
            // because Fill adds one to it for the loose sheet and int.MaxValue + 1 is
            // negative, which would quietly deliver nothing at all.
            int count = everySheetOfTheIsland ? 0 : sheetsPerOpening;
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

            // A FIXED DEBUG DELIVERY, and it is a debug delivery — MapCrate is a development
            // tool and not the game's supply (Q7.2). One opening puts down:
            //
            //   * one binder holding the WHOLE of the first island, every office in one folder;
            //   * two binders of a SECOND island, one office each.
            //
            // Between them those are the two cases a table has to handle. The full binder shows
            // three layers on a table that takes one folder, so Q/E has something to cycle
            // without anything being merged first (Q3.4, Q4.5). The pair is what a merge is
            // tried on, and being two offices of one island it is also the case that has to
            // refuse to merge with the first island's folder.
            var delivered = new List<BinderView>(3);

            BinderView whole = binders.Create(seed, opening.IslandName);
            if (whole != null)
            {
                for (int i = 0; i < opening.Filed.Count; i++)
                {
                    // R2.10 enforced here and nowhere else: a sheet that is already out is never
                    // issued twice, even if a picker somewhere later gets it wrong.
                    SheetId id = opening.Filed[i];
                    if (generator.Ledger.MarkIssued(id)) whole.Add(id);
                }

                // Each is placed before the next is made: BinderSpawner.RestingPose probes
                // downward for what is already lying there, so the second binder comes to rest
                // on the first rather than inside it.
                binders.Place(whole, anchor);
                delivered.Add(whole);
            }

            // ---- the second island, two offices, one folder each ------------------------

            ulong second = generator.ReserveNextIslandSeed();
            HashSet<SheetId> issuedSecond = generator.Ledger.Snapshot(second);
            int drawSecond = unchecked((int)(second ^ ((ulong)issuedSecond.Count * 0x9E3779B97F4A7C15UL)));

            IslandGenerator alsoSource = generator;
            Task<Opening> alsoJob = Task.Run(
                () => Fill(alsoSource, second, issuedSecond, 0, false, drawSecond, ppmm));

            while (!alsoJob.IsCompleted) yield return null;

            if (alsoJob.IsFaulted) Debug.LogException(alsoJob.Exception, this);
            else
            {
                generator.Ledger.Describe(generator.GetOrGenerate(second));
                Opening also = alsoJob.Result;

                var byOffice = new Dictionary<Office, List<SheetId>>();
                for (int i = 0; i < also.Filed.Count; i++)
                {
                    SheetId id = also.Filed[i];
                    List<SheetId> forOffice;
                    if (!byOffice.TryGetValue(id.Office, out forOffice))
                    {
                        forOffice = new List<SheetId>();
                        byOffice[id.Office] = forOffice;
                    }
                    forOffice.Add(id);
                }

                // Offices.All order and the first two that have plates, so two openings deal the
                // same way round. The rest of the island stays unissued and the crate can be
                // opened again for it.
                int made = 0;
                for (int o = 0; o < Offices.All.Length && made < 2; o++)
                {
                    List<SheetId> forOffice;
                    if (!byOffice.TryGetValue(Offices.All[o], out forOffice)) continue;

                    BinderView binder = binders.Create(second, also.IslandName);
                    if (binder == null) break;

                    for (int i = 0; i < forOffice.Count; i++)
                    {
                        SheetId id = forOffice[i];
                        if (generator.Ledger.MarkIssued(id)) binder.Add(id);
                    }

                    binders.Place(binder, anchor);
                    delivered.Add(binder);
                    made++;
                }
            }

            yield return null;

            if (opening.Loose != null && generator.Ledger.MarkIssued(opening.Loose.Id))
                spawner.Place(opening.Loose, 0, 1, anchor);

            // C9.1: the ledger is saved the moment sheets are issued and before any of them can
            // reach a table, so no board can ever name a sheet the save says was never issued.
            Archive.Note();

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
            List<Sheet> picks = SheetPicker.PickUnissued(island, wanted, issued, drawSeed, true);

            // The loose sheet is the last of the pick, and is NOT one of the binder's: it
            // exists to be filed into the binder later, which it could not be if it were
            // already in there.
            SheetRender loose = null;
            int fileCount = picks.Count;

            // Never the chart, whatever else. The chart is the board's base (Q4.4) and R6.8a
            // will not open a board without it; leaving it on the floor as the one loose sheet
            // would make the board's own gate the thing lying under a rack. It is picks[0] when
            // it is present, and the loose one comes off the end, so this only bites on an
            // island whose chart is the ONLY plate left.
            bool lastIsChart = picks.Count > 0 && picks[picks.Count - 1].Survey.IsWholeIsland;

            if (wantLoose && picks.Count > 0 && !lastIsChart)
            {
                fileCount = picks.Count - 1;
                loose = Render(island, new List<Sheet> { picks[picks.Count - 1] }, pixelsPerPaperMm)[0];
            }

            var filed = new List<SheetId>(fileCount);
            for (int i = 0; i < fileCount; i++) filed.Add(SheetId.Of(picks[i]));

            return new Opening(island.Name, filed, loose);
        }

        /// <summary>
        /// Rasterises an explicit list of sheets, for a board: sheets rendered at a target GROUND resolution rather than a
        /// paper one, because a board lays every plate at its ground size and a chart at
        /// 1:25000 would otherwise come out softer than a quarter at 1:10000. See
        /// <c>RenderRequest.ForSheetAtGroundResolution</c>.
        /// </summary>
        public static List<SheetRender> RenderForBoard(Island island, IList<Sheet> sheets,
                                                       double pixelsPerMetre)
        {
            return Render(island, sheets, pixelsPerMetre, true);
        }

        /// <summary>
        /// Rasterises an explicit list of sheets at a paper resolution. Taking the list rather
        /// than a count is what lets a case be reproduced by naming its sheets instead of hoping
        /// the picker chooses them again.
        /// </summary>
        public static List<SheetRender> Render(Island island, IList<Sheet> sheets,
                                               double pixelsPerPaperMm)
        {
            return Render(island, sheets, pixelsPerPaperMm, false);
        }

        static List<SheetRender> Render(Island island, IList<Sheet> sheets,
                                        double resolution, bool byGround)
        {
            var rendered = new List<SheetRender>(sheets.Count);

            // One cache for the batch. Three offices' plates of one quarter read the same field
            // corners (Q1.2), and a batch is exactly where they arrive together — a crate
            // delivering an island, a board filling in. Created here and dropped with the
            // method, so it is single-threaded by construction and holds nothing afterwards.
            var samples = new SampleGridCache();

            for (int i = 0; i < sheets.Count; i++)
            {
                Sheet sheet = sheets[i];
                // The office's own layers (Q2.1), never LayerMask.All. Drawing everything on
                // every sheet is what made an office a stamp in the margin — F-S1.7 measured
                // the result and called it a filled colour relief map where the mockups show
                // ink on paper.
                RenderRequest request = byGround
                    ? RenderRequest.ForSheetAtGroundResolution(sheet, resolution, OfficeLayers.For(sheet))
                    : RenderRequest.ForSheet(sheet, resolution, OfficeLayers.For(sheet));
                ImageBuffer image = IslandRenderer.Render(island, request,
                                                          OfficeStyles.For(sheet), samples);
                rendered.Add(new SheetRender(SheetId.Of(sheet), sheet, island.Name, image,
                                             request.PixelsPerPaperMm));
            }
            return rendered;
        }
    }
}
