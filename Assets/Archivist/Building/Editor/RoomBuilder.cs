using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Archivist.Building.Collection;
using Archivist.Building.Interaction;
using Archivist.Building.Interactables;
using Archivist.Building.Sheets;

namespace Archivist.Building.Editor
{
    /// <summary>
    /// Builds the POC-04 debug room from the numbers in
    /// <c>docs/space/requirements.md</c>. The room is a function of the spec, not
    /// hand-placed geometry: change a constant below, re-run, get the new room.
    /// That is the whole point of it being a script — S3.1 marks these dimensions
    /// provisional, and provisional geometry has to be cheap to rebuild.
    /// </summary>
    public static class RoomBuilder
    {
        // ---- S1: metric standard -------------------------------------------
        const float Unit           = 1f;    // 1 unit = 1 metre (S1.1)
        const float WallThickness  = 0.2f;  // solid boxes, never planes (S1.3)

        // ---- S3: the room (provisional, S3.1) ------------------------------
        const float RoomInternal   = 10f;   // 10 x 10 m internal
        const float CeilingHeight  = 3.2f;

        // ---- S2: the player ------------------------------------------------
        const float PlayerHeight   = 1.8f;
        const float PlayerRadius   = 0.3f;
        const float EyeHeight      = 1.65f;
        const float CameraFov      = 60f;

        // ---- S4.2: flat calm neutral pastel sky, no HDRI, no baking --------
        static readonly Color SkyPastel = new Color(0.780f, 0.816f, 0.847f, 1f);
        static readonly Color Ambient   = new Color(0.560f, 0.570f, 0.580f, 1f);

        /// <summary>Paper stock, composited into each sheet's texture as its margin (R3.3
        /// will replace this with authored stock blended by condition).</summary>
        static readonly Color PaperStock = new Color(0.92f, 0.89f, 0.82f, 1f);

        const string Root      = "Assets/Archivist/Building";
        const string MatDir    = Root + "/Materials";
        const string TexDir    = Root + "/Textures";
        const string PrefabDir = Root + "/Prefabs";
        const string SceneDir  = Root + "/Scenes";
        const string ScenePath = SceneDir + "/POC04_Room.unity";
        const string RoomPrefab = PrefabDir + "/PF_Archive_Room_Debug.prefab";
        const string InputAsset = "Assets/InputSystem_Actions.inputactions";

        // ---- POC-05: interaction ------------------------------------------
        const float InteractReach = 2.5f;    // 'close enough' and 'aimed at' are one test
        const float CrateSize     = 0.5f;
        static readonly Vector3 CratePosition = new Vector3(0f, CrateSize / 2f, 0.5f);

        [MenuItem("Archivist/Build POC-04 Room")]
        public static void Build()
        {
            EnsureFolders();
            EnsureMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var roomAsset = BuildRoomPrefab();
            var room = (GameObject)PrefabUtility.InstantiatePrefab(roomAsset);
            room.name = "Archive_Room_Debug";

            InteractionPrompt prompt = BuildInteractionUi();
            BuildPlayer(prompt);

            IslandGenerator generator;
            SheetSpawner spawner;
            BuildGenerator(out generator, out spawner);
            BuildMapCrate(generator, spawner);

            ApplyEnvironment();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[RoomBuilder] Built {ScenePath} — {RoomInternal}x{RoomInternal}m internal, {CeilingHeight}m ceiling.");
        }

        // --------------------------------------------------------------------

        static void EnsureFolders()
        {
            foreach (var d in new[] { MatDir, TexDir, PrefabDir, SceneDir })
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
        }

        static void EnsureMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new System.Exception("URP Lit shader not found.");

            MakeFlat(lit, "M_Placeholder_Floor",   new Color(0.26f, 0.25f, 0.24f));
            MakeFlat(lit, "M_Placeholder_Wall",    new Color(0.72f, 0.69f, 0.63f));
            MakeFlat(lit, "M_Placeholder_Ceiling", new Color(0.86f, 0.85f, 0.82f));
            MakeFlat(lit, "M_Placeholder_Crate",   new Color(0.55f, 0.42f, 0.24f));

            // One material for the whole sheet. Paper and map are composited into a single
            // texture, so there is no second surface to tint, order or offset. White base:
            // the texture supplies all the colour. Every spawned sheet instances this one.
            MakeFlat(lit, "M_Sheet", Color.white);

            MakeChecker(lit);
        }

        static void MakeFlat(Shader lit, string name, Color tint)
        {
            var path = $"{MatDir}/{name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;

            var m = new Material(lit);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", 0.05f);
            m.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(m, path);
        }

