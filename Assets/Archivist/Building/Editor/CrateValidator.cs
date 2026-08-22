using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Archivist.Building.Collection;
using Archivist.Building.Interactables;
using Archivist.Building.Sheets;

namespace Archivist.Building.Editor
{
    /// <summary>
    /// Runs a crate opening in edit mode, without the keypress.
    ///
    /// <para>The interaction itself needs a human at the keyboard, but everything behind it —
    /// island generation, the ledger's exclusion, sheet selection, the raster, the upload
    /// flip, the layout — does not. This drives that half directly so it can be checked, and
    /// so sheet appearance can be iterated on without entering play mode each time.</para>
    /// </summary>
    public static class CrateValidator
    {
        /// <summary>
        /// The validator's own island counter, deliberately NOT
        /// <c>CollectionService.nextIslandIndex</c> — that field is serialised, and an editor
        /// tool has no business advancing the player's collection. Resets on domain reload,
        /// which is fine: nothing depends on it being continuous.
        /// </summary>
        static int editorIslandIndex;

        [MenuItem("Archivist/POC-04 · Clear Spawned Sheets")]
        public static void Clear()
        {
            var spawner = Object.FindFirstObjectByType<SheetSpawner>();
            if (spawner == null) return;

            spawner.ClearAll();
            Debug.Log("[CrateValidator] Cleared spawned sheets. The ledger is untouched — " +
                      "clearing the floor is not un-issuing anything.");
        }

        [MenuItem("Archivist/POC-04 · Draw One Crate (editor)")]
        public static void DrawOne()
        {
            var collection = Object.FindFirstObjectByType<CollectionService>();
            var spawner = Object.FindFirstObjectByType<SheetSpawner>();
            var crate = Object.FindFirstObjectByType<MapCrate>();

            if (collection == null || spawner == null || crate == null)
            {
                Debug.LogError("[CrateValidator] Open POC04_Room first — needs CollectionService, SheetSpawner and MapCrate in the scene.");
                return;
            }

            ulong seed = collection.SeedForIndex(editorIslandIndex++);
            HashSet<SheetId> issued = collection.Ledger.Snapshot(seed);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            List<SheetRender> batch = MapCrate.Draw(seed, issued, 5, unchecked((int)seed), 1.2);
            long drawMs = watch.ElapsedMilliseconds;

            if (batch.Count == 0)
            {
                Debug.LogWarning($"[CrateValidator] Island {seed:X16} yielded no sheets.");
                return;
            }

            for (int i = 0; i < batch.Count; i++)
            {
                collection.Ledger.MarkIssued(batch[i].Id);
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
            report.Append($"  ledger: {collection.Ledger.IssuedCount(seed)} issued for this island, " +
                          $"{collection.Ledger.KnownIslandCount} island(s) known");
            Debug.Log(report.ToString());
        }
    }
}
