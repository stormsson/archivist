using UnityEngine;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Marks a <c>SheetView</c> as living on the cartography board rather than in the room.
    /// Spec C5.4. No fields and no behaviour — the component's presence is the whole of it.
    ///
    /// <para><b>A board slab is not paper on this floor.</b> The board reuses <c>SheetView</c>
    /// deliberately: §3.2 wants the mesh built at true paper size so a 1:2500 A1 and a
    /// Hydrographic strip differ in board size by exactly as much as their ground footprints
    /// do. But <c>SheetSpawner</c> owns every <c>SheetView</c> in the scene and treats each one
    /// as a sheet lying on the room's floor (R4.7). Without this marker, three things go wrong
    /// — all three are in <c>SheetSpawner</c> and all three are silent:</para>
    ///
    /// <para>1. <c>SheetSpawner.Awake()</c> counts every scene-bound <c>SheetView</c> as stale
    /// and calls <c>ClearAll()</c> on it. That sweep exists for a real reason — the ledger is
    /// the only record that a sheet was issued and it does not survive a scene load, so
    /// surviving paper could be issued twice and R2.10 forbids that — but a board slab was
    /// never issued by the ledger's rules, and the sweep would destroy every board at scene
    /// start.</para>
    ///
    /// <para>2. <c>Place()</c> uses <c>AllInScene().Length</c> as the height of the floor pile,
    /// because the batch index restarts at zero every crate opening and using it made batches
    /// coplanar. Board slabs counted into that number push the next floor sheet
    /// <c>n * (Thickness + separation)</c> metres into the air — paper hovering above the
    /// floor, with nothing on screen to say why.</para>
    ///
    /// <para>3. <c>ClearAll()</c> destroys them, so anything that legitimately clears the
    /// floor — a debug menu item, a re-issue — takes the board with it.</para>
    ///
    /// <para><b>Why a component and not a layer test.</b> The board slabs are on the
    /// <c>Table</c> layer (C5.1) and testing that layer would compile and mostly work, which
    /// is the problem. A layer assignment is inspector state: it can be dropped by a prefab
    /// revert, missed on a runtime-created child, or renamed, and every one of those failures
    /// is silent — the board simply disappears at the next scene start and nothing in the
    /// console mentions layers. A marker component is code. It is added where the slab is
    /// built, it is visible in the Inspector as a named row, and forgetting it is a wiring
    /// mistake you can find by searching for the type. C5.4 settles this: "a layer test is not
    /// enough — the layer can be misconfigured in the inspector and the failure is
    /// silent."</para>
    ///
    /// <para><b>It will be found.</b> <c>SheetSpawner.AllInScene()</c> uses
    /// <c>Resources.FindObjectsOfTypeAll</c> — see its comment — precisely because that is the
    /// one lookup which still returns objects other APIs hide, including anything carrying
    /// DontSave-family hideFlags. So board slabs are not accidentally invisible to it and
    /// cannot be excluded by hiding them; the exclusion has to be stated, and this states
    /// it.</para>
    /// </summary>
    public sealed class BoardSheet : MonoBehaviour
    {
    }
}
