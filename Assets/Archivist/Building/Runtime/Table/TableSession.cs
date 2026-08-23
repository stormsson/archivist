using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using Archivist.Building.Collection;
using Archivist.Building.Handling;
using Archivist.Building.Interaction;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The mode switch (§8.2). One object owns the whole of "the room hands over to the
    /// table, and takes itself back": which island is open, what stops listening while it is,
    /// and what starts.
    ///
    /// <para><b>It disables components, not action maps</b> (C8.5). Disabling the Player map
    /// looks equivalent and is not: <see cref="PlayerInteractor.OnEnable"/> and
    /// <see cref="PlayerHands.OnEnable"/> each call <c>Enable()</c> on their single action,
    /// deliberately, so that neither depends on the order the player's components wake in.
    /// That independence is exactly what makes a map-level switch unsafe here — anything that
    /// re-enables one of those components while the table is open (a script, a prefab
    /// revert, the inspector) silently re-arms <c>Interact</c> or <c>Drop</c> underneath a
    /// full-screen view, and the symptom is paper being dropped in a room the player cannot
    /// see. Components off is the only form of "off" that survives being woken.</para>
    ///
    /// <para><b>It never touches the cursor</b> (C8.6). <see cref="FirstPersonController"/>
    /// locks the cursor in <c>OnEnable</c> and releases it in <c>OnDisable</c>, so disabling
    /// that component releases the cursor for free and re-enabling it re-captures it. A
    /// <c>Cursor.lockState</c> line here would be a second owner of one piece of global
    /// state, and two owners of a global disagree the first time an ordering changes —
    /// typically as a table that closes onto a room you can look around but not click in.
    /// There is deliberately no <c>Cursor</c> reference anywhere in this file.</para>
    ///
    /// <para><b>And it does not clear the reticle</b> (C8.7). <c>PlayerInteractor.OnDisable</c>
    /// already nulls <c>current</c> and hides the prompt; a second hide here would be a second
    /// thing to keep in step with the prompt's API for no gain.</para>
    ///
    /// <para><b>Prior enabled state is captured, not assumed.</b> Close re-enables the three
    /// components only if they were enabled when Open was called. A3 asks that closing restore
    /// walk, look and interact <i>exactly as they were</i>, and "as they were" is not
    /// necessarily "all three on" — the bench and the editor tools switch these off
    /// individually, and a close that turned them all back on would quietly repair a state
    /// somebody set on purpose.</para>
    ///
    /// <para><b>The EventSystem is created here if the scene has none, and the scene has
    /// none</b> — the project contains no <c>EventSystem</c> at all (§5.1 lists it as "does
    /// not exist yet"), so without this the canvas would be visible and completely dead to the
    /// pointer. Creating it lazily rather than requiring scene wiring keeps this slice
    /// demonstrable without editing the scene, and the one we create is ours to switch off
    /// again on close (C8.4). An EventSystem we <i>found</i> is left alone in both directions:
    /// it belongs to whoever put it there, and disabling another system's input module on the
    /// way out of a table is not this class's business.</para>
    ///
    /// <para>Input teardown is symmetric with setup on purpose. A <c>performed</c> handler
    /// left subscribed after a domain reload fires into a dead object and throws, once per
    /// key press, from a stack trace that names the input system rather than this file —
    /// which is why <see cref="OnDisable"/> unsubscribes everything <see cref="OnEnable"/>
    /// subscribed, and why the actions are looked up in <c>Awake</c> where a missing one
    /// throws immediately and names itself.</para>
    /// </summary>
    public sealed class TableSession : MonoBehaviour
    {
        [Header("Table")]
        [Tooltip("The board camera and its slabs. Owns everything below the chrome.")]
        [SerializeField] BoardView board;

        [Tooltip("Header, cabinet, handle, footer. Screen-space overlay, off until opened.")]
        [SerializeField] TableCanvas tableCanvas;

        [Header("Room")]
        [Tooltip("Where the island to open comes from (C8.2). The table in the room has no " +
                 "island of its own and deliberately never did — see CartographyTable.")]
        [SerializeField] IslandGenerator generator;

        [Tooltip("The three components switched off while the table is open (C8.4).")]
        [SerializeField] FirstPersonController controller;
        [SerializeField] PlayerInteractor interactor;
        [SerializeField] PlayerHands hands;

        [Header("Input")]
        [SerializeField] InputActionAsset inputActions;

        [Tooltip("C8.2: opens the table on the last island drawn with the OpenTable action " +
                 "(C). A debug affordance, not the design — the diegetic way in is aiming " +
                 "at the table and pressing F.")]
        [SerializeField] bool debugShortcut = true;

        InputActionMap playerMap;
        InputActionMap tableMap;
        InputActionMap uiMap;
        InputAction openTableAction;
        InputAction cancelAction;

        EventSystem events;
        bool ownsEvents;

        bool open;
        bool controllerWasEnabled;
        bool interactorWasEnabled;
        bool handsWasEnabled;

        static TableSession active;
        static TableSession scene;

        /// <summary>The session currently showing a board, or null. Nothing else in the
        /// project may assume there is exactly one open table; this is the one place that
        /// knows whether the room has handed over.</summary>
        public static TableSession Active { get { return active; } }

        /// <summary>The scene's session, open or not — how a thing in the room reaches the
        /// table before there is anything to reach. <see cref="Active"/> cannot serve: it is
        /// null precisely when someone wants to open the table.</summary>
        public static TableSession InScene
        {
            get
            {
                if (scene == null)
                    scene = FindFirstObjectByType<TableSession>(FindObjectsInactive.Include);
                return scene;
            }
        }

        public bool IsOpen { get { return open; } }

        /// <summary>The island the open board shows. Meaningless while closed.</summary>
        public ulong IslandSeed { get; private set; }

        void Awake()
        {
            scene = this;

            // Wiring is resolved rather than required. Every reference here is a scene
            // singleton and every one of them is findable; a session dropped into the scene
            // with nothing dragged onto it therefore works, and a session that HAS been wired
            // keeps exactly what it was given. The alternative — null-check and LogError —
            // makes the component useless until somebody does the drag, which for a slice
            // that cannot edit the scene means useless full stop.
            if (generator == null) generator = FindFirstObjectByType<IslandGenerator>();
            if (controller == null) controller = FindFirstObjectByType<FirstPersonController>();
            if (interactor == null) interactor = FindFirstObjectByType<PlayerInteractor>();
            if (hands == null) hands = FindFirstObjectByType<PlayerHands>();

            // Inactive included: the board root and the canvas are off until opened (§5.1),
            // so the active-only search would find neither.
            if (board == null) board = FindFirstObjectByType<BoardView>(FindObjectsInactive.Include);
            if (tableCanvas == null)
                tableCanvas = FindFirstObjectByType<TableCanvas>(FindObjectsInactive.Include);

            if (inputActions == null) inputActions = FindLoadedActions();
            if (inputActions == null)
            {
                Debug.LogError("[TableSession] No InputActionAsset. C and Esc will do nothing.", this);
                return;
            }

            // throwIfNotFound on all four: every one of these exists in the asset today, and
            // a typo that silently produced a null action would show up as a key that does
            // nothing, which is the same symptom as half a dozen unrelated faults.
            playerMap = inputActions.FindActionMap("Player", throwIfNotFound: true);
            tableMap = inputActions.FindActionMap("Table", throwIfNotFound: true);
            uiMap = inputActions.FindActionMap("UI", throwIfNotFound: true);

            openTableAction = playerMap.FindAction("OpenTable", throwIfNotFound: true);
            cancelAction = uiMap.FindAction("Cancel", throwIfNotFound: true);
        }

        /// <summary>
        /// Last resort for an unwired session: the project's one action asset, already loaded
        /// because <see cref="FirstPersonController"/> serialises a reference to it. Matched
        /// by the maps it must contain rather than by name, so it cannot pick up the input
        /// module's built-in default actions — which have a UI map and nothing else.
        /// </summary>
        static InputActionAsset FindLoadedActions()
        {
            InputActionAsset[] all = Resources.FindObjectsOfTypeAll<InputActionAsset>();
            for (int i = 0; i < all.Length; i++)
            {
                InputActionAsset a = all[i];
                if (a == null) continue;
                if (a.FindActionMap("Player") == null) continue;
                if (a.FindActionMap("Table") == null) continue;
                if (a.FindActionMap("UI") == null) continue;
                return a;
            }
            return null;
        }

        void OnEnable()
        {
            if (openTableAction != null)
            {
                // Enabled individually, the way PlayerInteractor and PlayerHands enable
                // theirs: idempotent, and it keeps this component independent of the order
                // the player's components wake in. The map still owns the lifetime, which is
                // what makes the shortcut go quiet while the table is open — disabling
                // FirstPersonController disables the Player map, and OpenTable with it.
                openTableAction.Enable();
                openTableAction.performed += OnOpenShortcut;
            }

            // Not enabled here. The UI map is off while the player is walking (A3a), so this
            // handler is subscribed but silent until Open turns the map on.
            if (cancelAction != null) cancelAction.performed += OnCancel;
        }

        void OnDisable()
        {
            if (openTableAction != null) openTableAction.performed -= OnOpenShortcut;
            if (cancelAction != null) cancelAction.performed -= OnCancel;

            // A session switched off mid-view would otherwise strand the player: three
            // disabled components, no cursor lock, and nothing left alive to put them back.
            if (open) Close();

            if (active == this) active = null;
            if (scene == this) scene = null;
        }

        void OnOpenShortcut(InputAction.CallbackContext _)
        {
            // Read at the moment of the press, not cached in OnEnable, so the flag can be
            // turned off in the inspector during play — which is when anyone would want to.
            if (!debugShortcut || open) return;
            OpenCurrentIsland();
        }

        void OnCancel(InputAction.CallbackContext _)
        {
            if (open) Close();   // C8.3
        }

        /// <summary>
        /// Opens on the island the room last drew (C8.2). The single path both ways in — the
        /// debug shortcut and <c>CartographyTable</c> — take, so "which island" is decided
        /// once. The table in the room deliberately holds no island binding of its own; the
        /// seed comes from the generator, which is the only thing that knows what the archive
        /// has actually met.
        /// </summary>
        public void OpenCurrentIsland()
        {
            if (generator == null)
            {
                Debug.LogError("[TableSession] No IslandGenerator in the scene.", this);
                return;
            }

            ulong seed = generator.LastIslandSeed;
            if (seed == 0)
            {
                // Deliberately a refusal rather than a reservation. MapCrate reserves a new
                // island when it finds no last one, because drawing an island is what a crate
                // is for; a table showing sheets that were never issued would be showing an
                // island the archive has not received. Nothing to lay out is a true answer.
                Debug.Log("[TableSession] No island has been drawn yet — open a crate first.", this);
                return;
            }

            Open(seed);
        }

        /// <summary>
        /// Hands the room over to the board (§8.2). Safe to call on an already-open session:
        /// a different seed re-shows the board and the cabinet without repeating the mode
        /// switch, and the same seed does nothing at all.
        /// </summary>
        public void Open(ulong islandSeed)
        {
            if (islandSeed == 0)
            {
                Debug.LogWarning("[TableSession] Refusing to open on seed 0 — no island.", this);
                return;
            }

            if (open)
            {
                if (islandSeed == IslandSeed) return;
                IslandSeed = islandSeed;
                ShowContents();
                return;
            }

            if (active != null && active != this)
            {
                Debug.LogWarning("[TableSession] Another table is already open.", this);
                return;
            }

            IslandSeed = islandSeed;
            open = true;
            active = this;

            // The room goes quiet BEFORE the table wakes up. The other order leaves one frame
            // in which the pointer is over a live board and Interact is still armed, and a
            // click in that frame does both things.
            SuspendRoom();
            EnsureEventSystem();

            // Both maps together (C8.14). Table carries the game verbs the board owns —
            // Q/E turning, which left PlayerHands in D-C10 — and UI drives the input module.
            if (tableMap != null) tableMap.Enable();
            if (uiMap != null) uiMap.Enable();

            ShowContents();
        }

        /// <summary>
        /// Board and chrome, for whatever island is current. Separated from
        /// <see cref="Open"/> because re-showing on a new seed must not touch the mode
        /// switch: suspending an already-suspended room would record "was disabled" as the
        /// state to restore, and the player would come back unable to move.
        /// </summary>
        void ShowContents()
        {
            if (board != null)
            {
                // Fire and forget. The coroutine is BoardView's to own and to stop — a
                // StopCoroutine issued from here would be the wrong MonoBehaviour — and
                // C5.7 makes opening explicitly non-blocking: the mounting sheet appears
                // first and the sheets land as their textures do.
                // BoardView enables its own camera as it builds it. Reaching for
                // `board.BoardCamera` here read null every time: Show is a coroutine, so the
                // camera does not exist on the line after the call.
                board.Show(IslandSeed);
            }

            if (tableCanvas != null)
            {
                // Active before Show, so Show can start coroutines and touch children; the
                // canvas is off in the scene until opened (§5.1). Idempotent if TableCanvas
                // also manages its own object.
                tableCanvas.gameObject.SetActive(true);
                tableCanvas.Show(IslandSeed, board);
            }
        }

        /// <summary>Gives the room back (§8.2, reversed). Idempotent.</summary>
        public void Close()
        {
            if (!open) return;

            open = false;
            if (active == this) active = null;

            if (tableCanvas != null)
            {
                tableCanvas.Hide();
                tableCanvas.gameObject.SetActive(false);
            }

            if (board != null)
            {
                board.Hide();
                Camera cam = board.BoardCamera;
                if (cam != null) cam.enabled = false;
            }

            // Input module first, then the maps it reads: disabling the EventSystem's object
            // takes InputSystemUIInputModule down with it, and the module disables the UI
            // actions it holds on the way out. Disabling the maps first would leave the
            // module briefly enabled over dead actions.
            if (ownsEvents && events != null) events.gameObject.SetActive(false);

            if (uiMap != null) uiMap.Disable();
            if (tableMap != null) tableMap.Disable();

            ResumeRoom();
        }

        /// <summary>
        /// The room's camera, switched off while the board is up and restored on the way out.
        ///
        /// <para><b>Not merely tidy — the board does not reliably appear without it.</b> A
        /// <c>Camera</c> created in code defaults to depth 0 and POC04_Room's Main Camera is
        /// also depth 0, so which one wins is undefined; <c>BoardView</c> now sets depth 100 to
        /// settle that, and this is the second half. The room's culling mask is
        /// <c>0xFFFFFFFF</c>, i.e. it renders the <c>Table</c> layer too, which C5.1 says it
        /// must not. Switching it off costs nothing while the player cannot see the room
        /// anyway, and means the board is the only thing drawing.</para>
        /// </summary>
        Camera roomCamera;
        bool roomCameraWasEnabled;

        void SuspendRoom()
        {
            if (roomCamera == null) roomCamera = Camera.main;

            roomCameraWasEnabled = roomCamera != null && roomCamera.enabled;
            if (roomCamera != null) roomCamera.enabled = false;
            else Debug.LogWarning("[TableSession] No Camera.main to suspend — the room will " +
                                  "keep drawing over the board.", this);

            // Captured, not assumed — see the class comment on A3.
            controllerWasEnabled = controller != null && controller.enabled;
            interactorWasEnabled = interactor != null && interactor.enabled;
            handsWasEnabled = hands != null && hands.enabled;

            // C8.5: the components, never just the map.
            if (controller != null) controller.enabled = false;   // and, by C8.6, the cursor
            if (interactor != null) interactor.enabled = false;   // and, by C8.7, the prompt
            if (hands != null) hands.enabled = false;
        }

        void ResumeRoom()
        {
            if (roomCamera != null && roomCameraWasEnabled) roomCamera.enabled = true;

            if (hands != null && handsWasEnabled) hands.enabled = true;
            if (interactor != null && interactorWasEnabled) interactor.enabled = true;

            // Last, because its OnEnable is what re-locks the cursor (C8.6) and re-enables
            // the Player map — including OpenTable, so C works again the moment it should.
            if (controller != null && controllerWasEnabled) controller.enabled = true;
        }

        /// <summary>
        /// Finds the scene's EventSystem or builds one. See the class comment: the project
        /// has none, and a canvas without one is visible and inert.
        /// </summary>
        void EnsureEventSystem()
        {
            if (events == null)
            {
                events = EventSystem.current;
                if (events == null) events = FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
                ownsEvents = false;
            }

            if (events == null)
            {
                var go = new GameObject("EventSystem");
                events = go.AddComponent<EventSystem>();

                var module = go.AddComponent<InputSystemUIInputModule>();

                // Assigning the asset is what binds Point/Click/Navigate/Cancel: the setter
                // resolves them by name out of the UI map. Left null the module falls back to
                // its own built-in default actions, which work but are a SECOND asset feeding
                // a second copy of the same bindings — so a rebind made in ours would not
                // reach the table, and nothing would look broken enough to explain why.
                if (inputActions != null) module.actionsAsset = inputActions;

                ownsEvents = true;
            }

            // Switched on either way — a canvas over a sleeping EventSystem is inert, and
            // that is true of a borrowed one too. Only OURS is switched off again on close:
            // an EventSystem we found belongs to whoever put it there, and leaving it on in
            // a room with no active canvas costs nothing, while turning off somebody else's
            // input module on the way out of a table could cost them everything.
            events.gameObject.SetActive(true);
        }
    }
}
