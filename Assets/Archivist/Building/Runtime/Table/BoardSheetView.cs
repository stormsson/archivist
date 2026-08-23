using System;
using UnityEngine;
using Archivist.Building.Collection;
using Archivist.Building.Sheets;
using Archivist.Generation.Sheets;
using Archivist.Render;

namespace Archivist.Building.Table
{
    /// <summary>
    /// One sheet on the cartography board: the <b>map alone</b>, at the exact size of the
    /// ground it covers. Not a sheet of paper — a window onto the island.
    ///
    /// <para><b>Why this is not a mode on <c>SheetView</c>.</b> The board tried that first,
    /// and the two objects turned out to disagree about what a sheet *is*. <c>SheetView</c>
    /// builds paper: a slab at <c>Format.WidthMm</c> with the map composited inside its margin,
    /// which is right for the room — the margin is in the texture precisely so a sheet is one
    /// surface and cannot z-fight itself (see <c>SheetTexture</c>). On a ground-space board
    /// that same object is simply <i>wrong</i>: <c>SurveySpec.SheetGroundWidth</c> is
    /// <c>Scale.GroundMetres(Format.MapWidthMm)</c> — the map area, not the paper — so
    /// <c>Sheet.FrameRect</c>, <c>Sheet.Contains</c> and <c>Sheet.GroundCorners</c> have always
    /// described the map and nothing else. Drawing whole paper against those numbers
    /// over-covers the ground by the margin on all four sides and misplaces every edge, which
    /// then makes the snap test of §6.1 argue with the geometry it is snapping to. Two
    /// meanings of "a sheet" cannot share a component without one of them being a special
    /// case in every method, so they do not share one. Removing the margin here is a
    /// correctness fix, not a look.</para>
    ///
    /// <para><b>A quad, not a slab — §3.2's thickness problem deleted rather than
    /// managed.</b> §3.2 sizes a paper-size mesh by <c>Scale.Denominator * UnitsPerMetre</c>
    /// and applies it as <c>(s, 1, s)</c>, non-uniform on purpose, because scaling Y too would
    /// make a 1:25000 whole-island sheet ten times thicker than a 1:2500 detail sheet. That
    /// awkwardness only existed because the mesh had a thickness the board never wanted: the
    /// board is looked at straight down by an orthographic camera (§3.1) and nothing on it can
    /// ever see an edge. With a flat quad there is no Y extent to scale wrongly, so the
    /// non-uniform scale and <c>BoardSpace.SlabScaleFor</c> both go away. <b>§3.2 is superseded
    /// on both counts</b> — paper size and non-uniform scale — while its actual finding, that
    /// sheets differ in board size by exactly as much as their ground footprints differ (D-C5),
    /// is what this component now expresses directly.</para>
    ///
    /// <para><b>Sized in board units, in the vertices.</b> Width and height are
    /// <c>Survey.SheetGroundWidth/Height * unitsPerMetre</c>, baked into the four corners, and
    /// <c>localScale</c> is left at one. There is no <c>Scale.Denominator</c> anywhere here —
    /// ground metres are already the answer, and going via paper metres and back is a unit
    /// conversion with two chances to be wrong. Anyone reading this transform in the Inspector
    /// or in a drag handler (C8.9) sees a pose and only a pose, with no hidden factor to
    /// remember.</para>
    ///
    /// <para><b>It carries <see cref="BoardSheet"/>, added here in <see cref="Create"/>.</b>
    /// C5.4 makes the marker non-optional and this component is where a board slab is built, so
    /// this is where it goes rather than in each caller. As it happens a <c>BoardSheetView</c>
    /// is not a <c>SheetView</c>, so <c>SheetSpawner.AllInScene</c> cannot see it and the three
    /// failures C5.4 lists — destroyed at scene start, counted into the floor pile, cleared
    /// with the floor — are structurally impossible now. The marker stays anyway: it is the
    /// spec's stated way to ask "is this a board slab", it survives someone later giving the
    /// spawner a wider net, and a marker that is present costs nothing.</para>
    ///
    /// <para>One mesh, one material, one texture, none of them assets — so this owns all three
    /// and destroys them, exactly as <c>SheetView</c> does.</para>
    /// </summary>
    public sealed class BoardSheetView : MonoBehaviour
    {
        /// <summary>
        /// Board units of collider depth, straddling the quad. The quad itself is flat, but
        /// C8.8 hit-tests with <c>Physics.Raycast</c> and a zero-thickness box is not a
        /// reliable raycast target. Half of <c>TableOptions.DefaultSheetSeparation</c>, so a
        /// slab's collider can never reach through the slab stacked above it in the draw order
        /// (§3.3) and steal its clicks.
        /// </summary>
        public const float ColliderThickness = TableOptions.DefaultSheetSeparation * 0.5f;

        public SheetId Id { get; private set; }

