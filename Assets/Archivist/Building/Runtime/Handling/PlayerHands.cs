using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Archivist.Building.Sheets;

namespace Archivist.Building.Handling
{
    /// <summary>
    /// What the player is carrying. One sheet for now; §3.5 will grow this into stacks
    /// (R5.1), weight (R5.2) and the settle (R5.3), which is why it is its own component
    /// rather than a field on the controller.
    ///
    /// <para><b>It holds an <see cref="ICarryable"/>, not a sheet.</b> A binder is not paper —
    /// different mesh, different contents, different verb on the floor — and the hands are the
    /// wrong place to learn that difference. Holding a <c>SheetSpawner</c> and asking it where a
    /// released sheet lands becomes a type switch inside a component that models a pair of hands.
    /// Instead the item is asked where it comes to rest and told when it has arrived.</para>
    ///
    /// <para>The held pose lives on <c>holdAnchor</c>, a transform under the eye, not in
    /// numbers here. Moving that transform in the scene view <i>is</i> the tuning loop —
    /// which matters, because where a sheet sits while carried is a feel question and feel
    /// questions are not settled by editing constants.</para>
    ///
    /// <para><b>There are two ways an item leaves the hands, and they are two verbs rather
    /// than one verb with a flag.</b> <see cref="Drop"/> is letting go: nothing has been
    /// decided for the item, so it decides for itself through <see cref="ICarryable.RestingPose"/>
    /// and falls to whatever it chose. <see cref="HandOver"/> is giving the item to something
    /// that has already made that decision — a table anchor, later a shelf slot — and it
    /// glides to the pose it was told. Folding these into one method taking a nullable pose
    /// would hide the difference that matters: whether the destination is the item's business
    /// or somebody else's, which also decides whether it is registered as part of the floor.
    /// The hands still do not know what a table is. They are handed a pose and they move the
    /// item to it.</para>
    ///
    /// <para><b>Q/E turning belongs to the cartography table, not here</b> (D-C10). Turning
    /// paper to read it belongs where paper is read: the orientation chosen in hand is discarded
    /// the moment the sheet is laid on a board, which has a true orientation of its own, so the
    /// verb in both places would mean two turn states that can disagree and one of them with no
    /// consequence.</para>
    /// </summary>
    public sealed class PlayerHands : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Transform under the eye that defines the carried pose. Move it to tune.")]
        [SerializeField] Transform holdAnchor;

        [Header("Options")]
        [Tooltip("Feel values for carrying and placing. See HandlingOptions.")]
        [SerializeField] HandlingOptions options;

        [Header("Input")]
        [SerializeField] InputActionAsset inputActions;

        [Header("Diagnostics")]
        [Tooltip("Logs where an item came from and where it was sent. A take that fails and " +
                 "a take that succeeds off-screen look identical from behind the camera.")]
        [SerializeField] bool logHandling;

        InputAction dropAction;

        ICarryable held;

        // The travel from floor to hands. Held in local space of the anchor, so the item
        // keeps following the camera while it flies rather than aiming at where the camera
        // used to be.
        bool arriving;
        float arrivalElapsed;
        Vector3 arrivalFrom;
        Quaternion arrivalFromRotation;

        public bool IsEmpty { get { return held == null; } }
        public ICarryable Held { get { return held; } }

        /// <summary>What is being carried, if it is paper. Null when the hands are empty and
        /// null when they hold something that is not a sheet — a binder holds sheets but is
        /// not one, and a caller that wants either should ask <see cref="Held"/>.</summary>
        public SheetView HeldSheet { get { return held as SheetView; } }

        void Awake()
        {
            var map = inputActions.FindActionMap("Player", throwIfNotFound: true);
            dropAction = map.FindAction("Drop", throwIfNotFound: true);
        }

