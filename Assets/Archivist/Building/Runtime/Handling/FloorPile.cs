using System;
using System.Collections.Generic;
using UnityEngine;

namespace Archivist.Building.Handling
{
    /// <summary>
    /// The floor as a place things are kept (R4.7): what is lying on it, where a released
    /// thing comes to rest, and the rule that a scene never starts with any of it down.
    ///
    /// <para>Making, numbering and delivering belong to the subclass — a sheet is a slab the
    /// runtime builds, a binder is an imported model with contents. What they share is one
    /// floor, so the pile, the downward probe and the startup sweep are here and exist once.
    /// </para>
    ///
    /// <para><b>Thicknesses are not shared.</b> <see cref="LiftOff"/>,
    /// <see cref="Separation"/> and <see cref="StackProbe"/> are the subclass's own, because
    /// paper and a ring binder are not the same object standing on the same floor.</para>
    /// </summary>
    public abstract class FloorPile<T> : MonoBehaviour where T : Component
    {
        [Header("Floor")]
        [SerializeField] protected float floorY;

        /// <summary>Where the downward probe starts, above <c>floorY</c>: clear of any pile,
        /// and low enough that the ray does not finish above what it is looking for.</summary>
        const float ProbeHeight = 0.6f;

        readonly List<T> spawned = new List<T>();

        public IReadOnlyList<T> Spawned { get { return spawned; } }

        /// <summary>Clearance between the floor and the first item's underside, in metres.</summary>
        protected abstract float LiftOff { get; }

        /// <summary>Air between whatever is already lying there and what goes on top of it,
        /// in metres.</summary>
        protected abstract float Separation { get; }

        /// <summary>How far down the search for what is already lying there reaches, in
        /// metres.</summary>
        protected virtual float StackProbe { get { return 1.2f; } }

        /// <summary>The layer items are put on, and the only layer the probe sees.</summary>
        protected abstract string LayerName { get; }

        /// <summary>
        /// The scene lookup, forwarded. Each subclass publishes it as a static — an editor
        /// sweep runs with no instance in the scene to ask — and a static cannot be
        /// overridden.
        /// </summary>
        protected abstract T[] Present { get; }

        /// <summary>
        /// <b>A scene never starts with a pile on the floor.</b>
        ///
        /// <para>The ledger is the only record that a sheet has been issued and it does not
        /// survive a scene load, so anything that did survive one would exist with nothing
        /// recording it and could be issued a second time — R2.10 says that must be
        /// impossible.</para>
        /// </summary>
        protected virtual void Awake()
        {
            int stale = Present.Length;
            if (stale == 0) return;

            ClearAll();
            Debug.Log($"[{GetType().Name}] Cleared {stale} {typeof(T).Name}(s) present at scene " +
                      "start. Issuance lives in the ledger, and the ledger starts empty.", this);
        }

        /// <summary>
        /// Everything of this kind actually in the scene, found rather than remembered.
        ///
        /// <para><c>spawned</c> is ordinary runtime state and does not survive a domain
        /// reload; the objects themselves do, because they are GameObjects. Trusting the list
        /// after a recompile means the pile has forgotten things still lying on the floor —
        /// it clears nothing, counts nothing, and stacks new items into the same plane as the
        /// old ones. Anything that must be right about what exists asks the scene.</para>
        ///
        /// <para><c>Resources.FindObjectsOfTypeAll</c>, and nothing else will do: it is the
        /// only lookup that returns objects carrying DontSave-family hideFlags, which
        /// <c>FindObjectsByType</c> skips, <c>Scene.GetRootGameObjects</c> skips, and the
        /// Hierarchy window does not draw — an object like that is rendered, collidable,
        /// walkable, and reachable by no other API at all. It also returns what is in the
        /// player's hands, a child of the camera.</para>
        ///
        /// <para><paramref name="accepts"/> narrows it to items that are genuinely on this
        /// floor, for the kinds whose component does a second job elsewhere.</para>
        /// </summary>
        protected static T[] InScene(Predicate<T> accepts)
        {
            var found = new List<T>();

            T[] all = Resources.FindObjectsOfTypeAll<T>();
            for (int i = 0; i < all.Length; i++)
            {
                T item = all[i];
                if (Gone(item)) continue;

                // Scene-bound only: the same call also returns prefab assets and anything
                // living in a preview scene, and neither is on this floor.
                if (!item.gameObject.scene.IsValid()) continue;

                if (accepts != null && !accepts(item)) continue;

                found.Add(item);
            }
            return found.ToArray();
        }

