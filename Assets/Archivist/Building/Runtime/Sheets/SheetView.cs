using UnityEngine;
using Archivist.Building.Collection;
using Archivist.Building.Handling;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Sheets
{
    /// <summary>
    /// One sheet as a physical object: paper at its true size, with the map printed inside
    /// its margin. What you see is the ground that sheet covers — same rect, same rotation,
    /// same scale the generator cut it at.
    ///
    /// <para>One mesh, one material, one texture. Nothing here is an asset, so this owns all
    /// three and destroys them.</para>
    ///
    /// <para><b>Carryable, and that is the only thing it knows about the player.</b> The hands
    /// ask a sheet where it comes to rest rather than working it out themselves, so a sheet
    /// answers by delegating to the <see cref="SheetSpawner"/> that owns the floor pile — the
    /// pile is what decides, and it is not the sheet's business to know how. The spawner is
    /// found, never stored across a load: a reference handed in at spawn time does not survive
    /// a domain reload and comes back null with no symptom but a sheet that lands in the wrong
    /// plane. <c>SheetPickup</c> has the same lesson written on it.</para>
    /// </summary>
    public sealed class SheetView : MonoBehaviour, ICarryable
    {
        /// <summary>
        /// Metres. Real paper is ~0.1 mm, which is below what depth precision can separate at
        /// grazing angles and below what a collider should be. 1.5 mm is a deliberate
        /// exaggeration: thick enough that stacked sheets are genuinely apart in space rather
        /// than relying on an offset to look apart.
        /// </summary>
        public const float Thickness = 0.0015f;

        public SheetId Id { get; private set; }
        public string IslandName { get; private set; }
        public Office Office { get; private set; }
        public int Number { get; private set; }

        /// <summary>The sheet's collider. Switched off while carried, so a sheet held in
        /// front of the eye does not swallow every interaction ray the player casts.</summary>
        public Collider Body { get; private set; }

        Mesh mesh;
        Material materialInstance;
        Texture2D texture;

        SheetSpawner floor;

        // ---- ICarryable ------------------------------------------------------------------

        public Transform Root { get { return transform; } }

        public string CarryName { get { return Id.ToString(); } }

        public int CarrySeed { get { return Id.GetHashCode(); } }

        /// <summary>No turn. A sheet is carried face-on to be read, which is exactly how the
        /// hold anchor is aimed; anything else would be a pose fighting the anchor's.</summary>
        public Quaternion CarriedRotation { get { return Quaternion.identity; } }

        /// <summary>Delegated to the floor pile, which is what actually decides how high a
        /// dropped sheet sits. Without one — a sheet in a bench scene — it lands where it was
        /// released, which is wrong by less than it is unhelpful.</summary>
        public void RestingPose(Vector3 releasedAt, float yaw,
                                out Vector3 position, out Quaternion rotation)
        {
            SheetSpawner pile = Pile;
            if (pile != null)
            {
                pile.RestingPose(releasedAt, yaw, out position, out rotation);
                return;
            }

            position = releasedAt;
            rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public void Settled()
        {
            SheetSpawner pile = Pile;
            if (pile != null) pile.Register(this);
        }

        /// <summary>Found rather than remembered — see the class comment.</summary>
        SheetSpawner Pile
        {
            get
            {
                if (floor == null) floor = FindAnyObjectByType<SheetSpawner>();
                return floor;
            }
        }

        public static SheetView Create(SheetRender render, Material sheetMaterial,
                                       Color paperTint, string mapTextureProperty)
        {
            SheetFormat format = render.Sheet.Survey.Format;

            float paperW = (float)(format.WidthMm / 1000.0);
            float paperH = (float)(format.HeightMm / 1000.0);

            var root = new GameObject($"Sheet_{render.IslandName}_{render.Sheet.Survey.Office}_{render.Sheet.Number}");
            var view = root.AddComponent<SheetView>();

            view.Id = render.Id;
            view.IslandName = render.IslandName;
            view.Office = render.Sheet.Survey.Office;
            view.Number = render.Sheet.Number;

            view.mesh = SheetMesh.CreateSlab(paperW, paperH, Thickness, "SheetSlab");
            view.texture = SheetTexture.Compose(render.Image, format, paperTint, "T_Sheet_" + render.Id);

            view.materialInstance = new Material(sheetMaterial);
            view.materialInstance.name = "M_Sheet_" + render.Id;
            view.materialInstance.hideFlags = HideFlags.DontSave;
            view.materialInstance.SetTexture(mapTextureProperty, view.texture);

            root.AddComponent<MeshFilter>().sharedMesh = view.mesh;

            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = view.materialInstance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // The slab's underside is at local y = 0, so the collider is offset to match.
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(paperW, Thickness, paperH);
            box.center = new Vector3(0f, Thickness * 0.5f, 0f);
            view.Body = box;

            return view;
        }

        void OnDestroy()
        {
            // Destroy is illegal in edit mode, and a sheet is routinely destroyed there — by
            // the test bench, by the purge, by deleting it in the Hierarchy.
            Discard(mesh);
            Discard(materialInstance);
            Discard(texture);
        }

        static void Discard(Object asset)
        {
            if (asset == null) return;

            if (Application.isPlaying) Destroy(asset);
            else DestroyImmediate(asset);
        }
    }
}
