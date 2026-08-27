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
    /// The crate on a bare platform, and nothing else: a floor, a player, the collection.
    /// Open the crate and read what came out, with no room, table or cabinet in the scene to
    /// be disturbed by — or to disturb — a change to <c>Archivist.Generation</c>.
    ///
    /// <para><b>The same rig as the room, not a copy of it.</b> Player, prompt, generator and
    /// crate all come from <see cref="SceneParts"/>, so what is pressed here is what ships. A
    /// debug scene wired by hand would drift from the room, and the day it did, the bug would
    /// only ever appear in one of them.</para>
    ///
    /// <para><b>The kerb is not decoration.</b> There are no walls, and the player falls under
    /// gravity like anywhere else; a lip at the edge is what keeps a debug session from ending
    /// with a long drop.</para>
    ///
    /// <para>Re-running overwrites the scene, which is what makes it cheap to keep current.
    /// Anything worth keeping goes in the builder, not in the saved file.</para>
    /// </summary>
    public static class GeneratorSceneBuilder
    {
        const string ScenePath = SceneParts.SceneDir + "/Debug_Generator.unity";

        /// <summary>As wide as the room is internally (S3.1), so reach and throw distances
        /// read the same here as they do there.</summary>
        const float PlatformSpan = 10f;
        const float KerbHeight   = 0.6f;

        [MenuItem("Archivist/Build Generator Debug Scene")]
        public static void Build()
        {
            SceneParts.EnsureFolders();
            SceneParts.EnsureMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildPlatform();

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

            Debug.Log($"[GeneratorSceneBuilder] Built {ScenePath} — crate, collection and " +
                      $"player on a {PlatformSpan}x{PlatformSpan}m platform.");
        }

        /// <summary>
        /// Floor, kerb and one light. Same top face at y = 0 as the room's floor (S3.3), so a
        /// sheet lies at the height the spawners already assume.
        /// </summary>
        static void BuildPlatform()
        {
            var root = new GameObject("Platform");

            const float t = SceneParts.WallThickness;
            float half = PlatformSpan / 2f + t / 2f;
            float span = PlatformSpan + 2f * t;

            SceneParts.Slab(root.transform, "Floor",
                new Vector3(span, t, span),
                new Vector3(0f, -t / 2f, 0f),
                "M_Placeholder_Floor", "Ground");

            Kerb(root.transform, "Kerb_North", new Vector3(span, KerbHeight, t), new Vector3(0f, 0f, half));
            Kerb(root.transform, "Kerb_South", new Vector3(span, KerbHeight, t), new Vector3(0f, 0f, -half));
            Kerb(root.transform, "Kerb_East",  new Vector3(t, KerbHeight, span), new Vector3(half, 0f, 0f));
            Kerb(root.transform, "Kerb_West",  new Vector3(t, KerbHeight, span), new Vector3(-half, 0f, 0f));

            SceneParts.KeyLight(root.transform);
        }

        static void Kerb(Transform parent, string name, Vector3 size, Vector3 footCentre)
        {
            SceneParts.Slab(parent, name, size,
                footCentre + new Vector3(0f, KerbHeight / 2f, 0f),
                "M_Placeholder_Wall", "Structure");
        }
    }
}
