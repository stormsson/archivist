using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Archivist.Building.Collection;
using Archivist.Building.Interactables;
using Archivist.Building.Sheets;
using Archivist.Generation;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Editor
{
    /// <summary>
    /// Runs a crate opening in edit mode, without the keypress.
    ///
    /// <para>The interaction itself needs a human at the keyboard, but everything behind it —
    /// island generation, the cache, the ledger's exclusion, sheet selection, the raster, the
    /// upload flip, the layout — does not. This drives that half directly so it can be
    /// checked, and so sheet appearance can be iterated on without entering play mode.</para>
    /// </summary>
    public static class CrateValidator
    {
        /// <summary>
        /// The validator's own island counter, deliberately NOT
        /// <c>IslandGenerator.nextIslandIndex</c> — that field is serialised, and an editor
        /// tool has no business advancing the player's collection. Resets on domain reload,
        /// which is fine: nothing depends on it being continuous.
        /// </summary>
        static int editorIslandIndex;

        [MenuItem("Archivist/POC-04 · Draw One Crate (editor)")]
        public static void DrawOne()
        {
            IslandGenerator generator;
            SheetSpawner spawner;
            MapCrate crate;
            if (!Find(out generator, out spawner, out crate)) return;

            ulong seed = generator.SeedForIndex(editorIslandIndex++);
            HashSet<SheetId> issued = generator.Ledger.Snapshot(seed);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            List<SheetRender> batch = MapCrate.Draw(generator, seed, issued, 5, unchecked((int)seed), 1.2);
            long drawMs = watch.ElapsedMilliseconds;

            if (batch.Count == 0)
            {
                Debug.LogWarning($"[CrateValidator] Island {seed:X16} yielded no sheets.");
                return;
            }

            for (int i = 0; i < batch.Count; i++)
            {
                generator.Ledger.MarkIssued(batch[i].Id);
                spawner.Place(batch[i], i, batch.Count, crate.transform);
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine($"[CrateValidator] {batch[0].IslandName} ({seed:X16}) — {batch.Count} sheets in {drawMs} ms");
            for (int i = 0; i < batch.Count; i++)
            {
                SheetRender r = batch[i];
                report.AppendLine($"  {r.Id}  1:{r.Sheet.Survey.Scale.Denominator}" +
                                  $"  raster {r.Image.Width}x{r.Image.Height}" +
                                  $"  paper {r.Sheet.Survey.Format.WidthMm:0}x{r.Sheet.Survey.Format.HeightMm:0} mm" +
                                  $"  rot {r.Sheet.RotationDeg:0.0} deg");
            }
            report.AppendLine($"  ledger: {generator.Ledger.IssuedCount(seed)} issued for this island, " +
                              $"{generator.Ledger.KnownIslandCount} island(s) known");
            report.Append($"  cache: {generator.Cache.Count} island(s) held, " +
                          $"{generator.Cache.Hits} hit / {generator.Cache.Misses} miss");
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Asks every sheet on the floor what it is, using nothing but its
        /// <see cref="SheetId"/>. This is the walk back the design depends on — a sheet stores
        /// an identity and no geometry, so if this fails, storing only the identity was the
        /// wrong bargain.
        /// </summary>
        [MenuItem("Archivist/POC-04 · Resolve Sheets On Floor")]
        public static void ResolveSheets()
        {
            IslandGenerator generator;
            SheetSpawner spawner;
            MapCrate crate;
            if (!Find(out generator, out spawner, out crate)) return;

            if (spawner.Spawned.Count == 0)
            {
                Debug.LogWarning("[CrateValidator] No sheets on the floor. Draw a crate first.");
                return;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var report = new System.Text.StringBuilder();
            report.AppendLine($"[CrateValidator] Resolving {spawner.Spawned.Count} sheet(s) from SheetId alone:");

            int resolved = 0;
            for (int i = 0; i < spawner.Spawned.Count; i++)
            {
                SheetView view = spawner.Spawned[i];
                if (view == null) continue;

                Island island;
                Sheet sheet;
                if (!generator.TryResolve(view.Id, out island, out sheet))
                {
                    report.AppendLine($"  {view.Id}  UNRESOLVED");
                    continue;
                }

                resolved++;
                report.AppendLine($"  {island.Name}  {sheet.Survey.Office} {sheet.Survey.Year}" +
                                  $"  sheet {sheet.Number}  1:{sheet.Survey.Scale.Denominator}" +
                                  $"  centre ({sheet.CentreGround.X:0} m, {sheet.CentreGround.Y:0} m)" +
                                  $"  {sheet.Survey.SheetGroundWidth:0}x{sheet.Survey.SheetGroundHeight:0} m of ground");
            }

            report.Append($"  {resolved}/{spawner.Spawned.Count} resolved in {watch.ElapsedMilliseconds} ms — " +
                          $"cache {generator.Cache.Hits} hit / {generator.Cache.Misses} miss");
            Debug.Log(report.ToString());
        }

        [MenuItem("Archivist/POC-04 · Clear Spawned Sheets")]
        public static void Clear()
        {
            var spawner = Object.FindFirstObjectByType<SheetSpawner>();
            if (spawner == null) return;

            spawner.ClearAll();
            Debug.Log("[CrateValidator] Cleared spawned sheets. The ledger is untouched — " +
                      "clearing the floor is not un-issuing anything.");
        }

        static bool Find(out IslandGenerator generator, out SheetSpawner spawner, out MapCrate crate)
        {
            generator = Object.FindFirstObjectByType<IslandGenerator>();
            spawner = Object.FindFirstObjectByType<SheetSpawner>();
            crate = Object.FindFirstObjectByType<MapCrate>();

            if (generator != null && spawner != null && crate != null) return true;

            Debug.LogError("[CrateValidator] Open POC04_Room first — needs IslandGenerator, SheetSpawner and MapCrate in the scene.");
            return false;
        }
    }
}