        void OnEnable()
        {
            // FirstPersonController owns the map's lifetime; enabling single actions is
            // idempotent and keeps this component independent of the order they wake in.
            // Living in the map is the point: whatever disables Player input — a menu, the
            // map table taking over — must stop dropping too, and actions bound in code would
            // have carried on regardless.
            dropAction.Enable();
        }

        void Update()
        {
            if (held == null) return;

            if (dropAction.WasPressedThisFrame())
            {
                Drop();
                return;
            }

            if (arriving) Advance();
        }

        /// <summary>
        /// Moves the item from where it lay into the carried pose. Interpolated in the
        /// anchor's LOCAL space, so a player who keeps turning while something is on its way
        /// sees it follow them rather than fly at a spot they have left.
        /// </summary>
        void Advance()
        {
            float duration = TakeSeconds;
            arrivalElapsed += Time.deltaTime;

            float k = duration <= 0f ? 1f : Mathf.Clamp01(arrivalElapsed / duration);
            float eased = k * k * (3f - 2f * k);   // smoothstep: no sudden start, no sudden stop

            // The item says which way round it is held; the anchor says where. Turning to
            // the item's own pose rather than to identity is what lets a binder be carried
            // like a folder while a sheet stays face-on, without a second anchor.
            Quaternion carried = held.CarriedRotation;

            Transform t = held.Root;
            t.localPosition = Vector3.Lerp(arrivalFrom, Vector3.zero, eased);
            t.localRotation = Quaternion.Slerp(arrivalFromRotation, carried, eased);

            if (k < 1f) return;

            t.localPosition = Vector3.zero;
            t.localRotation = carried;
            arriving = false;
        }

        float TakeSeconds
        {
            get
            {
                return options != null
                    ? options.SheetTakeSeconds
                    : HandlingOptions.DefaultSheetTakeSeconds;
            }
        }

        float PlaceSeconds
        {
            get
            {
                return options != null
                    ? options.BinderPlaceSeconds
                    : HandlingOptions.DefaultBinderPlaceSeconds;
            }
        }

        /// <summary>
        /// Takes an item into the hold pose. Its collider goes off while carried: otherwise it
        /// sits directly in front of the eye and swallows every interaction ray the player
        /// casts, making everything else in the room unreachable.
        /// </summary>
        public bool Take(ICarryable item)
        {
            if (item == null || item.Root == null || held != null || holdAnchor == null) return false;

            held = item;
            if (item.Body != null) item.Body.enabled = false;

            Transform t = item.Root;
            Vector3 cameFrom = t.position;

            // worldPositionStays: the item is adopted by the anchor without moving, and then
            // travels to the pose under its own steam. Snapping it to the pose in the same
            // frame it is picked up reads as the item vanishing from the floor and a
            // different one appearing in front of the face.
            t.SetParent(holdAnchor, worldPositionStays: true);

            arrivalFrom = t.localPosition;
            arrivalFromRotation = t.localRotation;
            arrivalElapsed = 0f;
            arriving = true;

            // Update does not tick in edit mode, so an animated take there would strand the
            // item where it lay. The Sheet Test Bench exists to look at the carried pose;
            // it should get the pose, not the journey.
            if (!Application.isPlaying)
            {
                t.localPosition = Vector3.zero;
                t.localRotation = item.CarriedRotation;
                arriving = false;
            }

            if (logHandling)
                Debug.Log($"[Hands] took {item.CarryName} from {cameFrom} -> anchor {holdAnchor.position}", this);

            return true;
        }

