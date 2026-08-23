using System.Collections.Generic;
using UnityEngine;

namespace Archivist.Building.Binders
{
    /// <summary>
    /// Makes binders, numbers them, and puts them on the floor. The counterpart of
    /// <c>SheetSpawner</c>, and deliberately a second component rather than a mode of it: a
    /// binder is an imported model with contents, a sheet is a slab the runtime builds, and
    /// the only thing they share is a floor to land on.
    ///
    /// <para><b>It owns the numbering.</b> <c>Binder_1</c>, <c>Binder_2</c>, … — one counter,
    /// incremented once per binder made, and nowhere else. A binder cannot number itself
    /// without knowing about every other binder, and a crate cannot without becoming the
    /// second place that mints identity the moment a second crate exists. Same shape as
    /// <c>IslandGenerator.nextIslandIndex</c>, and for the same reason.</para>
    ///
    /// <para><b>A scene never starts with binders in it</b>, exactly as it never starts with
    /// paper on the floor. A binder holds <c>SheetId</c>s of sheets the ledger has recorded as
    /// issued, and the ledger does not survive a scene load — so a binder that did would hold
    /// sheets nothing remembers issuing, and a crate could issue them a second time (R2.10).
    /// Worse, its contents are not serialised at all, so what came back would be an empty
    /// binder claiming a number. Swept at startup, and stripped before a scene is written to
    /// disk by <c>SheetSceneGuard</c>.</para>
    /// </summary>
    public sealed class BinderSpawner : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The binder prefab. Needs a BinderView, a BinderPickup and a collider.")]
        [SerializeField] GameObject binderPrefab;

        [Header("Numbering")]
        [Tooltip("The n of the next Binder_n. Serialised, so it is visible and can be reset; " +
                 "incremented by one every time a binder is made.")]
        [SerializeField] int nextNumber = 1;

        [Header("Layout")]
        [SerializeField] float floorY;
        [Tooltip("Where a freshly delivered binder lands, in the drop anchor's local space. " +
                 "Far enough to one side to clear the loose debug sheet, which is drawn at " +
                 "true paper size and can be A1.")]
        [SerializeField] Vector3 dropOffset = new Vector3(-0.95f, 0f, 0.8f);
        [Tooltip("Degrees of scatter, so a delivered binder reads as put down rather than " +
                 "placed. Deterministic per binder number.")]
        [SerializeField] float rotationJitter = 10f;
        [SerializeField] string binderLayer = "Item";

        [Header("Stacking")]
        [Tooltip("Clearance between the floor and the binder's underside, in metres.")]
        [SerializeField] float liftOff = 0.001f;
        [Tooltip("Air between whatever is already lying there and the binder put on top of " +
                 "it, in metres.")]
        [SerializeField] float separation = 0.002f;
        [Tooltip("How far down the search for what is already lying there reaches, in metres.")]
        [SerializeField] float stackProbe = 1.2f;

        readonly List<BinderView> spawned = new List<BinderView>();

        public IReadOnlyList<BinderView> Spawned { get { return spawned; } }

        /// <summary>The n the next binder will get. Read-only: the counter moves in
        /// <see cref="Create"/> and nowhere else.</summary>
        public int NextNumber { get { return nextNumber; } }

        /// <summary>See the class comment: issuance lives in the ledger, and the ledger starts
        /// empty.</summary>
        void Awake()
        {
            int stale = AllInScene().Length;
            if (stale == 0) return;

            ClearAll();
            Debug.Log($"[BinderSpawner] Cleared {stale} binder(s) present at scene start. " +
                      "What they held was issued, and issuance starts empty.", this);
        }

        /// <summary>
        /// Every binder actually in the scene, found rather than remembered.
        ///
        /// <para><c>spawned</c> is ordinary runtime state and does not survive a domain
        /// reload; the binders themselves do, because they are GameObjects. Anything that must
        /// be right about what exists asks the scene — the lesson <c>SheetSpawner</c> learned
        /// the hard way, applied here before it can be learned twice.</para>
        /// </summary>
        public static BinderView[] AllInScene()
        {
            var found = new List<BinderView>();

            BinderView[] all = Resources.FindObjectsOfTypeAll<BinderView>();
            for (int i = 0; i < all.Length; i++)
            {
                BinderView binder = all[i];
                if (binder == null) continue;

                // Scene-bound only: the same call returns the prefab asset itself and anything
                // living in a preview scene, and neither is a binder on this floor.
                if (!binder.gameObject.scene.IsValid()) continue;

                found.Add(binder);
            }
            return found.ToArray();
        }

