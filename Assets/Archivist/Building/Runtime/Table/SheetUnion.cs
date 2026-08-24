using System.Collections.Generic;
using UnityEngine;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The board-space bounding box of an assembly's quads (G5.4), plus the lowest tier in the
    /// run so an outline can sit under all of it.
    ///
    /// <para><b>One implementation of "where is this assembly".</b> The selection outline, the
    /// corner handle and the turn pivot are all measured from this box, and their agreeing is
    /// the whole point — a second way to compute it is how they stop agreeing.</para>
    ///
    /// <para><b>Corners, not <c>Renderer.bounds</c>.</b> A renderer's bounds are axis-aligned in
    /// <i>world</i>, and the rig hangs 500 units under the room on a root that may move, so
    /// reading them would make the box depend on where the board was built. The four corners of
    /// a <c>SheetGroundWidth × UnitsPerMetre</c> quad are exact.</para>
    ///
    /// <para><b>Axis-aligned in board space, and it stays that way.</b> A sheet has one rotation
    /// but an assembly does not — the Hydrographic coast walk gives every strip its own angle
    /// (D-H2) — so there is no angle to turn the box by.</para>
    ///
    /// <para>Taken from the members' transforms rather than from the group's frame: they are
    /// already derived from it, and this box only has to agree with what is on screen.</para>
    /// </summary>
    public readonly struct SheetUnion
    {
        public readonly float MinX, MinZ, MaxX, MaxZ, LowestY;

        public SheetUnion(float minX, float minZ, float maxX, float maxZ, float lowestY)
        {
            MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ; LowestY = lowestY;
        }

        public float Width { get { return MaxX - MinX; } }
        public float Height { get { return MaxZ - MinZ; } }
        public float CentreX { get { return (MinX + MaxX) * 0.5f; } }
        public float CentreZ { get { return (MinZ + MaxZ) * 0.5f; } }

        /// <summary>
        /// Every member's four corners, turned by its own rotation, reduced to a box in the
        /// board root's local space.
        ///
        /// <para>False on an empty list rather than a box at the origin, so a caller with no
        /// slabs to measure falls back to something it chooses — a pivot that is merely
        /// arbitrary, rather than a turn that happens 500 units away.</para>
        /// </summary>
        public static bool TryOf(IReadOnlyList<BoardSheetView> members, float unitsPerMetre,
                                 out SheetUnion union)
        {
            union = default(SheetUnion);
            if (members == null || members.Count == 0) return false;

            float minX = float.MaxValue, minZ = float.MaxValue, lowestY = float.MaxValue;
            float maxX = float.MinValue, maxZ = float.MinValue;

            for (int i = 0; i < members.Count; i++)
            {
                BoardSheetView slab = members[i];
                Transform t = slab.transform;

                float hw = (float)(slab.Sheet.Survey.SheetGroundWidth * unitsPerMetre * 0.5);
                float hh = (float)(slab.Sheet.Survey.SheetGroundHeight * unitsPerMetre * 0.5);

                Vector3 centre = t.localPosition;
                if (centre.y < lowestY) lowestY = centre.y;

                for (int c = 0; c < 4; c++)
                {
                    float sx = (c == 0 || c == 3) ? -hw : hw;
                    float sz = (c == 0 || c == 1) ? -hh : hh;

                    Vector3 corner = centre + t.localRotation * new Vector3(sx, 0f, sz);

                    if (corner.x < minX) minX = corner.x;
                    if (corner.x > maxX) maxX = corner.x;
                    if (corner.z < minZ) minZ = corner.z;
                    if (corner.z > maxZ) maxZ = corner.z;
                }
            }

            union = new SheetUnion(minX, minZ, maxX, maxZ, lowestY);
            return true;
        }
    }
}