        /// <summary>
        /// Lets the carried item go. It falls from the hands rather than appearing on the
        /// floor: dropping something is a thing that takes a moment, and R4.7 makes the floor
        /// a legitimate destination rather than a failure.
        /// </summary>
        public bool Drop()
        {
            if (held == null) return false;

            ICarryable item = held;
            held = null;
            arriving = false;

            Transform t = item.Root;
            if (t == null) return true;

            t.SetParent(null, worldPositionStays: true);

            // The item takes the way the player is facing. There is no true mapping between
            // "up in the view" and a compass bearing, so some choice has to be made; this one
            // means it lands aligned with the way the player was facing when they let go.
            float yaw = transform.eulerAngles.y;

            // It falls from where it was held, so it lands roughly where the player was
            // holding it — which is what letting go of something means.
            Vector3 releasedAt = t.position;

            // Asked of the item, and asked BEFORE it moves: the resting place is decided at
            // release (R5.6). A sheet answers through the floor pile it belongs to, a binder
            // through its own; the hands do not need to know which.
            Vector3 rest;
            Quaternion restRotation;
            item.RestingPose(releasedAt, yaw, out rest, out restRotation);

            // Update does not tick in edit mode, so an item dropped there would hang in the
            // air. The bench gets the outcome, not the fall.
            if (!Application.isPlaying)
            {
                t.SetPositionAndRotation(rest, restRotation);
                Land(item);
                return true;
            }

            // The collider stays off until it lands: a falling item is not something to aim
            // at, and a resting pose found by looking downward for what is already lying
            // there must not find the thing that is still on its way.
            t.gameObject.AddComponent<ItemFall>()
                        .Begin(item, rest, restRotation, options, transform.right, Land);

            return true;
        }

        /// <summary>
        /// Gives the carried item to something that has already decided where it goes, and
        /// lets it glide there. The pose is the caller's: a table anchor, a shelf slot,
        /// anything that owns a place rather than merely being underneath one.
        ///
        /// <para>It glides rather than falls, through <see cref="ItemPlace"/> — see that class
        /// for why a fall is the wrong path for a destination in front of the player rather
        /// than below them.</para>
        ///
        /// <para><b>It deliberately does not call <c>ICarryable.Settled</c>, and must not be
        /// "tidied up" by routing it through <c>Land</c>.</b> Settling is how an item tells
        /// whatever tracks the floor that it is part of it again, and an item set down on a table
        /// is not on the floor: registering it there puts it into the pile the drop probe stacks
        /// against, so the next thing dropped comes to rest on top of a binder lying on a table,
        /// in mid-air. The collider still comes back on when it arrives, because the item does
        /// need to be aimed at again.</para>
        /// </summary>
        public bool HandOver(Vector3 restPosition, Quaternion restRotation, Action<ICarryable> onLanded)
        {
            if (held == null) return false;

            ICarryable item = held;
            held = null;
            arriving = false;

            Transform t = item.Root;
            if (t == null) return true;

            t.SetParent(null, worldPositionStays: true);

            // Update does not tick in edit mode, so an item handed over there would hang in
            // the air halfway to the table. The bench gets the outcome, not the journey.
            if (!Application.isPlaying)
            {
                t.SetPositionAndRotation(restPosition, restRotation);
                if (item.Body != null) item.Body.enabled = true;
                if (onLanded != null) onLanded(item);
                return true;
            }

            // The collider stays off for the whole journey and comes back on when it arrives,
            // for the same reason a falling item's does: something still travelling is not
            // something to aim at, and a pose found by probing for what is already there must
            // not find the thing that is still on its way.
            t.gameObject.AddComponent<ItemPlace>()
                        .Begin(item, restPosition, restRotation, PlaceSeconds, landedItem =>
                        {
                            if (landedItem.Body != null) landedItem.Body.enabled = true;

                            if (logHandling)
                                Debug.Log($"[Hands] handed {landedItem.CarryName} over at {landedItem.Root.position}", this);

                            if (onLanded != null) onLanded(landedItem);
                        });

            return true;
        }

        void Land(ICarryable item)
        {
            if (item == null || item.Root == null) return;

            if (item.Body != null) item.Body.enabled = true;
            item.Settled();

            if (logHandling)
                Debug.Log($"[Hands] {item.CarryName} settled at {item.Root.position}", this);
        }
    }
}
