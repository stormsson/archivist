using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Archivist.Building.Binders;
using Archivist.Building.Sheets;

namespace Archivist.Building.Editor
{
    /// <summary>
    /// Strips spawned sheets — and the binders that hold them — out of a scene just before it
    /// is written to disk.
    ///
    /// <para>The rule is that a scene never starts with paper on the floor: the ledger is the
    /// only record that a sheet has been issued, it starts empty on every load, and a sheet
    /// that outlived a load would be issuable twice (R2.10). <see cref="SheetSpawner"/>
    /// enforces that at startup; this stops the file ever containing one in the first place,
    /// so the working scene stays small and a diff of it stays readable.</para>
    ///
    /// <para><b>A binder falls under the same rule, which is why this class covers more than
    /// its name says.</b> A binder is a list of issued <c>SheetId</c>s, so saving one saves a
    /// claim on sheets the ledger will have forgotten — and its contents are not serialised
    /// anyway, so what came back would be an empty folder holding a number. It is the same
    /// rule about the same fact, so it is enforced in the same place rather than in a second
    /// guard that could be added, removed or forgotten independently.</para>
    ///
    /// <para>Deliberately a visible hook rather than a hideFlag on the sheets. Hiding them from
    /// Unity's own bookkeeping keeps them out of the file too, and takes the Hierarchy, mesh
    /// ownership and <c>FindObjectsByType</c> with it. A save hook does one thing, says what it
    /// does, and leaves a sheet an ordinary GameObject the rest of the time.</para>
    /// </summary>
    [InitializeOnLoad]
    static class SheetSceneGuard
    {
        /// <summary>
        /// Destroys every sheet in memory, including any belonging to no scene. A sheet
        /// registered with no scene renders and collides but survives a scene reload and is
        /// reachable by nothing but <c>Resources.FindObjectsOfTypeAll</c>; this is the only
        /// sweep that gets one out of the room.
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
            int sheetsRemoved = 0;
            SheetView[] sheets = SheetSpawner.AllInScene();

            for (int i = 0; i < sheets.Length; i++)
            {
                if (sheets[i] == null) continue;
                if (sheets[i].gameObject.scene != scene) continue;

                Object.DestroyImmediate(sheets[i].gameObject);
                sheetsRemoved++;
            }

            int bindersRemoved = 0;
            BinderView[] binders = BinderSpawner.AllInScene();

            for (int i = 0; i < binders.Length; i++)
            {
                if (binders[i] == null) continue;
                if (binders[i].gameObject.scene != scene) continue;

                Object.DestroyImmediate(binders[i].gameObject);
                bindersRemoved++;
            }

            if (sheetsRemoved > 0 || bindersRemoved > 0)
                Debug.Log($"[SheetSceneGuard] Removed {sheetsRemoved} spawned sheet(s) and " +
                          $"{bindersRemoved} binder(s) before saving {scene.name}.");
        }
    }
}
