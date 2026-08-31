using Archivist.Building.Collection;
using Archivist.Generation;
using Archivist.Generation.Sheets;
using UnityEngine;
using UnityEngine.UI;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The chrome over the cartography board: a full-width header naming the island and the
    /// office layer on screen. C1.1 draws the composition area as real slabs under an
    /// orthographic camera and puts <i>only</i> the chrome in UGUI, and this is that chrome.
    /// Nothing here knows the board camera exists.
    ///
    /// <para><b>Built in code, like the room.</b> No prefab, no UXML: every number here is
    /// provisional — measured off <c>1b-empty-table.png</c> at 1442 px and scaled to a 1920
    /// canvas, a guess about a screen nobody has played on — and a prefab would freeze it into a
    /// binary asset where adjusting a hairline is a merge conflict. All of them live in
    /// <see cref="TableStyle"/>; none in this file.</para>
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
    /// alive between openings, and — the part that matters — stops this canvas swallowing
    /// clicks meant for the room while the player is walking around it.</para>
    ///
    /// <para><b>Why an <see cref="IslandGenerator"/> is wanted.</b> The island's name is a
    /// function of its seed and nothing caches it here (R1.11), so naming the header means
    /// holding the whole <see cref="Island"/>. It is resolved once per opening, through the
    /// generator's cache when there is one — never per drawn row, which would hide a 340 ms
    /// generation inside a UI call (C7.7a). Without a generator it falls back to
    /// <c>Island.FromSeed</c>: correct but uncached, and for a test bench rather than for
    /// play.</para>
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

        Canvas canvas;
        GraphicRaycaster raycaster;

        Text islandNameText;
        Text officeNameText;
        Text officeCodeText;

        BoardView board;
        Island island;
        bool built;

        /// <summary>The island whose paperwork is on screen, or 0 while hidden.</summary>
        public ulong IslandSeed { get; private set; }

        /// <summary>True between <see cref="Show"/> and <see cref="Hide"/>.</summary>
        public bool IsShown { get { return canvas != null && canvas.enabled; } }

        /// <summary>What the office field reads when the board has no layers — an island whose
        /// binders hold nothing but the chart. Assembled from <see cref="OfficeLabels.Separator"/>
        /// so the placeholder and a real code cannot disagree about the middle dot.</summary>
        public static readonly string NoLayerCode =
            TableStyle.UnknownName + OfficeLabels.Separator + TableStyle.UnknownName;

        /// <summary>The same, for the name.</summary>
        public const string NoLayerName = "No office";

        /// <summary>
        /// How much of the screen's top edge the header covers, in pixels.
        ///
        /// <para>Off the canvas's own <c>scaleFactor</c>, never
        /// <c>HeaderHeight / ReferenceHeight</c>: the scaler's match is 0.5, so the band's screen
        /// height is a function of the window's width as well as its height, and the scaler is
        /// the only thing that knows the factor it settled on.</para>
        /// </summary>
        public float HeaderScreenHeight
        {
            get { return canvas == null ? 0f : TableStyle.HeaderHeight * canvas.scaleFactor; }
        }

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
        }

        void OnDestroy()
        {
            if (board != null) board.Changed -= OnBoardChanged;
            board = null;
        }

        /// <summary>
        /// Keeps the board out from under the header (§5.1). The band is opaque chrome over a
        /// camera that fills the screen, and at zoom 1 the board's full height is in frame — so
        /// whatever the header covers is board no pan can reach. <c>BoardView</c> narrows its
        /// rect by what it is told.
        ///
        /// <para>Pushed every frame rather than at <see cref="Show"/>: the scaler settles its
        /// factor after the canvas is enabled, and a window can be resized while the table is
        /// open. The board writes its camera only when the rectangle actually moves.</para>
        /// </summary>
        void Update()
        {
            if (board == null || canvas == null || !canvas.enabled) return;

            board.TopInsetPixels = HeaderScreenHeight;
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
                                : TableStyle.UnknownName;

            ShowOffice();

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

            ShowOffice();
            islandNameText.text = TableStyle.UnknownName;

            canvas.enabled = false;
            raycaster.enabled = false;
        }

        /// <summary>
        /// Writes the OFFICE field of the header: whose hand the board is showing (Q4.3).
        ///
        /// <para><b>Display only.</b> It reports the board's layer; it does not choose one.
        /// <c>BoardControls</c> owns that, and this follows through <c>BoardView.Changed</c>.</para>
        ///
        /// <para>The code line carries the office's prefix and its place in the cycle —
        /// <c>L S · 2/3</c> — so a player knows both who drew this and that there are two more
        /// to see. Without the position, one office looks like the only one.</para>
        /// </summary>
        public void ShowOffice()
        {
            Build();

            int count = board != null ? board.Layers.Count : 0;
            if (count == 0 || board.LayerIndex < 0)
            {
                officeNameText.text = NoLayerName;
                officeNameText.color = TableStyle.Muted;
                officeCodeText.text = TableStyle.Spaced(NoLayerCode);
                return;
            }

            Office office = board.ActiveLayer;

            officeNameText.text = OfficeLabels.OfficeTitleFor(office);
            officeNameText.color = TableStyle.Ink;
            officeCodeText.text = TableStyle.Spaced(
                OfficeLabels.PrefixFor(office) + OfficeLabels.Separator
                + (board.LayerIndex + 1) + "/" + count);
        }

        // --------------------------------------------------------------------

        /// <summary>Null while the canvas is screen-space-overlay, which <see cref="Build"/>
        /// makes it. Read off the canvas rather than assumed, for the same reason the ghost
        /// does it.</summary>
        Camera EventCamera()
        {
            if (canvas == null) return null;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        /// <summary>Plates land one per frame while a board opens (C5.6), so this fires
        /// repeatedly. Rewriting the office field each time is cheap and keeps the header
        /// honest about the layer the board switched to.</summary>
        void OnBoardChanged()
        {
            ShowOffice();
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

            canvas.enabled = false;
            raycaster.enabled = false;
        }

        /// <summary>
        /// The header of <c>1b-empty-table.png</c>: ISLAND and its name, a rule, OFFICE and the
        /// active layer's name and code.
        ///
        /// <para><b>Laid out with layout groups rather than fixed x positions</b>, which is the
        /// one place in this view where that is worth the cost. In the mockup the island name
        /// ends a pixel short of the divider — <i>Ilha do Corvo</i> happens to fit. A longer name
        /// at a fixed divider would run straight through it. So the ISLAND block sizes to its own
        /// text with a floor under it (<see cref="TableStyle.IslandFieldMinWidth"/>), so the
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
            rt.offsetMin = new Vector2(0f, -TableStyle.HeaderHeight);
            rt.offsetMax = Vector2.zero;

            var plate = TableStyle.Plate(rt, "Plate", TableStyle.HeaderCream);
            plate.raycastTarget = true;      // the header swallows clicks meant for the board

            TableStyle.Hairline(rt, "BottomRule", TableStyle.Rule,
                                  new Vector2(0f, 0f), new Vector2(1f, 0f),
                                  new Vector2(0f, TableStyle.HairlineWidth));

            var fieldsGo = new GameObject("Fields", typeof(RectTransform));
            fieldsGo.transform.SetParent(rt, false);
            TableStyle.Stretch((RectTransform)fieldsGo.transform);

            var row = fieldsGo.AddComponent<HorizontalLayoutGroup>();
            row.spacing = TableStyle.HeaderFieldGap;
            row.padding = new RectOffset((int)TableStyle.HeaderPadLeft, 0, 0, 0);
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            // ---- ISLAND ----
            RectTransform islandField = Field(fieldsGo.transform, "IslandField");
            islandField.gameObject.AddComponent<LayoutElement>().minWidth =
                TableStyle.IslandFieldMinWidth;

            TableStyle.Label(islandField, "Label", TableStyle.Spaced("Island"),
                               TableStyle.Sans(), TableStyle.HeaderLabelSize,
                               TableStyle.Muted);

            islandNameText = TableStyle.Label(islandField, "Value", TableStyle.UnknownName,
                                                TableStyle.Serif(), TableStyle.IslandNameSize,
                                                TableStyle.Ink);

            // ---- divider ----
            var dividerGo = new GameObject("Divider", typeof(RectTransform));
            dividerGo.transform.SetParent(fieldsGo.transform, false);

            var divider = dividerGo.AddComponent<Image>();
            divider.color = TableStyle.Rule;
            divider.raycastTarget = false;

            var dividerElement = dividerGo.AddComponent<LayoutElement>();
            dividerElement.preferredWidth = TableStyle.HairlineWidth;
            dividerElement.minWidth = TableStyle.HairlineWidth;
            dividerElement.preferredHeight = TableStyle.HeaderDividerHeight;
            dividerElement.minHeight = TableStyle.HeaderDividerHeight;

            // ---- OFFICE ----
            RectTransform sheetField = Field(fieldsGo.transform, "OfficeField");

            TableStyle.Label(sheetField, "Label", TableStyle.Spaced("Office"),
                               TableStyle.Sans(), TableStyle.HeaderLabelSize,
                               TableStyle.Muted);

            var sheetLineGo = new GameObject("Value", typeof(RectTransform));
            sheetLineGo.transform.SetParent(sheetField, false);

            var sheetLine = sheetLineGo.AddComponent<HorizontalLayoutGroup>();
            sheetLine.spacing = TableStyle.HeaderLabelGap * 3f;
            // Lower-left, not middle: the code is half the size of the name and should sit on
            // the same line as it, not float in the middle of its cap height.
            sheetLine.childAlignment = TextAnchor.LowerLeft;
            sheetLine.childControlWidth = true;
            sheetLine.childControlHeight = true;
            sheetLine.childForceExpandWidth = false;
            sheetLine.childForceExpandHeight = false;

            officeNameText = TableStyle.Label((RectTransform)sheetLineGo.transform, "Name",
                                               NoLayerName, TableStyle.Serif(),
                                               TableStyle.SheetNameSize, TableStyle.Muted);

            officeCodeText = TableStyle.Label((RectTransform)sheetLineGo.transform, "Code",
                                               TableStyle.Spaced(NoLayerCode),
                                               TableStyle.Sans(), TableStyle.SheetCodeSize,
                                               TableStyle.Muted);
            officeCodeText.alignment = TextAnchor.LowerLeft;
        }

        /// <summary>A label-over-value block: the shape both header fields share.</summary>
        static RectTransform Field(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var group = go.AddComponent<VerticalLayoutGroup>();
            group.spacing = TableStyle.HeaderLabelGap;
            group.padding = new RectOffset(0, 0, 0, 0);
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;

            return (RectTransform)go.transform;
        }
    }
}
