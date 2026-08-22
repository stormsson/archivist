using UnityEngine;
using Archivist.Building.Collection;
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
    /// </summary>
    public sealed class SheetView : MonoBehaviour
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

        Mesh mesh;
        Material materialInstance;
        Texture2D texture;

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
            view.materialInstance.SetTexture(mapTextureProperty, view.texture);

            root.AddComponent<MeshFilter>().sharedMesh = view.mesh;

            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = view.materialInstance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // The slab's underside is at local y = 0, so the collider is offset to match.
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(paperW, Thickness, paperH);
            box.center = new Vector3(0f, Thickness * 0.5f, 0f);

            return view;
        }

        void OnDestroy()
        {
            Destroy(mesh);
            Destroy(materialInstance);
            Destroy(texture);
        }
    }
}