        /// <summary>The sheet this slab draws. Kept whole rather than as pieces because every
        /// question the board asks later — <c>CentreGround</c> and <c>RotationDeg</c> for the
        /// snap test (§6.1), <c>Survey.Office</c> and <c>Number</c> for the cabinet (§7.1) —
        /// is a question about the sheet, and the generator is the authority on all of
        /// them.</summary>
        public Sheet Sheet { get; private set; }

        /// <summary>The slab's collider — the pointer target of C8.8.</summary>
        public Collider Body { get; private set; }

        Mesh mesh;
        Material materialInstance;
        Texture2D texture;

        /// <summary>
        /// False when the texture was handed in rather than built here, in which case this slab
        /// must NOT destroy it.
        ///
        /// <para>The cabinet thumbnail and the board slab want the same pixels (C5.5), and
        /// <see cref="BoardView"/> caches one texture per <see cref="SheetId"/> to serve both.
        /// Without this flag the two options were a second upload per sheet — about 36 MB of
        /// duplicate VRAM across a 48-sheet board — or one texture with two owners and one
        /// <c>Destroy</c>, which is worse: the first <c>Remove</c> would blank every thumbnail
        /// still on screen. A borrowed texture is neither.</para>
        /// </summary>
        bool ownsTexture = true;

        /// <summary>
        /// Builds one board slab, sized in board units and textured with the map alone.
        /// <paramref name="unitsPerMetre"/> is <c>TableOptions.BoardUnitsPerMetre</c> (§3.1);
        /// it is passed in rather than read from the options asset so the sizing stays a pure
        /// function of its arguments and a bench can drive it without wiring.
        ///
        /// <para>The returned object is unparented and at the origin. The caller places it —
        /// <c>BoardSpace.ToBoard(sheet.CentreGround)</c> for X/Z, the draw-order stack for Y
        /// (§3.3), and the rotation <i>negated</i>, because ground Y maps to board Z and Unity's
        /// yaw turns the other way.</para>
        /// </summary>
        public static BoardSheetView Create(SheetRender render, Material unlitMaterial,
                                            string mapTextureProperty, double unitsPerMetre)
        {
            if (render == null) throw new ArgumentNullException("render");

            BoardSheetView view = Create(render.Sheet, render.Id, render.IslandName,
                                         Upload(render.Image, "T_Board_" + render.Id),
                                         unlitMaterial, mapTextureProperty, unitsPerMetre);
            view.ownsTexture = true;
            return view;
        }

        /// <summary>
        /// The same slab, textured with a map somebody else owns — <see cref="BoardView"/>'s
        /// per-<see cref="SheetId"/> cache, which the cabinet's thumbnails read from too (C5.5).
        ///
        /// <para><b>This slab will not destroy that texture.</b> One raster, one upload, two
        /// readers: the expensive half is the <c>IslandRenderer</c> pass, but a second
        /// <c>Texture2D</c> per sheet is still real memory for pixels that already exist.
        /// The caller keeps the texture alive for as long as any slab or thumbnail shows it.</para>
        /// </summary>
        public static BoardSheetView Create(Sheet sheet, SheetId id, string islandName,
                                            Texture2D map, Material unlitMaterial,
                                            string mapTextureProperty, double unitsPerMetre)
        {
            BoardSheetView borrowed = Build(sheet, id, islandName, map, unlitMaterial,
                                            mapTextureProperty, unitsPerMetre);
            borrowed.ownsTexture = false;
            return borrowed;
        }

        static BoardSheetView Build(Sheet sheet, SheetId id, string islandName, Texture2D map,
                                    Material unlitMaterial, string mapTextureProperty,
                                    double unitsPerMetre)
        {
            SurveySpec survey = sheet.Survey;

            // Ground metres straight to board units. SheetGroundWidth/Height are the MAP's
            // footprint (Scale.GroundMetres(Format.MapWidthMm)) — which is exactly what is
            // drawn here, margin and all excluded.
            float width  = (float)(survey.SheetGroundWidth  * unitsPerMetre);
            float height = (float)(survey.SheetGroundHeight * unitsPerMetre);

            var root = new GameObject(
                $"BoardSheet_{islandName}_{survey.Office}_{sheet.Number}");

            var view = root.AddComponent<BoardSheetView>();
            view.Id = id;
            view.Sheet = sheet;

            view.mesh = CreateQuad(width, height, "BoardSheetQuad");
            view.texture = map;

            view.materialInstance = new Material(unlitMaterial);
            view.materialInstance.name = "M_Board_" + id;
            view.materialInstance.hideFlags = HideFlags.DontSave;
            view.materialInstance.SetTexture(mapTextureProperty, view.texture);

            root.AddComponent<MeshFilter>().sharedMesh = view.mesh;

            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = view.materialInstance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // The quad straddles y = 0 rather than sitting on it, so the collider does too.
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(width, ColliderThickness, height);
            box.center = Vector3.zero;
            view.Body = box;

            // C5.4. See the class comment: structurally unnecessary now, kept because the
            // spec says a board slab is the thing that carries this.
            root.AddComponent<BoardSheet>();

            return view;
        }