        /// <summary>
        /// A new, empty binder for one island, numbered and named. Nothing is filed into it
        /// here: what goes in is the caller's business, and the ledger has to be told about
        /// every sheet that does (R2.10).
        /// </summary>
        public BinderView Create(ulong islandSeed, string islandName)
        {
            if (binderPrefab == null)
            {
                Debug.LogError("[BinderSpawner] No binder prefab wired.", this);
                return null;
            }

            GameObject instance = Instantiate(binderPrefab);

            BinderView binder = instance.GetComponent<BinderView>();
            if (binder == null)
            {
                Debug.LogError($"[BinderSpawner] {binderPrefab.name} has no BinderView.", this);

                if (Application.isPlaying) Destroy(instance);
                else DestroyImmediate(instance);
                return null;
            }

            binder.Bind(nextNumber, islandSeed, islandName);
            nextNumber++;

            int layer = LayerMask.NameToLayer(binderLayer);
            if (layer >= 0) SetLayerRecursive(instance, layer);

            return binder;
        }

        /// <summary>
        /// Puts a binder down beside <paramref name="anchor"/> — where a crate's delivery
        /// lands. The offset keeps it clear of the sheet pile rather than in it.
        /// </summary>
        public BinderView Place(BinderView binder, Transform anchor)
        {
            if (binder == null) return null;

            Vector3 world = anchor != null
                ? anchor.position + anchor.rotation * dropOffset
                : dropOffset;

            float facing = anchor != null ? anchor.eulerAngles.y : 0f;

            // Deterministic scatter: Binder_3 always lands the same way round, which makes a
            // reported layout reproducible.
            float jitter = (Mathf.Abs(binder.Number * 37 % 1000) / 1000f - 0.5f) * 2f * rotationJitter;

            // Its own collider goes off for the probe: RestingPose looks downward for what is
            // already lying there, and a freshly instantiated binder sitting at the origin —
            // or at the spot it is about to be moved to — must not find itself and stack on
            // top of it.
            Collider body = binder.Body;
            bool wasEnabled = body != null && body.enabled;
            if (body != null) body.enabled = false;

            Vector3 rest;
            Quaternion rotation;
            RestingPose(world, facing + jitter, out rest, out rotation);

            if (body != null) body.enabled = wasEnabled;

            binder.transform.SetPositionAndRotation(rest, rotation);
            Register(binder);
            return binder;
        }

        /// <summary>
        /// Where a binder released above <paramref name="point"/> comes to rest.
        ///
        /// <para>Decided before it starts falling, not on arrival — R5.6, and the same rule
        /// <c>SheetSpawner</c> works to. The downward probe means a binder put down on a pile
        /// of paper sits on the paper rather than through it.</para>
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

            int layer = LayerMask.NameToLayer(binderLayer);
            if (layer >= 0)
            {
                // Cast from just above the floor, not from the release point: a binder is let
                // go at chest height and the ray would finish above whatever it is aiming at.
                var origin = new Vector3(point.x, floorY + 0.6f, point.z);

                RaycastHit hit;
                if (Physics.Raycast(origin, Vector3.down, out hit, stackProbe,
                                    1 << layer, QueryTriggerInteraction.Ignore))
                {
                    y = Mathf.Max(y, hit.point.y + separation);
                }
            }

            position = new Vector3(point.x, y, point.z);
            rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>Counts a binder as part of the floor. Called once it has actually
        /// landed.</summary>
        public void Register(BinderView binder)
        {
            if (binder != null && !spawned.Contains(binder)) spawned.Add(binder);
        }

        /// <summary>
        /// Removes every binder in the scene. Does not touch the ledger: what is on the floor
        /// and what has been issued are different facts, and clearing the floor un-issues
        /// nothing.
        /// </summary>
        public void ClearAll()
        {
            BinderView[] all = AllInScene();
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
