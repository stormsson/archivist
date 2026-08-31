using UnityEngine;
using Archivist.Building.Handling;
using Archivist.Building.Table;

namespace Archivist.Building.Sheets
{
    /// <summary>
    /// Puts rendered sheets on the floor. R4.7 makes a floor pile a legitimate place for
    /// things to be, and §4.3 makes it the first thing that happens to a crate's contents —
    /// so this is not a placeholder for a proper inventory. It is the destination.
    /// </summary>
    public sealed class SheetSpawner : FloorPile<SheetView>
    {
        [Header("Materials")]
        [SerializeField] Material sheetMaterial;
        [Tooltip("Shader texture property the composed sheet goes into. URP Lit uses _BaseMap.")]
        [SerializeField] string mapTextureProperty = "_BaseMap";
        [Tooltip("Paper stock. Composited into the texture as the margin, not drawn as a second surface.")]
        [SerializeField] Color paperTint = new Color(0.92f, 0.89f, 0.82f);

        [Header("Layout")]
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

        protected override string LayerName { get { return sheetLayer; } }
        protected override float LiftOff { get { return liftOff; } }
        protected override float Separation { get { return separation; } }
        protected override SheetView[] Present { get { return AllInScene(); } }

        /// <summary>Every sheet actually on this floor. See <c>FloorPile.InScene</c>.</summary>
        public static SheetView[] AllInScene()
        {
            return InScene(OnTheFloor);
        }

        /// <summary>
        /// Board slabs are not paper on this floor (C5.4). A <c>SheetView</c> on the
        /// cartography board is the same component doing a different job, and every caller
        /// here would get it wrong: the startup sweep would destroy it, <see cref="Place"/>
        /// would count it into the floor pile's height, and <c>ClearAll</c> would take the
        /// board with the floor. See <c>BoardSheet</c>'s class comment for why the test is a
        /// marker component and not the Table layer.
        /// </summary>
        static bool OnTheFloor(SheetView view)
        {
            return view.GetComponent<BoardSheet>() == null;
        }

        /// <summary>
        /// One sheet, positioned by its index within the batch. Called once per frame by the
        /// crate rather than in a loop: each call uploads a texture, and five uploads in one
        /// frame is a visible hitch in a game whose entire tone is "calm" (T5).
        /// </summary>
        public SheetView Place(SheetRender render, int index, int total, Transform anchor)
        {
            // Height comes from how much paper is already down, NOT from the batch index.
            // The index restarts at zero every opening, so using it put the first sheet of
            // every batch at exactly the same height as the first sheet of the last one —
            // coplanar, and guaranteed to fight. Counted from the scene rather than the list
            // so a reload cannot restart the pile underneath surviving sheets.
            int inPile = AllInScene().Length;

            SheetView view = SheetView.Create(render, sheetMaterial, paperTint, mapTextureProperty);
            ApplyLayer(view.gameObject);

            // A sheet in the world can be taken. Nothing is handed to the verb — it asks
            // whoever aims at it.
            view.gameObject.AddComponent<SheetPickup>();

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

            float jitter = Scatter(render.Id.GetHashCode(), rotationJitter);
            float facing = anchor != null ? anchor.eulerAngles.y : 0f;

            view.transform.SetPositionAndRotation(world, Quaternion.Euler(0f, facing + jitter, 0f));

            Register(view);
            return view;
        }

        /// <summary>
        /// A sheet read back out of the save, at exactly the pose it was left at.
        ///
        /// <para><b>Not <see cref="Place"/>, and not <see cref="LayOnFloor"/>.</b> Place scatters
        /// by batch index and stacks by how much paper is already down, which is right for a
        /// delivery and wrong for a restore — the pile it computes is the pile it is halfway
        /// through rebuilding. LayOnFloor probes downward for what is already lying there, which
        /// is the same problem: restoring five sheets that were in one pile would put each on top
        /// of the last four and grow the stack by a centimetre. The file has the answer; this
        /// writes it.</para>
        ///
        /// <para>Everything else a sheet in the world needs is the same as
        /// <see cref="Place"/>'s: the layer, and the verb that lets it be picked up.</para>
        /// </summary>
        public SheetView Restore(SheetRender render, Vector3 position, Quaternion rotation)
        {
            SheetView view = SheetView.Create(render, sheetMaterial, paperTint, mapTextureProperty);
            ApplyLayer(view.gameObject);

            view.gameObject.AddComponent<SheetPickup>();
            view.transform.SetPositionAndRotation(position, rotation);

            Register(view);
            return view;
        }

        /// <summary>
        /// Lays a sheet flat at a point on the floor — where a carried sheet goes when it is
        /// dropped (R4.7). Unlike <see cref="Place"/> this takes a position rather than a
        /// slot in a batch, because the player chose it.
        ///
        /// <para>Height is found by looking down for paper already lying there and sitting on
        /// top of it. That is both what should visibly happen and what keeps a dropped sheet
        /// out of the plane of one already down — a running counter would work until a sheet
        /// were picked up and put back, which is exactly the case this method exists for.</para>
        /// </summary>
        public void LayOnFloor(SheetView view, Vector3 point, float yaw)
        {
            if (view == null) return;

            Vector3 position;
            Quaternion rotation;
            RestingPose(point, yaw, out position, out rotation);

            view.transform.SetPositionAndRotation(position, rotation);
            Register(view);
        }
    }
}
