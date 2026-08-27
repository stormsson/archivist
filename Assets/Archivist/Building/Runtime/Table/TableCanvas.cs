using Archivist.Building.Collection;
using Archivist.Generation;
using Archivist.Generation.Sheets;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The chrome over the cartography board: the full-width header (C7.6) and the right-hand
    /// cabinet (C7.1–C7.4). C1.1 draws the composition area as real <c>SheetView</c> slabs under
    /// an orthographic camera and puts <i>only</i> the chrome in UGUI, and this is that chrome.
    /// Nothing here knows the board camera exists.
    ///
    /// <para><b>Built in code, like the room.</b> No prefab, no UXML: every number here is
    /// provisional — measured off <c>1b-empty-table.png</c> at 1442 px and scaled to a 1920
    /// canvas, a guess about a screen nobody has played on — and a prefab would freeze it into a
    /// binary asset where adjusting a hairline is a merge conflict. All of them live in
    /// <see cref="CabinetStyle"/>; none in this file.</para>
    ///
    /// <para><b>UGUI, not UI Toolkit.</b> <c>InteractionPrompt</c> and
    /// <c>SceneParts.BuildInteractionUi</c> are already legacy UGUI, and the assembly definition
    /// already references <c>UnityEngine.UI</c>. A second UI stack would mean two event systems,
    /// two ways to say "cream", and two places to look when the header is wrong.</para>
    ///
    /// <para><b>Hidden by disabling the canvas, not the GameObject.</b> §5.1 says the canvas is
    /// "disabled until opened", and the obvious reading — <c>SetActive(false)</c> — cannot work:
    /// <c>Awake</c> would never run, so the hierarchy would not exist, so the first
    /// <see cref="Show"/> would have nothing to fill. Turning off the <see cref="Canvas"/> and
    /// its <see cref="GraphicRaycaster"/> costs the same nothing, keeps the built hierarchy
    /// alive between openings, and — the part that matters — stops the cabinet swallowing
    /// clicks meant for the room while the player is walking around it.</para>
    ///
    /// <para><b>This is where the cabinet meets the board, and the only place that knows
    /// both.</b> The cabinet reports gestures in screen space; <c>BoardInteractor</c> owns
    /// selection and placement in the board's space, and neither references the other. This
    /// class translates: a drop becomes <c>BeginPlace</c>, the interactor's selection becomes a
    /// header line (C7.6). <see cref="SetSelected"/> stays a pure display update, because
    /// selection is <i>decided</i> elsewhere and the header only reports it.</para>
    ///
    /// <para><b>A group crosses the same seam as a sheet, by the same two events</b> (G6.1): a
    /// click becomes a selection, a drop over the composition area becomes
    /// <c>BoardInteractor.PlaceGroup</c> (G6.5). The other direction — a group dragged off the
    /// board onto the column and parked (G6.4) — never reaches this class, because it is a board
    /// gesture and <c>ReleaseOverCabinet</c> is how the board hears about the column.</para>
    ///
    /// <para><b>Board or cabinet is decided by one rectangle, negatively.</b> "Over the board" is
    /// <i>not</i> inside the cabinet's rect (C7.5). Hit-testing the board positively means a
    /// physics raycast through <c>BoardCamera</c>, which makes this chrome depend on the camera
    /// it is documented above as not knowing about — and a drop on the dark wood <i>beside</i>
    /// the mounting sheet would hit nothing and be discarded, when it is plainly a drop on the
    /// table.</para>
    ///
    /// <para><b>Why the refile flag comes from the cabinet's pointer-enter and not the row
    /// drag.</b> C7.5 has two directions and only one passes through
    /// <see cref="CabinetPanel.Dragging"/>: a slab dragged off the board and back into the drawer
    /// is a board gesture this class never hears about, so driving <c>ReleaseOverCabinet</c> from
    /// row drags leaves the flag stale for exactly the case it exists to serve.
    /// <see cref="CabinetPanel.PointerOverChanged"/> is true of the pointer at any moment,
    /// whatever is being dragged. It is written from one place only: a wrong value here does not
    /// misdraw something, it refiles a sheet the player meant to keep.</para>
    ///
    /// <para><b>Why an <see cref="IslandGenerator"/> is wanted.</b> A sheet's name needs the
    /// whole island — <c>SheetNaming.NameFor</c> takes an <see cref="Island"/> deliberately, so a
    /// UI cannot hide a 340 ms generation inside a call it makes once per visible row (C7.7a). So
    /// the island is resolved once per opening, through the generator's cache when there is one.
    /// Without a generator it falls back to <c>Island.FromSeed</c>: correct but uncached, and for
    /// a test bench rather than for play.</para>
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public sealed class TableCanvas : MonoBehaviour
    {
        /// <summary>Reference resolution, matching <c>SceneParts.BuildInteractionUi</c> so the
        /// prompt and this view scale together.</summary>
        public const float ReferenceWidth = 1920f;
        public const float ReferenceHeight = 1080f;

        /// <summary>Drawn over the room's own canvas: this is a full-screen mode, and the aim
        /// reticle must not float on top of the board.</summary>
        public const int SortingOrder = 100;

        [Tooltip("Optional. Supplies the cached island a sheet's name is derived from. Found in " +
                 "the scene on first use when left empty; without one, islands are generated " +
                 "uncached, which is fine for a bench and not for play.")]
        [SerializeField] IslandGenerator generator;

        [Tooltip("Optional. Owns what is selected on the board and what gets laid down. Found " +
                 "in the scene on first use when left empty; without one the cabinet still " +
                 "draws and still scrolls, and a drop onto the board does nothing.")]
        [SerializeField] BoardInteractor interactor;

        Canvas canvas;
        GraphicRaycaster raycaster;

        Text islandNameText;
        Text sheetNameText;
        Text sheetCodeText;
        CabinetPanel cabinet;

        BoardView board;
        Island island;
        bool built;
        bool selectionHooked;

        /// <summary>The island whose paperwork is on screen, or 0 while hidden.</summary>
        public ulong IslandSeed { get; private set; }

        /// <summary>True between <see cref="Show"/> and <see cref="Hide"/>.</summary>
        public bool IsShown { get { return canvas != null && canvas.enabled; } }

        /// <summary>C7.6's empty reading, assembled from <see cref="SheetNaming.Separator"/> so
        /// that the placeholder and a real code can never disagree about the middle dot.</summary>
        public static readonly string NoSelectionCode =
            CabinetStyle.UnknownName + SheetNaming.Separator + CabinetStyle.UnknownName;

        /// <summary>C7.6, verbatim from <c>1b-empty-table.png</c>.</summary>
        public const string NoSelectionName = "None selected";

        // --------------------------------------------------------------------

        /// <summary>
        /// Makes the canvas object §5.1 names, with this component on it. Offered because
        /// nothing else in the scene owns this view's shape: the caller that builds the board
        /// should not have to know it needs a <see cref="CanvasScaler"/> set to 1920 × 1080.
        /// </summary>
        public static TableCanvas Create(Transform parent = null)
        {
            var go = new GameObject("TableCanvas",
                                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (parent != null) go.transform.SetParent(parent, false);

            return go.AddComponent<TableCanvas>();
        }

        void Awake()
        {
            Build();
            ResolveInteractor();
        }

        void OnDestroy()
        {
            if (board != null) board.Changed -= OnBoardChanged;
            board = null;

            // A delegate left on an object that survives this one throws on the next domain
            // reload, when the target has been serialized away and the invocation list has not.
            UnhookSelection();
        }

        // --------------------------------------------------------------------

        /// <summary>
        /// Opens the view on one island and one board. Idempotent — calling it again with a
        /// different board swaps cleanly, which is what happens when the player closes one table
        /// and opens another.
        ///
        /// <para>Nothing here waits for a texture. C5.7 is explicit that opening a table costs
        /// one island generation plus N renders and that <b>the view opens on the mounting sheet
        /// with the cabinet filling in</b>; every row is drawn immediately with a blank plate and
        /// fills in on <c>BoardView.Changed</c> as uploads land, one per frame (C5.6).</para>
        /// </summary>
        /// <param name="islandSeed">The island the header names. Authoritative even if
        /// <paramref name="board"/> disagrees — the caller said which island this is.</param>
        /// <param name="board">The board being shown. May be null, which draws an empty
        /// cabinet rather than throwing.</param>
        public void Show(ulong islandSeed, BoardView board)
        {
            Build();

            if (this.board != null) this.board.Changed -= OnBoardChanged;

            this.board = board;
            IslandSeed = islandSeed;
            island = ResolveIsland(islandSeed);

            if (this.board != null) this.board.Changed += OnBoardChanged;

            islandNameText.text = (island != null && !string.IsNullOrEmpty(island.Name))
                                ? island.Name
                                : CabinetStyle.UnknownName;

            cabinet.Bind(island, board);

            // Hooked on Show rather than on Awake: the interactor may be built with the board,
            // after this canvas, and an opening is the first moment both are certain to exist.
            HookSelection();
            if (interactor != null) interactor.ReleaseOverCabinet = false;

            SetSelected(interactor != null ? interactor.Selected : null);

            canvas.enabled = true;
            raycaster.enabled = true;
        }

        /// <summary>
        /// Closes the view and drops its hold on the board. The hierarchy survives; the island
        /// does not, because the generator's cache is the right place to keep one and holding a
        /// second reference here would keep a dead island alive after the cache had let go.
        /// </summary>
        public void Hide()
        {
            Build();

            if (board != null) board.Changed -= OnBoardChanged;
            board = null;
            island = null;
            IslandSeed = 0UL;

            UnhookSelection();

            // The pointer's last known side of the screen must not survive a closed table: with
            // the canvas off nothing raises pointer-exit, so a table closed with the cursor in
            // the cabinet would leave "release means refile" armed for the next opening.
            if (interactor != null) interactor.ReleaseOverCabinet = false;

            cabinet.Clear();
            SetSelected(null);
            islandNameText.text = CabinetStyle.UnknownName;

            canvas.enabled = false;
            raycaster.enabled = false;
        }

        /// <summary>
        /// Writes the SHEET field of the header (C7.6). <b>Display only</b> — it selects
        /// nothing, moves nothing and changes no row: C7.4 allows the cabinet two states, in the
        /// drawer and on the table, and "selected" is not one of them. Where the selection is
        /// visible is on the board, where the sheet is.
        ///
        /// <para>Null reads <c>None selected  —·—</c>, exactly as <c>1b-empty-table.png</c>
        /// draws it. A sheet the board cannot resolve reads a dash and its real code, because
        /// the code is derivable from the <see cref="SheetId"/> alone and is still true.</para>
        /// </summary>
        public void SetSelected(SheetId? id)
        {
            Build();

            if (!id.HasValue)
            {
                sheetNameText.text = NoSelectionName;
                sheetNameText.color = CabinetStyle.Muted;
                sheetCodeText.text = CabinetStyle.Spaced(NoSelectionCode);
                return;
            }

            SheetId sheetId = id.Value;
            string name = CabinetStyle.UnknownName;

            if (board != null && island != null)
            {
                Sheet sheet;
                if (board.TrySheet(sheetId, out sheet))
                {
                    string resolved = SheetNaming.NameFor(island, sheet);
                    if (!string.IsNullOrEmpty(resolved)) name = resolved;
                }
            }

            sheetNameText.text = name;
            sheetNameText.color = CabinetStyle.Ink;
            sheetCodeText.text = CabinetStyle.Spaced(SheetNaming.CodeFor(sheetId));
        }

        // --------------------------------------------------------------------

        /// <summary>
        /// C7.6 — a row click tells the BOARD first, and the header follows from that.
        ///
        /// <para>Setting the header directly lies: the interactor keeps whatever was selected on
        /// the board, so the header names one sheet while <c>Q</c>/<c>E</c> turns another. Going
        /// through <c>Select</c> makes the board the authority and leaves the header a view of
        /// it, updated by <c>SelectionChanged</c> like every other selection change.</para>
        ///
        /// <para>The direct <c>SetSelected</c> stays as the fallback for a table with no
        /// interactor wired, where the cabinet is still worth reading.</para>
        /// </summary>
        void OnRowClicked(SheetId id)
        {
            if (interactor != null) interactor.Select(id);
            else SetSelected(id);
        }

        /// <summary>
        /// A row released over the board lays its sheet down at the drop point (C7.5); released
        /// anywhere inside the cabinet it never left the drawer, and nothing happens — no
        /// message, no snap-back, because the ghost is already gone and the row never changed.
        /// The header is deliberately not touched here either: what is selected after a
        /// placement is the interactor's to say, and it says so through
        /// <c>SelectionChanged</c>.
        /// </summary>
        void OnRowDragEnded(SheetId id, PointerEventData eventData)
        {
            if (interactor == null) return;
            if (IsOverCabinet(eventData.position)) return;

            interactor.BeginPlace(id);
        }

        /// <summary>
        /// A click on a Groups row selects the assembly (G6.1, C7.6 applied to the new section).
        ///
        /// <para><b>It selects a member, because that is what a selection is.</b> G1.6 makes the
        /// group the unit of interaction by way of the clicked sheet, so the row hands over the
        /// first member in join order — the sheet the row's own label names (G6.3's "from n").
        /// Selecting the group follows, and <c>Q</c>/<c>E</c> and the corner handle then turn the
        /// whole assembly.</para>
        ///
        /// <para>For a <b>parked</b> group this clears the board selection rather than setting
        /// one, because <c>Select</c> refuses a sheet that is not on the table — the same answer
        /// a click on a drawer row gives.</para>
        /// </summary>
        void OnGroupRowClicked(int groupId)
        {
            if (interactor == null || board == null) return;

            GroupRecord group;
            if (!board.TryGetGroup(groupId, out group)) return;
            if (group.Members == null || group.Members.Count == 0) return;

            interactor.Select(group.Members[0]);
        }

        /// <summary>
        /// G6.5: a Groups row released over the composition area lays the whole assembly back
        /// down under the pointer, preserving its frame rotation φ. Released inside the cabinet
        /// it never left the drawer and nothing happens — the same silence
        /// <see cref="OnRowDragEnded"/> keeps, and C6.6's shape of failure (R6.5).
        ///
        /// <para>The board/cabinet decision is <see cref="IsOverCabinet"/>'s, unchanged: one
        /// rectangle, asked negatively, so a group and a sheet cannot disagree about where the
        /// column ends.</para>
        /// </summary>
        void OnGroupDragEnded(int groupId, PointerEventData eventData)
        {
            if (interactor == null) return;
            if (IsOverCabinet(eventData.position)) return;

            interactor.PlaceGroup(groupId);
        }

        /// <summary>The cabinet's edge, crossed. See the class comment for why this one event
        /// owns <c>ReleaseOverCabinet</c> and the row drag does not.</summary>
        void OnPointerOverCabinet(bool over)
        {
            if (interactor != null) interactor.ReleaseOverCabinet = over;
        }

        void OnSelectionChanged(SheetId? id) { SetSelected(id); }

        /// <summary>
        /// Geometric, not a raycast: the answer must be the same whether or not a row happens to
        /// lie under the pointer, and must not depend on the scroll viewport's mask. It differs
        /// from the pointer-enter chain only along the column's one-pixel edge, and a drop
        /// exactly on that line is a drop the player cannot have meant either way.
        ///
        /// <para>The header is <i>not</i> the cabinet, so a drop on it counts as a drop on the
        /// board. Left that way deliberately: the header is a strip along the top of the board,
        /// the drop point is the interactor's to read, and inventing a third "neither" region
        /// would mean a gesture that visibly ends nowhere.</para>
        /// </summary>
        bool IsOverCabinet(Vector2 screenPoint)
        {
            if (cabinet == null) return false;

            return RectTransformUtility.RectangleContainsScreenPoint(
                cabinet.Rect, screenPoint, EventCamera());
        }

        /// <summary>Null while the canvas is screen-space-overlay, which <see cref="Build"/>
        /// makes it. Read off the canvas rather than assumed, for the same reason the ghost
        /// does it.</summary>
        Camera EventCamera()
        {
            if (canvas == null) return null;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        /// <summary><c>FindObjectsInactive.Include</c>, unlike the generator lookup: the board
        /// rig is off while the player is walking around the room, and the one moment this
        /// canvas most wants the interactor — the first <see cref="Show"/> — is the moment
        /// before it has been switched on.</summary>
        void ResolveInteractor()
        {
            if (interactor == null)
                interactor = FindFirstObjectByType<BoardInteractor>(FindObjectsInactive.Include);
        }

        void HookSelection()
        {
            ResolveInteractor();
            if (interactor == null || selectionHooked) return;

            interactor.SelectionChanged += OnSelectionChanged;
            selectionHooked = true;
        }

        void UnhookSelection()
        {
            if (interactor != null && selectionHooked) interactor.SelectionChanged -= OnSelectionChanged;
            selectionHooked = false;
        }

        void OnBoardChanged()
        {
            // C5.6 — thumbnails arrive late and one per frame, so this fires repeatedly during
            // an opening. Refresh re-reads textures and row states and rebuilds only when the
            // set of available sheets actually changed; it must stay cheap for that reason.
            if (cabinet != null) cabinet.Refresh();
        }

        Island ResolveIsland(ulong islandSeed)
        {
            if (generator == null) generator = FindFirstObjectByType<IslandGenerator>();
            if (generator != null) return generator.GetOrGenerate(islandSeed);

            // No generator in the scene: correct, uncached, and slow enough to notice. See the
            // class comment — this is the bench path, not the play path.
            return Island.FromSeed(islandSeed);
        }

        // --------------------------------------------------------------------

        void Build()
        {
            if (built) return;
            built = true;

            canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.matchWidthOrHeight = 0.5f;

            raycaster = GetComponent<GraphicRaycaster>();

            var root = (RectTransform)transform;
            BuildHeader(root);
            cabinet = CabinetPanel.Create(root);

            // Never unsubscribed: the cabinet is a child of this object and dies with it, so
            // there is no dangling delegate to leave behind. The interactor is the opposite case
            // — it outlives an opening — and is hooked and unhooked around Show/Hide instead.
            cabinet.RowClicked += OnRowClicked;
            cabinet.DragEnded += OnRowDragEnded;
            cabinet.PointerOverChanged += OnPointerOverCabinet;

            // The Groups section's two halves of the same pair (G6.1). DragStarted and Dragging
            // are deliberately left unwired on both the sheet rows and these — the ghost is the
            // row's own and ReleaseOverCabinet is PointerOverChanged's, so a listener here would
            // be a second writer of facts that already have one (see the class comment).
            cabinet.GroupRowClicked += OnGroupRowClicked;
            cabinet.GroupDragEnded += OnGroupDragEnded;

            canvas.enabled = false;
            raycaster.enabled = false;
        }

        /// <summary>
        /// The header of <c>1b-empty-table.png</c>: ISLAND and its name, a rule, SHEET and the
        /// selected sheet's name and code.
        ///
        /// <para><b>Laid out with layout groups rather than fixed x positions</b>, which is the
        /// one place in this view where that is worth the cost. In the mockup the island name
        /// ends a pixel short of the divider — <i>Ilha do Corvo</i> happens to fit. A longer name
        /// at a fixed divider would run straight through it. So the ISLAND block sizes to its own
        /// text with a floor under it (<see cref="CabinetStyle.IslandFieldMinWidth"/>), so the
        /// divider does not walk left and right as islands come and go, and moves right rather
        /// than being overrun when a name is long.</para>
        /// </summary>
        void BuildHeader(RectTransform parent)
        {
            var go = new GameObject("Header", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -CabinetStyle.HeaderHeight);
            rt.offsetMax = Vector2.zero;

            var plate = CabinetStyle.Plate(rt, "Plate", CabinetStyle.HeaderCream);
            plate.raycastTarget = true;      // the header swallows clicks meant for the board

            CabinetStyle.Hairline(rt, "BottomRule", CabinetStyle.Rule,
                                  new Vector2(0f, 0f), new Vector2(1f, 0f),
                                  new Vector2(0f, CabinetStyle.HairlineWidth));

            var fieldsGo = new GameObject("Fields", typeof(RectTransform));
            fieldsGo.transform.SetParent(rt, false);
            CabinetStyle.Stretch((RectTransform)fieldsGo.transform);

            var row = fieldsGo.AddComponent<HorizontalLayoutGroup>();
            row.spacing = CabinetStyle.HeaderFieldGap;
            row.padding = new RectOffset((int)CabinetStyle.HeaderPadLeft, 0, 0, 0);
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            // ---- ISLAND ----
            RectTransform islandField = Field(fieldsGo.transform, "IslandField");
            islandField.gameObject.AddComponent<LayoutElement>().minWidth =
                CabinetStyle.IslandFieldMinWidth;

            CabinetStyle.Label(islandField, "Label", CabinetStyle.Spaced("Island"),
                               CabinetStyle.Sans(), CabinetStyle.HeaderLabelSize,
                               CabinetStyle.Muted);

            islandNameText = CabinetStyle.Label(islandField, "Value", CabinetStyle.UnknownName,
                                                CabinetStyle.Serif(), CabinetStyle.IslandNameSize,
                                                CabinetStyle.Ink);

            // ---- divider ----
            var dividerGo = new GameObject("Divider", typeof(RectTransform));
            dividerGo.transform.SetParent(fieldsGo.transform, false);

            var divider = dividerGo.AddComponent<Image>();
            divider.color = CabinetStyle.Rule;
            divider.raycastTarget = false;

            var dividerElement = dividerGo.AddComponent<LayoutElement>();
            dividerElement.preferredWidth = CabinetStyle.HairlineWidth;
            dividerElement.minWidth = CabinetStyle.HairlineWidth;
            dividerElement.preferredHeight = CabinetStyle.HeaderDividerHeight;
            dividerElement.minHeight = CabinetStyle.HeaderDividerHeight;

            // ---- SHEET ----
            RectTransform sheetField = Field(fieldsGo.transform, "SheetField");

            CabinetStyle.Label(sheetField, "Label", CabinetStyle.Spaced("Sheet"),
                               CabinetStyle.Sans(), CabinetStyle.HeaderLabelSize,
                               CabinetStyle.Muted);

            var sheetLineGo = new GameObject("Value", typeof(RectTransform));
            sheetLineGo.transform.SetParent(sheetField, false);

            var sheetLine = sheetLineGo.AddComponent<HorizontalLayoutGroup>();
            sheetLine.spacing = CabinetStyle.HeaderLabelGap * 3f;
            // Lower-left, not middle: the code is half the size of the name and should sit on
            // the same line as it, not float in the middle of its cap height.
            sheetLine.childAlignment = TextAnchor.LowerLeft;
            sheetLine.childControlWidth = true;
            sheetLine.childControlHeight = true;
            sheetLine.childForceExpandWidth = false;
            sheetLine.childForceExpandHeight = false;

            sheetNameText = CabinetStyle.Label((RectTransform)sheetLineGo.transform, "Name",
                                               NoSelectionName, CabinetStyle.Serif(),
                                               CabinetStyle.SheetNameSize, CabinetStyle.Muted);

            sheetCodeText = CabinetStyle.Label((RectTransform)sheetLineGo.transform, "Code",
                                               CabinetStyle.Spaced(NoSelectionCode),
                                               CabinetStyle.Sans(), CabinetStyle.SheetCodeSize,
                                               CabinetStyle.Muted);
            sheetCodeText.alignment = TextAnchor.LowerLeft;
        }

        /// <summary>A label-over-value block: the shape both header fields share.</summary>
        static RectTransform Field(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            VerticalLayoutGroup group = CabinetStyle.Stack(go, CabinetStyle.HeaderLabelGap);
            group.childForceExpandWidth = false;

            return (RectTransform)go.transform;
        }
    }
}
