using System.Collections.Generic;
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
        /// <b>A scene never starts with paper on the floor.</b>
        ///
        /// <para>The ledger is the only record that a sheet has been issued and it does not
        /// survive a scene load, so a sheet that did survive one would exist with nothing
        /// recording it and could be issued a second time — R2.10 says that must be
        /// impossible. This used to be enforced by hiding sheets from serialisation with
        /// HideFlags.DontSaveInEditor, which cost three separate bugs: their meshes were
        /// garbage-collected as unreferenced, FindObjectsByType stopped returning them, and
        /// they vanished from the Hierarchy window while still being visible in the scene.
        /// Sweeping them at startup states the same rule out loud, and leaves a sheet an
        /// ordinary GameObject that can be selected, inspected and deleted like anything
        /// else.</para>
        /// </summary>
        void Awake()
        {
            int stale = AllInScene().Length;
            if (stale == 0) return;

            ClearAll();
            Debug.Log($"[SheetSpawner] Cleared {stale} sheet(s) present at scene start. " +
                      "Issuance lives in the ledger, and the ledger starts empty.", this);
        }

        /// <summary>
        /// Every sheet actually in the scene, found rather than remembered.
        ///
        /// <para><c>spawned</c> is ordinary runtime state and does not survive a domain
        /// reload; the sheets themselves do, because they are GameObjects. Trusting the list
        /// after a recompile means the spawner has forgotten paper that is still lying on the
        /// floor — it clears nothing, counts nothing, and stacks new sheets into the same
        /// plane as the old ones. Anything that must be right about what exists asks the
        /// scene.</para>
        ///
        /// <para>Walks the active scene's roots rather than calling
        /// <c>FindObjectsByType</c>: it is scoped to one scene, it includes the sheet
        /// currently in the player's hands (a child of the camera), and it does not care what
        /// hideFlags anything carries — a lesson learned when <c>FindObjectsByType</c>
        /// reported, quite confidently, that there was no paper in a room full of it.</para>
        /// </summary>
        public static SheetView[] AllInScene()
        {
            var found = new List<SheetView>();

            // Resources.FindObjectsOfTypeAll, and nothing else will do. It is the only lookup
            // that returns objects carrying DontSave-family hideFlags — FindObjectsByType
            // skips them, Scene.GetRootGameObjects skips them, and the Hierarchy window does
            // not draw them. Sheets used to be spawned with HideFlags.DontSaveInEditor, and
            // any left over from that era are rendered, collidable, walkable, and reachable
            // by no ordinary API at all. This finds them so they can be destroyed.
            SheetView[] all = Resources.FindObjectsOfTypeAll<SheetView>();

            for (int i = 0; i < all.Length; i++)
            {
                SheetView view = all[i];
                if (view == null) continue;

                // Scene-bound only: the same call also returns prefab assets and anything
                // living in a preview scene, and neither is paper on this floor.
                if (!view.gameObject.scene.IsValid()) continue;

                // Board slabs are not paper on this floor (C5.4). A SheetView on the
                // cartography board is the same component doing a different job, and every
                // caller here would get it wrong: Awake() would destroy it, Place() would
                // count it into the floor pile height, ClearAll() would take the board with
                // the floor. See BoardSheet's class comment for why the test is a marker
                // component and not the Table layer.
                if (view.GetComponent<BoardSheet>() != null) continue;

                found.Add(view);
            }
            return found.ToArray();
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

            int layer = LayerMask.NameToLayer(sheetLayer);
            if (layer >= 0) SetLayerRecursive(view.gameObject, layer);

            // A sheet in the world can be taken. The spawner decides that, not the view:
            // the same view will later sit in a rack or on the table with a different verb.
            // Nothing is handed to it — it asks whoever aims at it.
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

            // Deterministic scatter: the same sheet always lands the same way, which makes a
            // reported layout reproducible.
            float jitter = (Mathf.Abs(render.Id.GetHashCode() % 1000) / 1000f - 0.5f) * 2f * rotationJitter;
            float facing = anchor != null ? anchor.eulerAngles.y : 0f;

            view.transform.SetPositionAndRotation(world, Quaternion.Euler(0f, facing + jitter, 0f));

            spawned.Add(view);
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

        /// <summary>
        /// Where a sheet released above <paramref name="point"/> comes to rest.
        ///
        /// <para>Decided before the sheet starts falling, not on arrival. A drifting sheet
        /// that worked out where it landed only once it got there could land inside another
        /// one, or fail to find the pile it was aiming at — and R5.6 leaves no room for
        /// either. The fall is presentation; this is the fact.</para>
        /// </summary>
        public void RestingPose(Vector3 point, float yaw, out Vector3 position, out Quaternion rotation)
        {
            float y = floorY + liftOff;

            // Transforms moved by script are not visible to a query until physics is told
            // about them: Physics.autoSyncTransforms is off by default, so a collider that was
            // positioned this frame is still queried where it used to be. In play mode the
            // next FixedUpdate hides that; in edit mode there is no next FixedUpdate, and the
            // probe silently finds nothing at all — which reads as "the floor is clear" and
            // puts one thing straight through another.
            Physics.SyncTransforms();

            int layer = LayerMask.NameToLayer(sheetLayer);
            if (layer >= 0)
            {
                // Cast from just above the floor, not from the release point: paper is
                // released at chest height and the ray would finish above the pile it is
                // looking for.
                var origin = new Vector3(point.x, floorY + 0.6f, point.z);

                RaycastHit hit;
                if (Physics.Raycast(origin, Vector3.down, out hit, 1.2f,
                                    1 << layer, QueryTriggerInteraction.Ignore))
                {
                    y = Mathf.Max(y, hit.point.y + separation);
                }
            }

            position = new Vector3(point.x, y, point.z);
            rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>Counts a sheet as part of the floor. Called once it has actually landed.</summary>
        public void Register(SheetView view)
        {
            if (view != null && !spawned.Contains(view)) spawned.Add(view);
        }

        /// <summary>
        /// Stops counting a sheet as part of the floor. The counterpart of
        /// <see cref="Register"/>, and it exists because <see cref="ClearAll"/> is no longer
        /// the only way paper leaves the world.
        ///
        /// <para>A sheet is about to gain a second exit: being filed into a binder, where the
        /// paper is destroyed and only its <c>SheetId</c> survives. <c>spawned</c> is a plain
        /// <c>List</c> that <c>Register</c> only ever appends to, so a sheet destroyed that way
        /// leaves a null hole behind in it — and <see cref="Spawned"/> is public and read by the
        /// editor bench, which would then be reading a list of the floor that is partly about
        /// sheets that are not on it. This keeps the floor's own record of the floor
        /// honest.</para>
        ///
        /// <para><b>It does not touch the ledger</b>, for the reason <see cref="ClearAll"/>
        /// gives for the same omission: what is lying on the floor and what has been issued are
        /// different facts. Filing a sheet away is not un-issuing it, any more than clearing the
        /// floor was — the ledger is the record that it exists at all (R2.10), and a sheet in a
        /// binder still does.</para>
        /// </summary>
        public void Forget(SheetView view)
        {
            if (view == null) return;
            spawned.Remove(view);
        }

        /// <summary>
        /// Removes every sheet this spawner has placed. Does not touch the ledger: what is
        /// on the floor and what has been issued are different facts, and clearing the floor
        /// is not un-issuing anything.
        /// </summary>
        public void ClearAll()
        {
            SheetView[] all = AllInScene();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;

                if (Application.isPlaying) Destroy(all[i].gameObject);
                else DestroyImmediate(all[i].gameObject);
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
