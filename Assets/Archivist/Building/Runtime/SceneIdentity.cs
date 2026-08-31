using UnityEngine;

namespace Archivist.Building
{
    /// <summary>
    /// A component's place in the scene as 32 hex characters — a GUID's shape, from a hash rather
    /// than a draw, so two runs of one scene agree without anything being written down.
    ///
    /// <para>FNV-1a, twice, and deliberately not <c>string.GetHashCode</c>: that is randomised per
    /// process on modern runtimes, which would make this the very thing it exists to stop being —
    /// a different answer every time the game starts.</para>
    /// </summary>
    public static class SceneIdentity
    {
        /// <summary>The id <paramref name="of"/> answers to while nothing has been serialised for
        /// it, and the same value <see cref="Pin"/> writes down.</summary>
        public static string Derive(Component of)
        {
            if (of == null) return string.Empty;

            Transform t = of.transform;
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            UnityEngine.SceneManagement.Scene scene = of.gameObject.scene;
            string where = (string.IsNullOrEmpty(scene.path) ? scene.name : scene.path) + ":" + path;

            return Hash(where, 14695981039346656037UL).ToString("x16")
                 + Hash(where, 0xCBF29CE484222325UL ^ 0x9E3779B97F4A7C15UL).ToString("x16");
        }

        static ulong Hash(string text, ulong basis)
        {
            ulong hash = basis;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Writes the derived id into <paramref name="id"/>, in a real scene only, so that it is
        /// pinned against the component later being renamed or reparented. Every guard here is a
        /// way an id ends up shared: a prefab asset's instances all inherit one, and
        /// <c>PrefabUtility.LoadPrefabContents</c> loads into a <i>preview scene</i>, where
        /// <c>IsPartOfPrefabAsset</c> is false and <c>GetCurrentPrefabStage</c> is null, so
        /// without <c>IsPreviewSceneObject</c> an id gets written into the prefab asset itself.
        /// Both failures are silent, and the symptom — two objects quietly sharing one history —
        /// does not look like an identity bug.
        ///
        /// <para><b>The value is derived, not drawn.</b> Marking the component dirty does not save
        /// the scene, and nothing makes anybody save it; a drawn GUID that never reached disk
        /// would be re-drawn on the next domain reload and the object's whole history would move
        /// with it. Writing the same value the component would have derived anyway means the
        /// unsaved case and the saved case agree.</para>
        /// </summary>
        public static void Pin(Component of, ref string id)
        {
            if (of == null || !string.IsNullOrEmpty(id)) return;
            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(of)) return;
            if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null) return;
            if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewSceneObject(of)) return;
            if (!of.gameObject.scene.IsValid()) return;

            id = Derive(of);
            UnityEditor.EditorUtility.SetDirty(of);
        }

        /// <summary>For the one case <see cref="Pin"/> cannot serve: two components that must not
        /// share a history and do, because one was duplicated in the Hierarchy and arrives holding
        /// its original's serialised id. This is also how a component is deliberately given
        /// somebody else's history: paste the id in by hand.</summary>
        public static void Mint(Component of, ref string id)
        {
            if (of == null) return;

            id = System.Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(of);
        }
#endif
    }
}
