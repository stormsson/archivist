using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Archivist.Building.Shelving;

namespace Archivist.Building.Editor
{
    /// <summary>
    /// Makes a shelf's slots: one empty per grid position, each carrying the collider the player
    /// aims at, and an aim box.
    ///
    /// <para><b>Edit-time work, and it stays out of the runtime assembly.</b> It asks with a
    /// dialog, destroys through <c>Undo</c> and writes materials into the project — none of which
    /// exists in a player build. <see cref="Shelf"/> keeps the button, because that is where the
    /// numbers being tuned are, and forwards to the delegate installed here at load.</para>
    ///
    /// <para><b>Rebuilding wipes</b> — <see cref="Shelf"/>'s class comment carries the argument
    /// for that, and the dialog is the guard.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class ShelfGridBuilder
    {
        const string LitShader   = "Universal Render Pipeline/Lit";
        const string UnlitShader = "Universal Render Pipeline/Unlit";

        static ShelfGridBuilder() { Shelf.Builder = Rebuild; }

        internal static void Rebuild(Shelf shelf)
        {
            if (shelf == null) return;

            var existing = new List<ShelfSlot>();
            shelf.GetComponentsInChildren(true, existing);

            if (existing.Count > 0 &&
                !EditorUtility.DisplayDialog(
                    "Rebuild " + shelf.name + "?",
                    existing.Count + " slot(s) will be destroyed and rebuilt from the current " +
                    "numbers. Anything edited by hand — a moved slot, a deleted one, a binder " +
                    "standing in one — goes with them.",
                    "Rebuild", "Cancel"))
                return;

            for (int i = 0; i < existing.Count; i++)
                if (existing[i] != null)
                    Undo.DestroyObjectImmediate(existing[i].gameObject);

            Material aim = AimMaterial();

            for (int r = 0; r < shelf.RowAmount; r++)
                for (int c = 0; c < shelf.SlotsPerRow; c++)
                    BuildSlot(shelf, r, c, aim);

            shelf.Rescan();

            EditorUtility.SetDirty(shelf);
            EditorSceneManager.MarkSceneDirty(shelf.gameObject.scene);

            Debug.Log($"[Shelf] {shelf.name}: rebuilt {shelf.RowAmount * shelf.SlotsPerRow} " +
                      $"slot(s) — {shelf.RowAmount} x {shelf.SlotsPerRow}, " +
                      $"{shelf.SlotWidth:0.###} x {shelf.SlotHeight:0.###} x " +
                      $"{shelf.Depth:0.###} m.", shelf);
        }

        static void BuildSlot(Shelf shelf, int row, int column, Material aim)
        {
            var go = new GameObject("Slot_r" + (row + 1) + "c" + (column + 1));
            Undo.RegisterCreatedObjectUndo(go, "Rebuild shelf slots");

            go.transform.SetParent(shelf.transform, false);
            go.transform.localPosition = shelf.AnchorLocal(row, column);
            go.transform.localRotation = Quaternion.identity;
            go.layer = shelf.gameObject.layer;

            var slot = go.AddComponent<ShelfSlot>();
            slot.Configure(row, column, shelf.SlotWidth, shelf.SlotHeight, shelf.Depth);

            BuildAim(shelf, go.transform, aim);
        }

        /// <summary>
        /// The box that lights under the player's aim — the slot's only renderer.
        ///
        /// <para>A slot used to carry a second, always-visible cube so the grid could be seen
        /// while it was being tuned. The gizmos do that without costing a renderer per slot, and
        /// K1.1 counts what the cubes would come to across an archive.</para>
        /// </summary>
        static void BuildAim(Shelf shelf, Transform slot, Material material)
        {
            // A primitive brings its own collider; the slot's own box is the one the player aims
            // at, and a second one here would be a target that comes and goes with a highlight.
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());

            Vector3 centre, size;
            ShelfSlot.SlotBox(shelf.SlotWidth, shelf.SlotHeight, shelf.Depth, out centre, out size);

            cube.name = ShelfSlot.AimName;
            cube.transform.SetParent(slot, false);
            cube.transform.localPosition = centre;
            cube.transform.localScale = size * ShelfSlot.AimSwell;
            cube.layer = shelf.gameObject.layer;

            var renderer = cube.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Dark until aimed at: a shelf that lit every box at once would say nothing about
            // which slot is under the pointer.
            renderer.enabled = false;

            if (material != null) renderer.sharedMaterial = material;
        }

        /// <summary>
        /// The aim light's material — <b>Unlit</b>, deliberately.
        ///
        /// <para>A highlight that took the room's lighting would be dimmest exactly where the
        /// shelves are darkest, which is where a player most needs to see which slot they are
        /// pointing at. Its colour is written per slot through a property block, so the white
        /// here is only what an untinted one would be.</para>
        /// </summary>
        static Material AimMaterial()
        {
            return SceneParts.MakeTranslucent(Shader.Find(UnlitShader), "M_ShelfSlot_Aim",
                                              new Color(1f, 1f, 1f, 0.35f));
        }
    }
}
