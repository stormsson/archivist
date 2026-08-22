using System.Collections.Generic;
using UnityEngine;

namespace Archivist.Building.Sheets
{
    /// <summary>
    /// Puts rendered sheets on the floor. R4.7 makes a floor pile a legitimate place for
    /// things to be, and §4.3 makes it the first thing that happens to a crate's contents —
    /// so this is not a placeholder for a proper inventory. It is the destination.
    /// </summary>
    public sealed class SheetSpawner : MonoBehaviour
    {
        [Header("Materials")]
        [SerializeField] Material sheetMaterial;
        [Tooltip("Shader texture property the composed sheet goes into. URP Lit uses _BaseMap.")]
        [SerializeField] string mapTextureProperty = "_BaseMap";
        [Tooltip("Paper stock. Composited into the texture as the margin, not drawn as a second surface.")]
        [SerializeField] Color paperTint = new Color(0.92f, 0.89f, 0.82f);

        [Header("Layout")]
        [SerializeField] float floorY;
        [SerializeField] int columns = 3;
        [SerializeField] float columnSpacing = 1.15f;
        [SerializeField] float rowSpacing = 1.1f;
        [Tooltip("Metres from the anchor to the first row.")]
        [SerializeField] float firstRowOffset = 1.1f;
        [Tooltip("Degrees of scatter, so a pile reads as dropped rather than laid out.")]
        [SerializeField] float rotationJitter = 14f;
        [SerializeField] string sheetLayer = "Item";

        [Header("Stacking")]
        [Tooltip("Clearance between the floor and the first sheet's underside, in metres.")]
        [SerializeField] float liftOff = 0.0004f;
        [Tooltip("Air between one sheet's top face and the next sheet's underside, in metres.")]
        [SerializeField] float separation = 0.0008f;

        readonly List<SheetView> spawned = new List<SheetView>();

        public IReadOnlyList<SheetView> Spawned { get { return spawned; } }

        /// <summary>
        /// One sheet, positioned by its index within the batch. Called once per frame by the
        /// crate rather than in a loop: each call uploads a texture, and five uploads in one
        /// frame is a visible hitch in a game whose entire tone is "calm" (T5).
        /// </summary>
        public SheetView Place(SheetRender render, int index, int total, Transform anchor)
        {
            // Height comes from the running total, NOT from the batch index. The index
            // restarts at zero every opening, so using it put the first sheet of every batch
            // at exactly the same height as the first sheet of the last one — coplanar, and
            // guaranteed to fight. The pile only ever grows upward.
            int inPile = spawned.Count;

            SheetView view = SheetView.Create(render, sheetMaterial, paperTint, mapTextureProperty);

            int layer = LayerMask.NameToLayer(sheetLayer);
            if (layer >= 0) SetLayerRecursive(view.gameObject, layer);

            // A spawned sheet is NEVER written into a scene file. The ledger is the only
            // record that a sheet has been issued, and it does not survive a scene load —
            // so a sheet that did survive one would exist with nothing recording it, and
            // could be issued a second time. R2.10 says that must be impossible, so the
            // guarantee is made structural rather than left to whoever presses Ctrl-S.
            view.gameObject.hideFlags = HideFlags.DontSaveInEditor;

            int row = index / columns;
            int col = index % columns;
            int inThisRow = Mathf.Min(columns, total - row * columns);

            float x = (col - (inThisRow - 1) * 0.5f) * columnSpacing;
            float z = firstRowOffset + row * rowSpacing;

            Vector3 local = new Vector3(x, 0f, z);
            Vector3 world = anchor != null ? anchor.position + anchor.rotation * local : local;

            // Every sheet is a solid 1.5 mm slab, so a pile is separated in actual space
            // rather than by an offset chosen to be just big enough.
            world.y = floorY + liftOff + inPile * (SheetView.Thickness + separation);

            // Deterministic scatter: the same sheet always lands the same way, which makes a
            // reported layout reproducible.
            float jitter = (Mathf.Abs(render.Id.GetHashCode() % 1000) / 1000f - 0.5f) * 2f * rotationJitter;
            float facing = anchor != null ? anchor.eulerAngles.y : 0f;

            view.transform.SetPositionAndRotation(world, Quaternion.Euler(0f, facing + jitter, 0f));

            spawned.Add(view);
            return view;
        }

        /// <summary>
        /// Removes every sheet this spawner has placed. Does not touch the ledger: what is
        /// on the floor and what has been issued are different facts, and clearing the floor
        /// is not un-issuing anything.
        /// </summary>
        public void ClearAll()
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] == null) continue;

                if (Application.isPlaying) Destroy(spawned[i].gameObject);
                else DestroyImmediate(spawned[i].gameObject);
            }
            spawned.Clear();
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }
    }
}
