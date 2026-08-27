using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Archivist.Building.Binders;
using Archivist.Building.Collection;
using Archivist.Building.Handling;
using Archivist.Building.Interaction;
using Archivist.Building.Interactables;
using Archivist.Building.Sheets;

namespace Archivist.Building.Editor
{
    /// <summary>
    /// Everything a scene needs before anything in it can be pressed: the player, the aim
    /// prompt, the collection, and the crate that draws from it. The numbers come from
    /// <c>docs/space/requirements.md</c>, and they describe the player and the paper rather
    /// than any particular room.
    ///
    /// <para><b>Shared, so a second scene is the same rig and not a copy of one.</b> A bench
    /// that builds its own player proves only that the bench works — the same rule that keeps
    /// <see cref="CartographyBoardBench"/> on the shipping path. Two players wired slightly
    /// differently is a bug that only ever reproduces in one scene.</para>
    ///
    /// <para>Room geometry is not here. Walls, ceiling and the checker are
    /// <see cref="RoomBuilder"/>'s, because they are what makes a room a room.</para>
    /// </summary>
    internal static class SceneParts
    {
        // ---- S1: metric standard -------------------------------------------
        internal const float Unit          = 1f;    // 1 unit = 1 metre (S1.1)
        internal const float WallThickness = 0.2f;  // solid boxes, never planes (S1.3)

        // ---- S2: the player ------------------------------------------------
        internal const float PlayerHeight = 1.8f;
        internal const float PlayerRadius = 0.3f;
        internal const float EyeHeight    = 1.65f;
        internal const float CameraFov    = 60f;

        // ---- S4.2: flat calm neutral pastel sky, no HDRI, no baking --------
        internal static readonly Color SkyPastel = new Color(0.780f, 0.816f, 0.847f, 1f);
        internal static readonly Color Ambient   = new Color(0.560f, 0.570f, 0.580f, 1f);

        /// <summary>Paper stock, composited into each sheet's texture as its margin (R3.3
        /// will replace this with authored stock blended by condition).</summary>
        internal static readonly Color PaperStock = new Color(0.92f, 0.89f, 0.82f, 1f);

        internal const string Root      = "Assets/Archivist/Building";
        internal const string MatDir    = Root + "/Materials";
        internal const string TexDir    = Root + "/Textures";
        internal const string PrefabDir = Root + "/Prefabs";
        internal const string SceneDir  = Root + "/Scenes";
        internal const string OptionsDir = Root + "/Options";
        internal const string HandlingOptionsPath = OptionsDir + "/HandlingOptions.asset";
        internal const string BinderPrefabPath = PrefabDir + "/PF_Binder.prefab";
        internal const string InputAsset = "Assets/InputSystem_Actions.inputactions";

        // ---- POC-05: interaction ------------------------------------------
        // 'Close enough' and 'aimed at' are one test. 2.5 m was chosen for things at hand
        // height and is too short for the floor: a sheet lies 1.65 m below the eye, so
        // sqrt(2.5^2 - 1.65^2) = 1.88 m is all the forward reach that leaves, and sheets are
        // laid out to ~2.5 m. 3.0 gives 2.50 m of floor reach. Provisional — the Sheet Test
        // Bench exposes this as a slider because it is a feel value, not a derivation.
        internal const float InteractReach = 3.0f;
        internal const float CrateSize     = 0.5f;
        internal static readonly Vector3 CratePosition = new Vector3(0f, CrateSize / 2f, 0.5f);

        /// <summary>Where the player stands at the start: back from the crate, facing it.</summary>
        internal static readonly Vector3 PlayerStart = new Vector3(0f, 0.05f, -3.5f);

        // ---- POC-06: the carried pose -------------------------------------
        // Right of centre and tilted, because the left half of the view is spoken for —
        // that is where the journal and whatever else the player holds will go. These are
        // a starting point: the anchor is a transform in the scene and moving it is the
        // tuning loop.
        static readonly Vector3 HoldPosition = new Vector3(0.36f, -0.09f, 1.15f);
        static readonly Vector3 HoldRotation = new Vector3(-77f, -12f, 5f);

        // --------------------------------------------------------------------

        internal static void EnsureFolders()
        {
            foreach (var d in new[] { MatDir, TexDir, PrefabDir, SceneDir, OptionsDir })
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
        }

        internal static void EnsureMaterials()
        {
            Shader lit = Lit();

            MakeFlat(lit, "M_Placeholder_Floor",   new Color(0.26f, 0.25f, 0.24f));
            MakeFlat(lit, "M_Placeholder_Wall",    new Color(0.72f, 0.69f, 0.63f));
            MakeFlat(lit, "M_Placeholder_Ceiling", new Color(0.86f, 0.85f, 0.82f));
            MakeFlat(lit, "M_Placeholder_Crate",   new Color(0.55f, 0.42f, 0.24f));

            // One material for the whole sheet. Paper and map are composited into a single
            // texture, so there is no second surface to tint, order or offset. White base:
            // the texture supplies all the colour. Every spawned sheet instances this one.
            MakeFlat(lit, "M_Sheet", Color.white);
        }