        /// <summary>
        /// S6.2 — a measuring instrument, not art. 512 px/m, 2 m tile, 0.5 m
        /// squares. If an imported asset is the wrong scale, this is what makes
        /// it visible instead of merely wrong.
        /// </summary>
        static void MakeChecker(Shader lit)
        {
            var matPath = $"{MatDir}/M_Placeholder_Checker.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null) return;

            var texPath = $"{TexDir}/T_Placeholder_Checker_BC.png";
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

            var chk = new Material(lit);
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

            var structure = NewChild("Structure", root.transform);
            NewChild("Openings", root.transform);                 // doors/windows, later
            var lighting  = NewChild("Lighting", root.transform);
            NewChild("Anchors", root.transform);                  // S3.4 — where furniture will land

            float span   = RoomInternal + 2f * WallThickness;     // slabs run under the walls
            float half   = RoomInternal / 2f + WallThickness / 2f;
            float wallMidY = CeilingHeight / 2f;

            // Floor: top face sits exactly at y = 0 (S3.3)
            Slab(structure.transform, "Floor",
                 new Vector3(span, WallThickness, span),
                 new Vector3(0f, -WallThickness / 2f, 0f),
                 "M_Placeholder_Floor", "Ground");

            Slab(structure.transform, "Ceiling",
                 new Vector3(span, WallThickness, span),
                 new Vector3(0f, CeilingHeight + WallThickness / 2f, 0f),
                 "M_Placeholder_Ceiling", "Structure");

            Slab(structure.transform, "Wall_North",
                 new Vector3(span, CeilingHeight, WallThickness),
                 new Vector3(0f, wallMidY, half), "M_Placeholder_Wall", "Structure");

            Slab(structure.transform, "Wall_South",
                 new Vector3(span, CeilingHeight, WallThickness),
                 new Vector3(0f, wallMidY, -half), "M_Placeholder_Wall", "Structure");

            Slab(structure.transform, "Wall_East",
                 new Vector3(WallThickness, CeilingHeight, span),
                 new Vector3(half, wallMidY, 0f), "M_Placeholder_Wall", "Structure");

            Slab(structure.transform, "Wall_West",
                 new Vector3(WallThickness, CeilingHeight, span),
                 new Vector3(-half, wallMidY, 0f), "M_Placeholder_Wall", "Structure");

            // S4.2 — one low directional light so surfaces read as separate
            // planes. Not a lighting design; there is no lighting design yet.
            var lightGo = NewChild("Light_Directional", lighting.transform);
            lightGo.transform.rotation = Quaternion.Euler(52f, -34f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.9f;
            light.color = new Color(1f, 0.98f, 0.95f);
            light.shadows = LightShadows.Soft;

            var asset = PrefabUtility.SaveAsPrefabAsset(root, RoomPrefab);
            Object.DestroyImmediate(root);
            return asset;
        }

        static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static void Slab(Transform parent, string name, Vector3 size, Vector3 centre, string material, string layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);   // brings its own BoxCollider
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size * Unit;
            go.layer = LayerMask.NameToLayer(layer);

            go.GetComponent<MeshRenderer>().sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{material}.mat");

            // Batching only. No ContributeGI: POC-04 does not bake (S4.2/S4.3).
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        }

        // --------------------------------------------------------------------

        static void BuildPlayer(InteractionPrompt prompt)
        {
            var player = new GameObject("Player");
            player.layer = LayerMask.NameToLayer("Player");
            player.tag = "Player";
            player.transform.position = new Vector3(0f, 0.05f, -3.5f);   // just clear of the floor

            var cc = player.AddComponent<CharacterController>();
            cc.height = PlayerHeight;
            cc.radius = PlayerRadius;
            cc.center = new Vector3(0f, PlayerHeight / 2f, 0f);
            cc.slopeLimit = 45f;
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.02f;

            var eye = new GameObject("Eye");
            eye.transform.SetParent(player.transform, false);
            eye.transform.localPosition = new Vector3(0f, EyeHeight, 0f);

            var cam = eye.AddComponent<Camera>();
            cam.fieldOfView = CameraFov;             // vertical FOV (S2.3)
            cam.nearClipPlane = 0.05f;
            // 50 m, not 200: the room is 10 m across, and a tighter near:far ratio is free
            // depth precision for things lying millimetres apart on the floor.
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = SkyPastel;
            eye.tag = "MainCamera";
            eye.AddComponent<AudioListener>();

            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAsset);

