using UnityEngine;

namespace Archivist.Building.Table
{
    /// <summary>
    /// C6.8's selection rim, taken literally: one quad at <see cref="Scale"/> with an unlit gold
    /// material, enabled and disabled. No shader, no outline pass, no second camera.
    ///
    /// <para><b>It draws; it does not decide.</b> Which pose, and which of the two golds, is
    /// <c>BoardInteractor</c>'s answer — the rim's colour is a function of the same
    /// <c>snapping</c> the release acts on, and splitting that judgement across two files is how
    /// the board starts promising joins it will not make.</para>
    ///
    /// <para><b>One object, reparented, rather than one per slab.</b> Exactly one sheet is
    /// selected at a time, so N-1 would always be off, and a quad living under a slab dies with
    /// it when the sheet is refiled. It hangs off the board root and is given the slab's local
    /// pose.</para>
    ///
    /// <para><b>It shares the slab's mesh</b> — every slab is a different size (F-S1.4), so its
    /// own mesh would be rebuilt on every selection change. Shared, never owned:
    /// <c>BoardSheetView</c> destroys that mesh in <c>OnDestroy</c>, so the reference is dropped
    /// the moment the outline is hidden.</para>
    ///
    /// <para><b>It sits <i>under</i> the slab</b>, by <see cref="Drop"/> of the separation.
    /// Coplanar quads z-fight; above the slab the 1.02 rim would be right but the middle would
    /// strobe over the map. Under it, the slab covers the middle and only the rim shows, which
    /// is what an outline is. A fraction of the separation rather than an absolute nudge keeps
    /// the quad inside its own slab's slot in the draw-order stack of §3.3, so it can never
    /// surface through the sheet stacked below it.</para>
    /// </summary>
    public sealed class BoardOutline
    {
        /// <summary>C8.8 / C5.1's layer. Must match <c>BoardView</c>'s and <c>SnapHint</c>'s —
        /// the board camera renders only this layer.</summary>
        const string TableLayerName = "Table";

        /// <summary>C6.8's "~1.02". Applied in X and Z only: the quad shares the slab's flat
        /// mesh, which has no Y extent to scale.</summary>
        public const float Scale = 1.02f;

        /// <summary>How far under the slab the quad sits, as a fraction of
        /// <c>SheetSeparation</c>.</summary>
        public const float Drop = 0.15f;

        GameObject quad;
        MeshFilter filter;
        MeshRenderer renderer;
        Material material;

        /// <summary>
        /// Shows the rim at a pose in the board root's local space, borrowing <paramref
        /// name="mesh"/> from the slab it outlines. A null mesh leaves the quad built and
        /// disabled — a slab whose raster has not landed yet (C5.7) is selected, just not drawn
        /// round.
        ///
        /// <para>Rebuilt rather than kept when the root changes: <c>BoardView.Hide</c> destroys
        /// the whole rig, taking any child of the root with it, so this cannot assume it
        /// survived the last close.</para>
        /// </summary>
        public void Place(Transform boardRoot, Mesh mesh, Vector3 localPosition,
                          Quaternion localRotation, Vector3 localScale, Color colour)
        {
            if (boardRoot == null) { Hide(); return; }

            if (quad == null || quad.transform.parent != boardRoot) Build(boardRoot);

            filter.sharedMesh = mesh;
            renderer.enabled = mesh != null;
            material.color = colour;

            quad.transform.localPosition = localPosition;
            quad.transform.localRotation = localRotation;
            quad.transform.localScale = localScale;
        }

        /// <summary>Goes dark, and drops the borrowed mesh — it belongs to a slab that may be
        /// destroyed this frame.</summary>
        public void Hide()
        {
            if (renderer != null) renderer.enabled = false;
            if (filter != null) filter.sharedMesh = null;
        }

        /// <summary>Destroys the quad and the material. Called from the driver's
        /// <c>OnDestroy</c>; the material is <c>DontSave</c> and would otherwise outlive the
        /// domain it was made in.</summary>
        public void Dispose()
        {
            Discard(quad);
            Discard(material);

            quad = null;
            filter = null;
            renderer = null;
            material = null;
        }

        void Build(Transform boardRoot)
        {
            Discard(quad);

            quad = new GameObject("SelectionOutline");
            quad.transform.SetParent(boardRoot, false);

            // The board camera's culling mask is the Table layer and nothing else (C5.1), so an
            // outline on the default layer is built, positioned, enabled — and invisible.
            int layer = LayerMask.NameToLayer(TableLayerName);
            if (layer >= 0) quad.layer = layer;

            filter = quad.AddComponent<MeshFilter>();
            renderer = quad.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (material == null)
            {
                // Unlit, for §3.4's reason: the board is independent of the room's lighting and
                // of where its root sits, and a lit gold would go black 500 units under the
                // floor.
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.name = "M_BoardOutline";
                material.hideFlags = HideFlags.DontSave;

                // URP's Unlit is OPAQUE by default and discards color.a. Without this, G7.5's
                // pulse computes a correct alpha every frame and renders as a slab that is
                // simply gold. Alpha 1 through a blended material is pixel-identical to the
                // opaque one, so both steady states are unchanged.
                SnapHint.MakeBlended(material);
            }

            renderer.sharedMaterial = material;
        }

        /// <summary>Destroy is illegal in edit mode, and the board rig is routinely built and
        /// torn down there by the bench.</summary>
        static void Discard(UnityEngine.Object thing)
        {
            if (thing == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(thing);
            else UnityEngine.Object.DestroyImmediate(thing);
        }
    }
}
