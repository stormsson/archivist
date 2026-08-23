using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Archivist.Building.Sheets;

namespace Archivist.Building.Editor
{
    /// <summary>
    /// Strips spawned sheets out of a scene just before it is written to disk.
    ///
    /// <para>The rule is that a scene never starts with paper on the floor: the ledger is the
    /// only record that a sheet has been issued, it starts empty on every load, and a sheet
    /// that outlived a load would be issuable twice (R2.10). <see cref="SheetSpawner"/>
    /// enforces that at startup; this stops the file ever containing one in the first place,
    /// so the working scene stays small and a diff of it stays readable.</para>
    ///
    /// <para>Deliberately a visible hook rather than a hideFlag. Sheets used to carry
    /// <c>HideFlags.DontSaveInEditor</c>, which achieved the same thing by making them
    /// invisible to Unity's own bookkeeping — and then their meshes were collected as
    /// unreferenced, <c>FindObjectsByType</c> stopped returning them, and they disappeared
    /// from the Hierarchy while still standing in the room. A save hook does one thing, says
    /// what it does, and leaves a sheet an ordinary GameObject the rest of the time.</para>
    /// </summary>
    [InitializeOnLoad]
    static class SheetSceneGuard
    {
        /// <summary>
        /// Destroys every sheet in memory, including ones that belong to no scene.
        ///
        /// <para>Sheets spawned while <c>HideFlags.DontSaveInEditor</c> was in force were
        /// never registered with a scene. That makes them invisible to the Hierarchy, to
        /// <c>FindObjectsByType</c>, and to <c>Scene.GetRootGameObjects</c> — and it means a
        /// scene reload does not destroy them, because there is no scene to unload them from.
        /// They render, they collide, and nothing ordinary can reach them. Only
        /// <c>Resources.FindObjectsOfTypeAll</c> returns them.</para>
        ///
        /// <para>The flag is gone, so nothing new can end up like this. This exists to sweep
        /// up what it left behind.</para>
        /// </summary>
        [MenuItem("Archivist/Quick · Purge Orphan Sheets")]
        public static void PurgeOrphans()
        {
            SheetView[] all = Resources.FindObjectsOfTypeAll<SheetView>();

            int removed = 0;
            int sceneless = 0;

            for (int i = 0; i < all.Length; i++)
            {
                SheetView view = all[i];
                if (view == null) continue;

                // Skip prefab assets on disk; everything else in memory is fair game.
                if (EditorUtility.IsPersistent(view.gameObject)) continue;

                if (!view.gameObject.scene.IsValid()) sceneless++;
                Object.DestroyImmediate(view.gameObject);
                removed++;
            }

            Debug.Log($"[SheetSceneGuard] Purged {removed} sheet(s), {sceneless} of which " +
                      "belonged to no scene at all.");
        }


        static SheetSceneGuard()
        {
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        static void OnSceneSaving(Scene scene, string path)
        {
            if (!scene.IsValid()) return;

            // SheetSpawner.AllInScene, not a root walk: root enumeration cannot see objects
            // with DontSave-family hideFlags, which is precisely the leftover this has to
            // catch.
            int removed = 0;
            SheetView[] sheets = SheetSpawner.AllInScene();

            for (int i = 0; i < sheets.Length; i++)
            {
                if (sheets[i] == null) continue;
                if (sheets[i].gameObject.scene != scene) continue;

                Object.DestroyImmediate(sheets[i].gameObject);
                removed++;
            }

            if (removed > 0)
                Debug.Log($"[SheetSceneGuard] Removed {removed} spawned sheet(s) before saving {scene.name}.");
        }
    }
}