            var fpc = player.AddComponent<FirstPersonController>();
            var so = new SerializedObject(fpc);
            so.FindProperty("inputActions").objectReferenceValue = actions;
            so.FindProperty("eye").objectReferenceValue = eye.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            var interactor = player.AddComponent<PlayerInteractor>();
            var iso = new SerializedObject(interactor);
            iso.FindProperty("reach").floatValue = InteractReach;
            // Everything except the player itself blocks the ray, so nothing is reachable
            // through a wall and no interactable needs a special case for it.
            iso.FindProperty("blockers").intValue = ~(1 << LayerMask.NameToLayer("Player"));
            iso.FindProperty("eye").objectReferenceValue = eye.transform;
            iso.FindProperty("inputActions").objectReferenceValue = actions;
            iso.FindProperty("prompt").objectReferenceValue = prompt;
            iso.ApplyModifiedPropertiesWithoutUndo();
        }

        // --------------------------------------------------------------------

        /// <summary>
        /// Reticle and aim label. The only screen text the POC has — T2 rules out counters
        /// and readouts, not a verb telling you what the key does.
        /// </summary>
        static InteractionPrompt BuildInteractionUi()
        {
            var canvasGo = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var reticle = new GameObject("Reticle").AddComponent<Image>();
            reticle.transform.SetParent(canvasGo.transform, false);
            reticle.color = new Color(1f, 1f, 1f, 0.5f);
            reticle.raycastTarget = false;
            Centre(reticle.rectTransform, new Vector2(4f, 4f), Vector2.zero);

            var promptGo = new GameObject("Prompt");
            promptGo.transform.SetParent(canvasGo.transform, false);

            var text = promptGo.AddComponent<Text>();           // brings its own RectTransform
            text.font = BuiltinFont();
            text.fontSize = 26;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            Centre(text.rectTransform, new Vector2(720f, 44f), new Vector2(0f, -76f));

            var group = promptGo.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;

            var prompt = promptGo.AddComponent<InteractionPrompt>();
            var pso = new SerializedObject(prompt);
            pso.FindProperty("group").objectReferenceValue = group;
            pso.FindProperty("label").objectReferenceValue = text;
            pso.ApplyModifiedPropertiesWithoutUndo();

            return prompt;
        }

        static void Centre(RectTransform rt, Vector2 size, Vector2 offset)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
        }

        static Font BuiltinFont()
        {
            Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return f != null ? f : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        // --------------------------------------------------------------------

        /// <summary>
        /// The generator and the two things it remembers, each its own object so each can
        /// grow a scope without the others: <b>IslandCache</b> is where a generated island is
        /// stored, <b>SheetLedger</b> is what has been issued. Split because they have
        /// opposite lifetimes — the cache can be thrown away at any moment for nothing worse
        /// than a re-generation, and the ledger cannot be lost at all without breaking R2.10.
        ///
        /// <para>None of it is geometry and none of it belongs to a room, so none of it is
        /// attached to anything physical. The spawner is a separate root: putting sheets on
        /// the floor is a world concern, not a collection one.</para>
        /// </summary>
        static void BuildGenerator(out IslandGenerator generator, out SheetSpawner spawner)
        {
            var root = new GameObject("Generator");
            generator = root.AddComponent<IslandGenerator>();

            var cacheGo = new GameObject("IslandCache");
            cacheGo.transform.SetParent(root.transform, false);
            IslandCache cache = cacheGo.AddComponent<IslandCache>();

            var ledgerGo = new GameObject("SheetLedger");
            ledgerGo.transform.SetParent(root.transform, false);
            SheetLedger ledger = ledgerGo.AddComponent<SheetLedger>();

            var gso = new SerializedObject(generator);
            gso.FindProperty("cache").objectReferenceValue = cache;
            gso.FindProperty("ledger").objectReferenceValue = ledger;
            gso.ApplyModifiedPropertiesWithoutUndo();

            spawner = new GameObject("SheetSpawner").AddComponent<SheetSpawner>();

            var so = new SerializedObject(spawner);
            so.FindProperty("sheetMaterial").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Sheet.mat");
            so.FindProperty("paperTint").colorValue = PaperStock;
            so.FindProperty("floorY").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildMapCrate(IslandGenerator generator, SheetSpawner spawner)
        {
            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "MapCrate";
            crate.transform.position = CratePosition;
            crate.transform.localScale = Vector3.one * CrateSize;
            crate.layer = LayerMask.NameToLayer("Item");
            crate.GetComponent<MeshRenderer>().sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Placeholder_Crate.mat");

            // Sheets land beyond the crate, so opening one does not bury the player's feet.
            var drop = new GameObject("DropAnchor");
            drop.transform.SetParent(crate.transform, false);
            drop.transform.position = new Vector3(CratePosition.x, 0f, CratePosition.z);

            var mapCrate = crate.AddComponent<MapCrate>();
            var so = new SerializedObject(mapCrate);
            so.FindProperty("label").stringValue = "Create map";
            so.FindProperty("generator").objectReferenceValue = generator;
            so.FindProperty("spawner").objectReferenceValue = spawner;
            so.FindProperty("dropAnchor").objectReferenceValue = drop.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ApplyEnvironment()
        {
            // S4.2 — flat pastel sky, flat ambient, nothing else. Deliberately
            // not a look: art direction (S4.1) is deferred.
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Ambient;
            RenderSettings.fog = false;
        }
    }
}