        internal static Shader Lit()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new System.Exception("URP Lit shader not found.");
            return lit;
        }

        internal static void MakeFlat(Shader lit, string name, Color tint)
        {
            var path = $"{MatDir}/{name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;

            var m = new Material(lit);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", 0.05f);
            m.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(m, path);
        }

        // --------------------------------------------------------------------

        internal static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        internal static void Slab(Transform parent, string name, Vector3 size, Vector3 centre,
                                  string material, string layer)
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

        /// <summary>
        /// S4.2 — one low directional light so surfaces read as separate planes. Not a
        /// lighting design; there is no lighting design yet.
        /// </summary>
        internal static Light KeyLight(Transform parent)
        {
            var go = NewChild("Light_Directional", parent);
            go.transform.rotation = Quaternion.Euler(52f, -34f, 0f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.9f;
            light.color = new Color(1f, 0.98f, 0.95f);
            light.shadows = LightShadows.Soft;
            return light;
        }

        // --------------------------------------------------------------------

        internal static PlayerHands BuildPlayer(InteractionPrompt prompt)
        {
            var player = new GameObject("Player");
            player.layer = LayerMask.NameToLayer("Player");
            player.tag = "Player";
            player.transform.position = PlayerStart;

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

            // The carried pose is a transform, not a constant: it is a feel question, and
            // feel questions get answered by dragging the thing, not by editing numbers.
            var hold = new GameObject("HoldAnchor");
            hold.transform.SetParent(eye.transform, false);
            hold.transform.localPosition = HoldPosition;
            hold.transform.localRotation = Quaternion.Euler(HoldRotation);

            var hands = player.AddComponent<PlayerHands>();
            var hso = new SerializedObject(hands);
            hso.FindProperty("holdAnchor").objectReferenceValue = hold.transform;
            hso.FindProperty("inputActions").objectReferenceValue = actions;
            hso.FindProperty("options").objectReferenceValue = EnsureHandlingOptions();
            // On by default while take/drop is being chased. One checkbox to silence.
            hso.FindProperty("logHandling").boolValue = true;
            hso.ApplyModifiedPropertiesWithoutUndo();

            return hands;
        }

        /// <summary>
        /// Reticle and aim label. The only screen text the POC has — T2 rules out counters
        /// and readouts, not a verb telling you what the key does.
        /// </summary>
        internal static InteractionPrompt BuildInteractionUi()
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

        /// <summary>
        /// Created once and never overwritten. These are feel values the project owner is
        /// expected to change by hand, and a rebuild of a scene must not undo that.
        /// </summary>
        static HandlingOptions EnsureHandlingOptions()
        {
            var existing = AssetDatabase.LoadAssetAtPath<HandlingOptions>(HandlingOptionsPath);
            if (existing != null) return existing;

            var options = ScriptableObject.CreateInstance<HandlingOptions>();
            AssetDatabase.CreateAsset(options, HandlingOptionsPath);
            return options;
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
        internal static void BuildGenerator(out IslandGenerator generator, out SheetSpawner spawner,
                                            out BinderSpawner binders)
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

            // A third root, beside the sheet spawner and for the same reason it is not part of
            // the generator: making binders is a world concern, not a collection one. It owns
            // the Binder_n counter, so it is one object and there is one of it.
            binders = new GameObject("BinderSpawner").AddComponent<BinderSpawner>();

            var bso = new SerializedObject(binders);
            bso.FindProperty("binderPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(BinderPrefabPath);
            bso.FindProperty("floorY").floatValue = 0f;
            bso.ApplyModifiedPropertiesWithoutUndo();

            if (bso.FindProperty("binderPrefab").objectReferenceValue == null)
                Debug.LogWarning($"[SceneParts] No binder prefab at {BinderPrefabPath}; " +
                                 "the crate will deliver nothing until one is wired.");
        }

        internal static MapCrate BuildMapCrate(IslandGenerator generator, SheetSpawner spawner,
                                               BinderSpawner binders)
        {
            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "MapCrate";
            crate.transform.position = CratePosition;
            crate.transform.localScale = Vector3.one * CrateSize;
            crate.layer = LayerMask.NameToLayer("Item");
            crate.GetComponent<MeshRenderer>().sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Placeholder_Crate.mat");

            // A delivery lands beyond the crate, so opening one does not bury the player's feet.
            var drop = new GameObject("DropAnchor");
            drop.transform.SetParent(crate.transform, false);
            drop.transform.position = new Vector3(CratePosition.x, 0f, CratePosition.z);

            var mapCrate = crate.AddComponent<MapCrate>();
            var so = new SerializedObject(mapCrate);
            so.FindProperty("label").stringValue = "Create map";
            so.FindProperty("generator").objectReferenceValue = generator;
            so.FindProperty("binders").objectReferenceValue = binders;
            so.FindProperty("spawner").objectReferenceValue = spawner;
            so.FindProperty("dropAnchor").objectReferenceValue = drop.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            return mapCrate;
        }

        internal static void ApplyEnvironment()
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
