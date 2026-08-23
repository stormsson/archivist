using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Archivist.Building.Collection;
using Archivist.Building.Handling;
using Archivist.Building.Interaction;
using Archivist.Building.Table;

namespace Archivist.Building.Editor
{
    /// <summary>
    /// Builds the cartography table's runtime rig into the open scene: a
    /// <see cref="BoardView"/>, a <see cref="TableCanvas"/>, and the
    /// <see cref="TableSession"/> that switches between them and the room.
    ///
    /// <para><b>A script, not hand-placement</b>, for the reason <c>RoomBuilder</c> exists: the
    /// numbers here are provisional and have to be cheap to rebuild. Wiring three components by
    /// dragging in the inspector is a thing nobody can review, nobody can repeat, and nobody
    /// notices has drifted.</para>
    ///
    /// <para><b>Idempotent.</b> Re-running finds what is already there and re-wires it rather
    /// than making a second one. Three sessions in a scene would fight over which of them owns
    /// the player's components (C8.4), and the symptom — a room that stays disabled after
    /// closing the table — looks nothing like its cause.</para>
    ///
    /// <para>It does <b>not</b> save the scene. Building a rig and committing it are separate
    /// decisions, and the second one is the human's.</para>
    /// </summary>
    public static class CartographyRigBuilder
    {
        [MenuItem("Archivist/Cartography Table · Build Rig")]
        public static void Build()
        {
            var generator = Object.FindFirstObjectByType<IslandGenerator>();
            if (generator == null)
            {
                Debug.LogError("[RigBuilder] Open POC04_Room first — this needs the scene's IslandGenerator.");
                return;
            }

            BoardView board = Ensure<BoardView>("BoardView");
            TableCanvas canvas = Object.FindFirstObjectByType<TableCanvas>(FindObjectsInactive.Include);
            if (canvas == null) canvas = TableCanvas.Create();

            BoardInteractor interactor = Ensure<BoardInteractor>("BoardInteractor");
            TableSession session = Ensure<TableSession>("TableSession");

            // Everything below is serialized private state, so it is set the way the inspector
            // would set it. SerializedObject rather than reflection: it goes through Unity's own
            // undo and dirty tracking, so the scene records the change like any other edit.
            Wire(board, new[]
            {
                ("generator", (Object)generator),
                ("options", (Object)LoadOptions()),
                ("unlitMaterial", (Object)null),   // BoardView builds its own when this is null
            });

            Wire(interactor, new[]
            {
                ("board", (Object)board),
                ("options", (Object)LoadOptions()),
                ("inputActions", (Object)LoadInputActions()),
            });

            // The canvas needs the interactor: a row click selects on the BOARD and the header
            // follows from SelectionChanged, so that one thing is the authority (C7.6).
            Wire(canvas, new[]
            {
                ("generator", (Object)generator),
                ("interactor", (Object)interactor),
            });

            Wire(session, new[]
            {
                ("board", (Object)board),
                ("tableCanvas", (Object)canvas),
                ("generator", (Object)generator),
                ("controller", (Object)Object.FindFirstObjectByType<FirstPersonController>()),
                ("interactor", (Object)Object.FindFirstObjectByType<PlayerInteractor>()),
                ("hands", (Object)Object.FindFirstObjectByType<PlayerHands>()),
                ("inputActions", (Object)LoadInputActions()),
            });

            Selection.activeGameObject = session.gameObject;
            Debug.Log("[RigBuilder] Rig ready: BoardView, BoardInteractor, TableCanvas and TableSession wired. " +
                      "Aim at the table and press the interact key, or press C. Esc closes. " +
                      "The scene is NOT saved.", session);
        }

