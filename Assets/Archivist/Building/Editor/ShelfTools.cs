using UnityEditor;
using UnityEngine;
using Archivist.Building.Shelving;

namespace Archivist.Building.Editor
{
    /// <summary>
    /// The whole scene's shelves, rebuilt in one go.
    ///
    /// <para>The per-shelf button lives on <see cref="Shelf"/> itself, where the numbers being
    /// changed are. This is for the other case: a change that is true of every shelf in the room,
    /// where visiting each one in the Hierarchy is how one gets missed. It is
    /// <c>RoomBuilder</c>'s argument at a smaller scale — provisional numbers have to be cheap to
    /// rebuild.</para>
    ///
    /// <para>Each shelf still asks before it wipes, so this is a run of confirmations rather than
    /// one blanket yes. That is deliberate: a menu item that silently destroyed every slot in the
    /// room would be one keystroke from losing an afternoon.</para>
    /// </summary>
    public static class ShelfTools
    {
        [MenuItem("Archivist/Rebuild Shelf Slots")]
        public static void RebuildAll()
        {
            Shelf[] shelves = Object.FindObjectsByType<Shelf>(FindObjectsSortMode.None);
            if (shelves.Length == 0)
            {
                Debug.Log("[ShelfTools] No Shelf in the open scene — nothing to rebuild.");
                return;
            }

            for (int i = 0; i < shelves.Length; i++) shelves[i].RebuildSlots();

            Debug.Log($"[ShelfTools] Rebuilt {shelves.Length} shelf/shelves.");
        }
    }
}
