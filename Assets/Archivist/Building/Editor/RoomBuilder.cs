using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Archivist.Building.Binders;
using Archivist.Building.Collection;
using Archivist.Building.Interaction;
using Archivist.Building.Sheets;

namespace Archivist.Building.Editor
{
    /// <summary>
    /// Builds the POC-04 debug room from the numbers in
    /// <c>docs/space/requirements.md</c>. The room is a function of the spec, not
    /// hand-placed geometry: change a constant below, re-run, get the new room.
    /// That is the whole point of it being a script — S3.1 marks these dimensions
    /// provisional, and provisional geometry has to be cheap to rebuild.
    ///
    /// <para>What is here is the room: its shell, its light, its checker. The player, the
    /// prompt, the collection and the crate come from <see cref="SceneParts"/>, because they
    /// are true wherever they stand and a second scene must get the same ones.</para>
    /// </summary>
    public static class RoomBuilder
    {
        // ---- S3: the room (provisional, S3.1) ------------------------------
        const float RoomInternal   = 10f;   // 10 x 10 m internal
        const float CeilingHeight  = 3.2f;

        const string ScenePath  = SceneParts.SceneDir + "/POC04_Room.unity";
        const string RoomPrefab = SceneParts.PrefabDir + "/PF_Archive_Room_Debug.prefab";

        [MenuItem("Archivist/Build POC-04 Room")]
        public static void Build()
        {
            SceneParts.EnsureFolders();
            SceneParts.EnsureMaterials();
            MakeChecker();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var roomAsset = BuildRoomPrefab();
            var room = (GameObject)PrefabUtility.InstantiatePrefab(roomAsset);
            room.name = "Archive_Room_Debug";

            InteractionPrompt prompt = SceneParts.BuildInteractionUi();
            SceneParts.BuildPlayer(prompt);

            IslandGenerator generator;
            SheetSpawner spawner;
            BinderSpawner binders;
            SceneParts.BuildGenerator(out generator, out spawner, out binders);
            SceneParts.BuildMapCrate(generator, spawner, binders);

            SceneParts.ApplyEnvironment();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[RoomBuilder] Built {ScenePath} — {RoomInternal}x{RoomInternal}m internal, {CeilingHeight}m ceiling.");
        }

        // --------------------------------------------------------------------

        /// <summary>
        /// S6.2 — a measuring instrument, not art. 512 px/m, 2 m tile, 0.5 m
        /// squares. If an imported asset is the wrong scale, this is what makes
        /// it visible instead of merely wrong.
        /// </summary>
        static void MakeChecker()
        {
            var matPath = $"{SceneParts.MatDir}/M_Placeholder_Checker.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null) return;

            var texPath = $"{SceneParts.TexDir}/T_Placeholder_Checker_BC.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null)
            {
                const int px = 1024, square = 256;   // 512 px/m over a 2 m tile
                var a = new Color(0.62f, 0.61f, 0.60f, 1f);
                var b = new Color(0.40f, 0.39f, 0.38f, 1f);

                var tex = new Texture2D(px, px, TextureFormat.RGBA32, false);
                var cols = new Color[px * px];
                for (int y = 0; y < px; y++)
                    for (int x = 0; x < px; x++)
                        cols[y * px + x] = (((x / square) + (y / square)) % 2 == 0) ? a : b;
                tex.SetPixels(cols);
                tex.Apply();
                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);

                AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);
                var imp = (TextureImporter)AssetImporter.GetAtPath(texPath);
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }

            var chk = new Material(SceneParts.Lit());
            chk.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            chk.SetTextureScale("_BaseMap", new Vector2(RoomInternal / 2f, RoomInternal / 2f));
            chk.SetFloat("_Smoothness", 0.05f);
            chk.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(chk, matPath);
        }

        // --------------------------------------------------------------------

        static GameObject BuildRoomPrefab()
        {
            var root = new GameObject("PF_Archive_Room_Debug");   // pivot at floor centre (S3.3)

            var structure = SceneParts.NewChild("Structure", root.transform);
            SceneParts.NewChild("Openings", root.transform);      // doors/windows, later
            var lighting  = SceneParts.NewChild("Lighting", root.transform);
            SceneParts.NewChild("Anchors", root.transform);       // S3.4 — where furniture will land

            const float t = SceneParts.WallThickness;
            float span   = RoomInternal + 2f * t;                 // slabs run under the walls
            float half   = RoomInternal / 2f + t / 2f;
            float wallMidY = CeilingHeight / 2f;

            // Floor: top face sits exactly at y = 0 (S3.3)
            SceneParts.Slab(structure.transform, "Floor",
                 new Vector3(span, t, span),
                 new Vector3(0f, -t / 2f, 0f),
                 "M_Placeholder_Floor", "Ground");

            SceneParts.Slab(structure.transform, "Ceiling",
                 new Vector3(span, t, span),
                 new Vector3(0f, CeilingHeight + t / 2f, 0f),
                 "M_Placeholder_Ceiling", "Structure");

            SceneParts.Slab(structure.transform, "Wall_North",
                 new Vector3(span, CeilingHeight, t),
                 new Vector3(0f, wallMidY, half), "M_Placeholder_Wall", "Structure");

            SceneParts.Slab(structure.transform, "Wall_South",
                 new Vector3(span, CeilingHeight, t),
                 new Vector3(0f, wallMidY, -half), "M_Placeholder_Wall", "Structure");

            SceneParts.Slab(structure.transform, "Wall_East",
                 new Vector3(t, CeilingHeight, span),
                 new Vector3(half, wallMidY, 0f), "M_Placeholder_Wall", "Structure");

            SceneParts.Slab(structure.transform, "Wall_West",
                 new Vector3(t, CeilingHeight, span),
                 new Vector3(-half, wallMidY, 0f), "M_Placeholder_Wall", "Structure");

            SceneParts.KeyLight(lighting.transform);

            var asset = PrefabUtility.SaveAsPrefabAsset(root, RoomPrefab);
            Object.DestroyImmediate(root);
            return asset;
        }
    }
}