        /// <summary>
        /// Opens the table from a menu, in play mode, without a keypress.
        ///
        /// <para>Exists because the two real ways in — aiming at the table, or the C shortcut —
        /// both need input, and input is exactly what a harness cannot supply. This drives the
        /// same <c>TableSession.OpenCurrentIsland</c> the keypress does, so a table that opens
        /// here opens there; it is not a parallel path.</para>
        /// </summary>
        /// <summary>
        /// Issues sheets into the ledger <b>without rendering any of them</b>, so a table has
        /// something to show.
        ///
        /// <para>Exists because <c>Quick · Draw Crate</c> generates an island and rasterises
        /// five sheets <i>synchronously on the main thread</i> — which is fine when a human
        /// clicks it and fatal to a harness: it blocks long enough that the editor stops
        /// answering and, in play mode, cannot be asked to stop again. Nothing on the cabinet
        /// needs a raster to exist: it lists <see cref="SheetId"/>s, and
        /// <c>BoardView</c> renders what it needs itself, off-thread, one upload per frame
        /// (C5.6). So issuing is all that is actually required, and issuing is free.</para>
        ///
        /// <para>It goes through <c>SheetPicker</c> and <c>SheetLedger.MarkIssued</c> — the same
        /// calls the crate makes — so R2.10 still holds and nothing is issued twice.</para>
        /// </summary>
        [MenuItem("Archivist/Cartography Table · Issue Sheets (no render)")]
        public static void IssueSheets()
        {
            var generator = Object.FindFirstObjectByType<IslandGenerator>();
            if (generator == null) { Debug.LogError("[RigBuilder] No IslandGenerator in the scene."); return; }

            ulong seed = generator.LastIslandSeed != 0
                ? generator.LastIslandSeed
                : generator.ReserveNextIslandSeed();

            Archivist.Generation.Island island = generator.GetOrGenerate(seed);
            generator.Ledger.Describe(island);

            var already = generator.Ledger.Snapshot(seed);
            var picks = SheetPicker.PickUnissued(island, 12, already, unchecked((int)seed));

            int issued = 0;
            for (int i = 0; i < picks.Count; i++)
                if (generator.Ledger.MarkIssued(SheetId.Of(picks[i]))) issued++;

            IslandHolding holding;
            generator.Ledger.TryGetHolding(seed, out holding);
            Debug.Log($"[RigBuilder] Issued {issued} sheet(s) of {island.Name} — {holding}");
        }

        [MenuItem("Archivist/Cartography Table · Open Now")]
        public static void OpenNow()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RigBuilder] Enter play mode first — the board builds itself " +
                                 "in a coroutine, and coroutines do not tick in edit mode.");
                return;
            }

            TableSession session = TableSession.InScene;
            if (session == null) { Debug.LogError("[RigBuilder] No TableSession — build the rig first."); return; }

            session.OpenCurrentIsland();
        }

        [MenuItem("Archivist/Cartography Table · Close Now")]
        public static void CloseNow()
        {
            TableSession session = TableSession.Active;
            if (session == null) { Debug.Log("[RigBuilder] No table is open."); return; }
            session.Close();
        }

        /// <summary>Finds the one in the scene or makes it. Includes inactive objects: the canvas
        /// and the board are switched off between openings, and a search that skipped them would
        /// build a second copy every time.</summary>
        static T Ensure<T>(string name) where T : Component
        {
            T found = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
            if (found != null) return found;

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Build Cartography Rig");
            return go.AddComponent<T>();
        }

        static void Wire(Component target, (string field, Object value)[] links)
        {
            if (target == null) return;

            var so = new SerializedObject(target);
            for (int i = 0; i < links.Length; i++)
            {
                SerializedProperty property = so.FindProperty(links[i].field);
                if (property == null)
                {
                    Debug.LogWarning($"[RigBuilder] {target.GetType().Name} has no field " +
                                     $"'{links[i].field}' — renamed?", target);
                    continue;
                }
                property.objectReferenceValue = links[i].value;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        static TableOptions LoadOptions()
        {
            string[] found = AssetDatabase.FindAssets("t:TableOptions");
            return found.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<TableOptions>(AssetDatabase.GUIDToAssetPath(found[0]));
        }

        /// <summary>The one asset carrying all three maps. Named by content rather than by path,
        /// so moving the file does not silently leave the session with no input.</summary>
        static InputActionAsset LoadInputActions()
        {
            string[] found = AssetDatabase.FindAssets("t:InputActionAsset");
            for (int i = 0; i < found.Length; i++)
            {
                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                    AssetDatabase.GUIDToAssetPath(found[i]));
                if (asset == null) continue;

                if (asset.FindActionMap("Player", false) != null
                 && asset.FindActionMap("Table", false) != null
                 && asset.FindActionMap("UI", false) != null)
                    return asset;
            }
            Debug.LogWarning("[RigBuilder] No InputActionAsset with Player, Table and UI maps.");
            return null;
        }
    }
}
