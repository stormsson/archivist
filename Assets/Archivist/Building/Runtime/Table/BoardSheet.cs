using UnityEngine;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Marks a <c>SheetView</c> as living on the cartography board rather than on the room's
    /// floor (C5.4). No fields and no behaviour — the component's presence is the whole of it.
    ///
    /// <para>A slab is a <c>SheetView</c> so that it is built at true paper size (§3.2), but
    /// <c>SheetSpawner</c> owns every <c>SheetView</c> in the scene and treats each as issued
    /// paper on the floor (R4.7): this is what excludes them, and the three silent ways that
    /// goes wrong are listed in <c>SheetSpawner</c>, where they would happen.</para>
    ///
    /// <para><b>A marker component, not a layer test</b> (C5.4). The <c>Table</c> layer would
    /// mostly work, but a layer is inspector state that a prefab revert or a runtime-created
    /// child can drop, and the board would then vanish at the next scene start with nothing in
    /// the console about layers.</para>
    /// </summary>
    public sealed class BoardSheet : MonoBehaviour
    {
    }
}
