using UnityEngine;
using Archivist.Building.Handling;

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
    /// <para><b>A scene never starts with binders in it</b>, for the reason
    /// <see cref="FloorPile{T}"/> gives, and for one of its own: a binder's contents are not
    /// serialised at all, so what came back would be an empty binder claiming a number. Swept
    /// at startup, and stripped before a scene is written to disk by <c>SheetSceneGuard</c>.</para>
    /// </summary>
    public sealed class BinderSpawner : FloorPile<BinderView>
    {
        [Header("Wiring")]
        [Tooltip("The binder prefab. Needs a BinderView, a BinderPickup and a collider.")]
        [SerializeField] GameObject binderPrefab;

        [Header("Numbering")]
        [Tooltip("The n of the next Binder_n. Serialised, so it is visible and can be reset; " +
                 "incremented by one every time a binder is made.")]
        [SerializeField] int nextNumber = 1;

        [Header("Layout")]
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

        protected override string LayerName { get { return binderLayer; } }
        protected override float LiftOff { get { return liftOff; } }
        protected override float Separation { get { return separation; } }
        protected override float StackProbe { get { return stackProbe; } }
        protected override BinderView[] Present { get { return AllInScene(); } }

        /// <summary>The n the next binder will get. Read-only: the counter moves in
        /// <see cref="Create"/> and nowhere else.</summary>
        public int NextNumber { get { return nextNumber; } }

        /// <summary>Every binder actually in the scene. See <c>FloorPile.InScene</c>.</summary>
        public static BinderView[] AllInScene()
        {
            return InScene();
        }

        /// <summary>
        /// A new, empty binder for one island (Q3.1), numbered and named. What offices end up in
        /// it is decided by what is filed into it, and that is the caller's business — the ledger
        /// has to be told about every sheet that goes in (R2.10).
        /// </summary>
        public BinderView Create(ulong islandSeed, string islandName)
        {
            BinderView binder = NewBinder();
            if (binder == null) return null;

            binder.Bind(nextNumber, islandSeed, islandName);
            nextNumber++;

            return binder;
        }

        /// <summary>
        /// A binder read back out of the save: same number, same island, and no claim on the
        /// counter. <see cref="Create"/> mints the next number because a new binder is new; this
        /// one already has a name on its spine, and minting a second would put two
        /// <c>Binder_4</c>s in one room.
        ///
        /// <para>Nothing is filed into it and it is not placed — the caller does both, because
        /// only the caller knows what the file said. It is not registered on the floor either:
        /// a restored binder may be on a table or in the player's hands.</para>
        /// </summary>
        public BinderView Recreate(int number, ulong islandSeed, string islandName)
        {
            BinderView binder = NewBinder();
            if (binder == null) return null;

            binder.Bind(number, islandSeed, islandName);
            return binder;
        }

        /// <summary>One unbound binder off the prefab, on the pile's layer.</summary>
        BinderView NewBinder()
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

            ApplyLayer(instance);
            return binder;
        }

        /// <summary>
        /// Where the counter is after a load. Never rewinds it — a number that has been on a
        /// spine is spent, whatever the file says — and the caller has already restored every
        /// binder the file named, so this only has to cover the numbers that are gone: a binder
        /// filed away, or one from a save older than the sheet that named it.
        /// </summary>
        public void AdoptNextNumber(int next)
        {
            if (next > nextNumber) nextNumber = next;
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
            float jitter = Scatter(binder.Number * 37, rotationJitter);

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
    }
}
