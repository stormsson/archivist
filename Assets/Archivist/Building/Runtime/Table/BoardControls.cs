using UnityEngine;
using UnityEngine.InputSystem;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The board's input: which office is showing (Q4.3), and nothing else.
    ///
    /// <para><b>What this is not.</b> It does not select, drag, rotate, fit or place anything —
    /// there is nothing to place. A plate lies at its quarter and nowhere else (Q4.1), so the
    /// only thing a player does at a table is look at it and change which hand they are looking
    /// at.</para>
    ///
    /// <para><b>There is no zoom and no pan, deliberately.</b>
    /// <c>TableOptions.BoardZoom</c> is 1 — the whole board, and as far out as the camera goes —
    /// so the framing is fixed and the pan travel is zero by construction. A board is a thing to
    /// look at rather than to work on; there is nothing to lean in on.</para>
    ///
    /// <para><b>The machinery is kept, not deleted</b>, the same way rotation is (D-Q1):
    /// <c>BoardViewport</c>, <c>BoardView.MoveView</c>, <c>BoardView.ZoomViewAbout</c> and
    /// <c>Wheel</c> all still work and all still have no caller. Wiring a wheel back to them is
    /// twenty lines, and this is where they would go.</para>
    ///
    /// <para><b><c>Q</c> and <c>E</c> keep their keys and change their meaning.</b> They used to
    /// turn a sheet; nothing turns now (D-Q1). They cycle the visible office, which is the
    /// gesture the whole table is for: the same ground in two hands, and the difference is what
    /// each office left out. The <c>Table/Turn</c> action is reused rather than renamed so the
    /// bindings asset does not have to change under a running scene.</para>
    ///
    /// <para><b>Read on the edge, not on the value.</b> <c>Turn</c> is a 1D axis and holding
    /// <c>E</c> reports 1 every frame; cycling per frame would spin through three offices in a
    /// twentieth of a second. A layer changes when the key goes down.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardControls : MonoBehaviour
    {
        [Tooltip("The board this drives. Found in the scene on first use when left empty.")]
        [SerializeField] BoardView board;

        [Tooltip("The asset holding the Table/Turn action. Found in the scene on first use.")]
        [SerializeField] InputActionAsset inputActions;

        InputAction turnAction;
        float lastTurn;

        /// <summary>
        /// The scene's controls, made if there are none.
        ///
        /// <para><b>Why this makes itself.</b> Every other piece of the rig —
        /// <c>BoardView</c>, <c>TableCanvas</c>, <c>TableSession</c> — is resolved by
        /// <c>TableSession.Awake</c> with <c>FindFirstObjectByType</c>, so a scene that has them
        /// works whether or not anyone wired it. This component is the one piece that only
        /// existed if somebody ran <c>Archivist ▸ Cartography Table · Build Rig</c>, and a scene
        /// is written by hand and by a menu item — so it sat a week behind the code and
        /// <c>Q</c>/<c>E</c> silently did nothing. A rig that is missing its input is not a
        /// scene the player can be asked to notice.</para>
        ///
        /// <para>Play mode only, for the reason <c>Archive.InScene</c> gives: an editor tool
        /// that silently added a GameObject to somebody's scene would be a change they did not
        /// make. The rig builder still creates one, so a built scene has it authored.</para>
        /// </summary>
        public static BoardControls EnsureInScene(InputActionAsset actions)
        {
            BoardControls found = FindFirstObjectByType<BoardControls>(FindObjectsInactive.Include);
            if (found == null)
            {
                if (!Application.isPlaying) return null;

                var go = new GameObject("BoardControls");
                found = go.AddComponent<BoardControls>();
            }

            // Handed the caller's asset rather than left to find its own: TableSession already
            // holds the one the session is driven by, and two components resolving separately is
            // two chances to disagree about which asset the game is bound to.
            if (found.inputActions == null && actions != null)
            {
                found.inputActions = actions;
                found.Rebind();
            }
            return found;
        }

        /// <summary>Re-reads <c>Table/Turn</c> from whatever asset is now set. <c>OnEnable</c>
        /// has usually run before the asset arrives, so the binding is redone rather than
        /// waited for.</summary>
        void Rebind()
        {
            if (inputActions == null) return;

            InputActionMap table = inputActions.FindActionMap("Table", throwIfNotFound: false);
            turnAction = table != null ? table.FindAction("Turn") : null;
        }

        BoardView Board
        {
            get
            {
                if (board == null) board = FindFirstObjectByType<BoardView>(FindObjectsInactive.Include);
                return board;
            }
        }

        void OnEnable()
        {
            if (inputActions == null) inputActions = FindTableActions();
            Rebind();
        }

        void Update()
        {
            BoardView view = Board;
            if (view == null || !view.IsShowing || view.BoardCamera == null) return;

            Layers(view);
        }

        void Layers(BoardView view)
        {
            if (turnAction == null) return;

            float now = turnAction.ReadValue<float>();
            float was = lastTurn;
            lastTurn = now;

            // The edge, and only the edge. Sign rather than magnitude: the axis is -1 or +1 and
            // a composite that ever reported 0.7 should still be one step.
            if (now > 0.5f && was <= 0.5f) view.CycleLayer(+1);
            else if (now < -0.5f && was >= -0.5f) view.CycleLayer(-1);
        }

        /// <summary>The asset carrying <c>Table/Turn</c>, found rather than required — the same
        /// lookup the interactor this replaces used.</summary>
        static InputActionAsset FindTableActions()
        {
            InputActionAsset[] all = Resources.FindObjectsOfTypeAll<InputActionAsset>();
            for (int i = 0; i < all.Length; i++)
            {
                InputActionAsset a = all[i];
                if (a == null) continue;

                InputActionMap map = a.FindActionMap("Table");
                if (map != null && map.FindAction("Turn") != null) return a;
            }
            return null;
        }
    }
}