        /// <summary>
        /// Four verts, two triangles, lying in the XZ plane, centred on the origin, normal +Y.
        /// Built by hand rather than with <c>GameObject.CreatePrimitive</c>: that drags in a
        /// <c>MeshCollider</c> to delete and hands back Unity's <i>shared</i> quad mesh, which
        /// this component must not own — and this component destroys what it owns.
        ///
        /// <para>UVs run U along +X and V along +Z with (0,0) at the −X/−Z corner, which is
        /// <c>SheetMesh</c>'s top face exactly. Paired with the upload flip below, that puts
        /// the frame's north at the slab's +Z edge, so a sheet's board rotation means the same
        /// thing here as it does on the floor.</para>
        /// </summary>
        static Mesh CreateQuad(float width, float height, string name)
        {
            float hw = width * 0.5f, hh = height * 0.5f;

            var verts = new[]
            {
                new Vector3(-hw, 0f, -hh),
                new Vector3( hw, 0f, -hh),
                new Vector3( hw, 0f,  hh),
                new Vector3(-hw, 0f,  hh)
            };

            var norms = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };

            var uvs = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f)
            };

            // (0,2,1) and (0,3,2): that pairing puts cross(v2-v0, v1-v0) along +Y, which is
            // the winding Unity treats as front-facing — the same convention SheetMesh.AddQuad
            // uses, and the reason the board camera looking down −Y sees paper and not
            // backfaces.
            var tris = new[] { 0, 2, 1, 0, 3, 2 };

            // DontSave for the reason SheetMesh.CreateSlab gives: a board slab is outside the
            // serialized object graph, so anything only it references looks unreferenced to
            // UnloadUnusedAssets and is collected on the next domain reload — leaving a slab
            // that is placed, enabled, and draws nothing. OnDestroy frees it, so nothing leaks.
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The map, and only the map, as a texture. No paper composite: on the board the map
        /// IS the sheet (see the class comment), so there is nothing to composite it onto.
        ///
        /// <para><b>The one vertical flip.</b> <see cref="ImageBuffer"/> is RGBA32, row-major,
        /// TOP-LEFT origin — what raster consumers and PNG expect — and <c>Texture2D</c> is
        /// BOTTOM-LEFT, so uploading the raw bytes shows the map upside down. On a roughly
        /// symmetric island that is genuinely easy to miss, which is why
        /// <c>SheetTexture.Compose</c> does it in exactly one place and says so. This is the
        /// board's copy of that flip, and it cannot be shared: <c>Archivist.Render</c> is
        /// engine-free by design and neither side may pull it toward UnityEngine. With no
        /// margin to write there is nothing to fold the flip into, so it is what it looks
        /// like — a plain row copy, source row y landing at destination row
        /// <c>Height - 1 - y</c>.</para>
        /// </summary>
        static Texture2D Upload(ImageBuffer map, string name)
        {
            int stride = map.Width * 4;
            var pixels = new byte[stride * map.Height];

            for (int y = 0; y < map.Height; y++)
                Buffer.BlockCopy(map.Pixels, y * stride,
                                 pixels, (map.Height - 1 - y) * stride, stride);

            var tex = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32,
                                    mipChain: true, linear: false);
            tex.name = name;
            tex.hideFlags = HideFlags.DontSave;      // see CreateQuad
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            // Deliberately NOT SheetTexture's 8. That value is justified there by "a sheet on
            // the floor is never seen face-on" — a board sheet is only ever seen face-on, by an
            // orthographic camera looking straight down (§3.1). Anisotropy would cost sampling
            // work on every board sheet to correct a foreshortening that cannot occur.
            tex.anisoLevel = 1;

            // SetPixelData, not LoadRawTextureData: with a mip chain the latter expects bytes
            // for every level, and only level 0 exists here.
            tex.SetPixelData(pixels, 0);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return tex;
        }

        void OnDestroy()
        {
            Discard(mesh);
            Discard(materialInstance);

            // Only if we made it. A borrowed texture belongs to BoardView's cache and is still
            // being shown by the cabinet after this slab goes back in the drawer.
            if (ownsTexture) Discard(texture);
        }

        /// <summary>Destroy is illegal in edit mode, and a board slab is routinely destroyed
        /// there — by the bench's Clear, by rebuilding the board, by deleting the root in the
        /// Hierarchy.</summary>
        static void Discard(UnityEngine.Object asset)
        {
            if (asset == null) return;

            if (Application.isPlaying) Destroy(asset);
            else DestroyImmediate(asset);
        }
    }
}