        /// <summary>Everything of this kind in the scene, unfiltered.</summary>
        protected static T[] InScene()
        {
            return InScene(null);
        }

        /// <summary>
        /// Where something released above <paramref name="point"/> comes to rest.
        ///
        /// <para>Decided before it starts falling, not on arrival. A drifting sheet that
        /// worked out where it landed only once it got there could land inside another one,
        /// or fail to find the pile it was aiming at — and R5.6 leaves no room for either.
        /// The fall is presentation; this is the fact.</para>
        /// </summary>
        public void RestingPose(Vector3 point, float yaw, out Vector3 position, out Quaternion rotation)
        {
            float y = floorY + LiftOff;

            // Transforms moved by script are not visible to a query until physics is told
            // about them: Physics.autoSyncTransforms is off by default, so a collider that was
            // positioned this frame is still queried where it used to be. In play mode the
            // next FixedUpdate hides that; in edit mode there is no next FixedUpdate, and the
            // probe silently finds nothing at all — which reads as "the floor is clear" and
            // puts one thing straight through another.
            Physics.SyncTransforms();

            int layer = LayerMask.NameToLayer(LayerName);
            if (layer >= 0)
            {
                // Cast from just above the floor, not from the release point: things are let
                // go at chest height and the ray would finish above the pile it is aiming at.
                var origin = new Vector3(point.x, floorY + ProbeHeight, point.z);

                RaycastHit hit;
                if (Physics.Raycast(origin, Vector3.down, out hit, StackProbe,
                                    1 << layer, QueryTriggerInteraction.Ignore))
                {
                    y = Mathf.Max(y, hit.point.y + Separation);
                }
            }

            position = new Vector3(point.x, y, point.z);
            rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>Counts an item as part of the floor. Called once it has actually
        /// landed.</summary>
        public void Register(T item)
        {
            if (!Gone(item) && !spawned.Contains(item)) spawned.Add(item);
        }

        /// <summary>
        /// Stops counting an item as part of the floor. The counterpart of
        /// <see cref="Register"/>, for the ways something leaves the world one at a time —
        /// filing a sheet into a binder destroys the paper and keeps only its <c>SheetId</c>.
        /// Without this <see cref="Spawned"/>, which is public and read by the editor bench,
        /// keeps a null hole where the item was.
        ///
        /// <para><b>It does not touch the ledger</b>, for the reason <see cref="ClearAll"/>
        /// gives for the same omission: what is lying on the floor and what has been issued are
        /// different facts. Filing a sheet away is not un-issuing it — the ledger is the record
        /// that it exists at all (R2.10), and a sheet in a binder still does.</para>
        /// </summary>
        public void Forget(T item)
        {
            if (Gone(item)) return;
            spawned.Remove(item);
        }

        /// <summary>
        /// Removes every item of this kind from the scene. Does not touch the ledger: what is
        /// on the floor and what has been issued are different facts, and clearing the floor
        /// is not un-issuing anything.
        /// </summary>
        public void ClearAll()
        {
            T[] all = Present;
            for (int i = 0; i < all.Length; i++)
            {
                if (Gone(all[i])) continue;

                if (Application.isPlaying) Destroy(all[i].gameObject);
                else DestroyImmediate(all[i].gameObject);
            }
            spawned.Clear();
        }

        /// <summary>Puts an item and everything under it on the pile's layer. The pile decides
        /// that, not the view: the same object will later sit in a rack or on the table.</summary>
        protected void ApplyLayer(GameObject go)
        {
            int layer = LayerMask.NameToLayer(LayerName);
            if (layer >= 0) SetLayerRecursive(go, layer);
        }

        /// <summary>
        /// Degrees of yaw for one item, so a pile reads as dropped rather than laid out.
        /// Deterministic in <paramref name="key"/>: the same sheet or binder always lands the
        /// same way round, which makes a reported layout reproducible.
        /// </summary>
        protected static float Scatter(int key, float degrees)
        {
            return (Mathf.Abs(key % 1000) / 1000f - 0.5f) * 2f * degrees;
        }

        /// <summary>
        /// Unity's null, not C#'s. <c>T</c> is a type parameter, so <c>item == null</c> on it
        /// compiles to a reference comparison and misses the overload that knows a destroyed
        /// object from a live one.
        /// </summary>
        static bool Gone(T item)
        {
            return (Component)item == null;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }
    }
}
