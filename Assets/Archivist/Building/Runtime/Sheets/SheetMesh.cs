using System.Collections.Generic;
using UnityEngine;

namespace Archivist.Building.Sheets
{
    /// <summary>
    /// A sheet of paper as a solid slab: a box with its underside at local y = 0, so
    /// positioning one means "put its underside here" rather than "put its middle here".
    ///
    /// <para><b>Why a solid, and why not two planes.</b> A paper quad with a map quad 0.2 mm
    /// above it is the textbook z-fighting setup, and it flickers at grazing angles — which is
    /// the angle a sheet on the floor is always seen at. The map is composited into the paper
    /// texture instead, so a sheet is <i>one</i> surface and cannot fight itself at any
    /// separation.</para>
    ///
    /// <para>Thickness then does the rest: sheets stack with real clearance instead of relying
    /// on sub-millimetre offsets to stay apart.</para>
    ///
    /// <para>The top face carries UV 0..1 with U along +X and V along +Z, which — after the
    /// upload flip — puts the frame's north at the sheet's far edge. Every other face samples
    /// a single point well inside the paper margin, so the edges read as paper without needing
    /// a second material or submesh.</para>
    /// </summary>
    public static class SheetMesh
    {
        /// <summary>A point inside the margin of every format the generator cuts. The
        /// narrowest margin in use is the detail sheet's 15 mm on 250 mm = 0.06 of the
        /// paper, so 0.012 is inside all of them with room to spare.</summary>
        static readonly Vector2 EdgeUv = new Vector2(0.012f, 0.012f);

        public static Mesh CreateSlab(float width, float depth, float thickness, string name)
        {
            float hw = width * 0.5f, hd = depth * 0.5f, t = thickness;

            var verts = new List<Vector3>(24);
            var norms = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var tris = new List<int>(36);

            // Top — the only face anyone reads.
            AddQuad(verts, norms, uvs, tris,
                    new Vector3(-hw, t, -hd), new Vector3(hw, t, -hd),
                    new Vector3(hw, t, hd), new Vector3(-hw, t, hd),
                    Vector3.up,
                    new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(1f, 1f), new Vector2(0f, 1f));

            // Bottom.
            AddQuad(verts, norms, uvs, tris,
                    new Vector3(-hw, 0f, -hd), new Vector3(-hw, 0f, hd),
                    new Vector3(hw, 0f, hd), new Vector3(hw, 0f, -hd),
                    Vector3.down, EdgeUv, EdgeUv, EdgeUv, EdgeUv);

            // Four edges, walked anticlockwise around the outline. Listing each as
            // (bottom_i, bottom_i+1, top_i+1, top_i) makes the winding come out outward-
            // facing for every one of them, so none needs to be reasoned about separately.
            var outline = new[]
            {
                new Vector2(-hw, -hd), new Vector2(hw, -hd),
                new Vector2(hw, hd), new Vector2(-hw, hd)
            };

            for (int i = 0; i < 4; i++)
            {
                Vector2 a = outline[i];
                Vector2 b = outline[(i + 1) % 4];
                Vector3 outward = new Vector3(a.x + b.x, 0f, a.y + b.y).normalized;

                AddQuad(verts, norms, uvs, tris,
                        new Vector3(a.x, 0f, a.y), new Vector3(b.x, 0f, b.y),
                        new Vector3(b.x, t, b.y), new Vector3(a.x, t, a.y),
                        outward, EdgeUv, EdgeUv, EdgeUv, EdgeUv);
            }

            // DontSave, and it is load-bearing. This mesh has no owner in any serialized
            // object graph — only the renderer SheetView puts it on — so without the flag
            // UnloadUnusedAssets collects it on the next play-mode transition or domain reload,
            // leaving a sheet that is positioned, parented, enabled and visible and draws
            // nothing at all. SheetView destroys it in OnDestroy, so nothing leaks.
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Corners listed around the perimeter; triangles (0,2,1) and (0,3,2). That pairing
        /// puts <c>cross(q2-q0, q1-q0)</c> along the outward normal, which is the direction
        /// Unity treats as front-facing.
        /// </summary>
        static void AddQuad(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                            Vector3 q0, Vector3 q1, Vector3 q2, Vector3 q3, Vector3 normal,
                            Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
        {
            int b = verts.Count;

            verts.Add(q0); verts.Add(q1); verts.Add(q2); verts.Add(q3);
            norms.Add(normal); norms.Add(normal); norms.Add(normal); norms.Add(normal);
            uvs.Add(uv0); uvs.Add(uv1); uvs.Add(uv2); uvs.Add(uv3);

            tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b + 0); tris.Add(b + 3); tris.Add(b + 2);
        }
    }
}
