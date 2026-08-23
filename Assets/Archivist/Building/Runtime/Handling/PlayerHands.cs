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
    /// <para>The held pose lives on <c>holdAnchor</c>, a transform under the eye, not in
    /// numbers here. Moving that transform in the scene view <i>is</i> the tuning loop —
    /// which matters, because where a sheet sits while carried is a feel question and feel
    /// questions are not settled by editing constants.</para>
    /// </summary>
    public sealed class PlayerHands : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Transform under the eye that defines the carried pose. Move it to tune.")]
        [SerializeField] Transform holdAnchor;
        [SerializeField] SheetSpawner spawner;

        [Header("Options")]
        [Tooltip("Feel values for carrying and placing. See HandlingOptions.")]
        [SerializeField] HandlingOptions options;

        [Header("Input")]
        [SerializeField] InputActionAsset inputActions;

        [Header("Diagnostics")]
        [Tooltip("Logs where a sheet came from and where it was sent. A take that fails and " +
                 "a take that succeeds off-screen look identical from behind the camera.")]
        [SerializeField] bool logHandling;

        InputAction dropAction;
        InputAction turnAction;

        SheetView held;

        /// <summary>
        /// How far the player has turned the carried sheet, in degrees. Kept so the turn
        /// survives being put down: choosing an orientation is only meaningful if the sheet
        /// stays where it was turned to.
        /// </summary>
        float heldTurn;

        // The travel from floor to hands. Held in local space of the anchor, so the sheet
        // keeps following the camera while it flies rather than aiming at where the camera
        // used to be.
        bool arriving;
        float arrivalElapsed;
        Vector3 arrivalFrom;
        Quaternion arrivalFromRotation;

        public bool IsEmpty { get { return held == null; } }
        public SheetView Held { get { return held; } }

        void Awake()
        {
            var map = inputActions.FindActionMap("Player", throwIfNotFound: true);
            dropAction = map.FindAction("Drop", throwIfNotFound: true);
            turnAction = map.FindAction("Turn", throwIfNotFound: true);
        }

        void OnEnable()
        {
            // FirstPersonController owns the map's lifetime; enabling single actions is
            // idempotent and keeps this component independent of the order they wake in.
            // Living in the map is the point: whatever disables Player input — a menu, the
            // map table taking over — must stop dropping and turning too, and actions bound
            // in code would have carried on regardless.
            dropAction.Enable();
            turnAction.Enable();
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
            else Turn();
        }

        /// <summary>
        /// Moves the sheet from where it lay into the carried pose. Interpolated in the
        /// anchor's LOCAL space, so a player who keeps turning while a sheet is on its way
        /// sees it follow them rather than fly at a spot they have left.
        /// </summary>
        void Advance()
        {
            float duration = TakeSeconds;
            arrivalElapsed += Time.deltaTime;

            float k = duration <= 0f ? 1f : Mathf.Clamp01(arrivalElapsed / duration);
            float eased = k * k * (3f - 2f * k);   // smoothstep: no sudden start, no sudden stop

            Transform t = held.transform;
            t.localPosition = Vector3.Lerp(arrivalFrom, Vector3.zero, eased);
            t.localRotation = Quaternion.Slerp(arrivalFromRotation, Quaternion.identity, eased);

            if (k < 1f) return;

            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            arriving = false;
        }

        /// <summary>
        /// Q and E spin the carried sheet in its own plane. Held-not-pressed, because turning
        /// a sheet to read it is a continuous adjustment rather than a series of steps, and a
        /// step size is one more number to be wrong about.
        ///
        /// <para>The rotation is about the sheet's local Y, which is its face normal and is
        /// pointing at the camera while carried — so it spins the paper rather than tipping
        /// it. Positive is clockwise from the player's side, which is why E is the composite's
        /// positive end.</para>
        ///
        /// <para>One axis rather than two buttons: Q and E are the ends of a single continuous
        /// adjustment, so the asset expresses them as a 1D composite and this reads one
        /// float. It also means a future stick or shoulder pair binds here with no code
        /// change.</para>
        /// </summary>
        void Turn()
        {
            float direction = turnAction.ReadValue<float>();
            if (Mathf.Approximately(direction, 0f)) return;

            float step = direction * TurnDegreesPerSecond * Time.deltaTime;
            heldTurn += step;
            held.transform.Rotate(0f, step, 0f);
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

        float TurnDegreesPerSecond
        {
            get
            {
                return options != null
                    ? options.SheetTurnDegreesPerSecond
                    : HandlingOptions.DefaultSheetTurnDegreesPerSecond;
            }
        }

        /// <summary>
        /// Takes a sheet into the hold pose. The sheet's collider goes off while carried:
        /// otherwise it sits directly in front of the eye and swallows every interaction ray
        /// the player casts, making everything else in the room unreachable.
        /// </summary>
        public bool Take(SheetView sheet)
        {
            if (sheet == null || held != null || holdAnchor == null) return false;

            held = sheet;
            heldTurn = 0f;
            if (sheet.Body != null) sheet.Body.enabled = false;

            Transform t = sheet.transform;
            Vector3 cameFrom = t.position;

            // worldPositionStays: the sheet is adopted by the anchor without moving, and then
            // travels to the pose under its own steam. Snapping it to the pose in the same
            // frame it is picked up reads as the sheet vanishing from the floor and a
            // different one appearing in front of the face.
            t.SetParent(holdAnchor, worldPositionStays: true);

            arrivalFrom = t.localPosition;
            arrivalFromRotation = t.localRotation;
            arrivalElapsed = 0f;
            arriving = true;

            // Update does not tick in edit mode, so an animated take there would strand the
            // sheet where it lay. The Sheet Test Bench exists to look at the carried pose;
            // it should get the pose, not the journey.
            if (!Application.isPlaying)
            {
                t.localPosition = Vector3.zero;
                t.localRotation = Quaternion.identity;
                arriving = false;
            }

            if (logHandling)
                Debug.Log($"[Hands] took {sheet.Id} from {cameFrom} -> anchor {holdAnchor.position}", this);

            return true;
        }

        /// <summary>
        /// Lets the carried sheet go. It falls from the hands rather than appearing on the
        /// floor: dropping paper is a thing that takes a moment, and R4.7 makes the floor a
        /// legitimate destination rather than a failure.
        /// </summary>
        public bool Drop()
        {
            if (held == null) return false;

            SheetView sheet = held;
            held = null;
            arriving = false;

            sheet.transform.SetParent(null, worldPositionStays: true);

            // The player's turn is added to the way they are facing. There is no true mapping
            // between "up in the view" and a compass bearing, so some choice has to be made;
            // this one means a sheet lands looking the way it looked in hand.
            float yaw = transform.eulerAngles.y + heldTurn;
            heldTurn = 0f;

            // It falls from where it was held, so it lands roughly where the player was
            // holding it — which is what letting go of something means.
            Vector3 releasedAt = sheet.transform.position;

            if (spawner == null)
            {
                Land(sheet);
                return true;
            }

            Vector3 rest;
            Quaternion restRotation;
            spawner.RestingPose(releasedAt, yaw, out rest, out restRotation);

            // Update does not tick in edit mode, so a sheet dropped there would hang in the
            // air. The bench gets the outcome, not the fall.
            if (!Application.isPlaying)
            {
                sheet.transform.SetPositionAndRotation(rest, restRotation);
                Land(sheet);
                return true;
            }

            // The collider stays off until it lands: a falling sheet is not something to aim
            // at, and RestingPose looks downward for paper already lying there — a sheet must
            // not find itself.
            sheet.gameObject.AddComponent<SheetFall>()
                 .Begin(sheet, rest, restRotation, options, transform.right, Land);

            return true;
        }

        void Land(SheetView sheet)
        {
            if (sheet == null) return;

            if (sheet.Body != null) sheet.Body.enabled = true;
            if (spawner != null) spawner.Register(sheet);

            if (logHandling)
                Debug.Log($"[Hands] {sheet.Id} settled at {sheet.transform.position}", this);
        }
    }
}
