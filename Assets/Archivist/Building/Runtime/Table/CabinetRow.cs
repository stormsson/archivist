using System;
using Archivist.Building.Collection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Archivist.Building.Table
{
    /// <summary>
    /// One line of the cabinet: a thumbnail, a serif title, a spaced code (C7.3). Built entirely
    /// in code — no prefab — for the reason <c>RoomBuilder</c> gives about the room: while the
    /// numbers are provisional they have to be cheap to change, and a prefab makes a spacing
    /// tweak a binary merge conflict instead of a one-line diff.
    ///
    /// <para><b>Two states, and no third (C7.4).</b> <i>In the drawer</i> is plain: white plate,
    /// hairline border, ink name. <i>On the table</i> is gold border, gold tint, gold title, the
    /// thumbnail tilted a few degrees off-square, and a table mark on the right. That is the
    /// whole vocabulary. The ✓ drawn on some rows in <c>1a-mid-assembly.png</c> and
    /// <c>1c-snap-moment.png</c> is legacy and must not be reproduced: D-C4 says the
    /// <c>2a-cabinet-states.png</c> legend governs and those two mockups were simply never
    /// re-rendered. Nor is there a "seated" row state — seating is visible on the board, and
    /// R5.5 is explicit that a cabinet full of checkmarks turns an archive into a score sheet.
    /// D-C3 keeps the section <i>count</i> against R5.5 and drops the mark; a per-row mark has
    /// no such defence.</para>
    ///
    /// <para><b>The tilt is not decoration.</b> A row for a sheet that is out on the table shows
    /// its thumbnail knocked off-square because that is what the sheet itself is: laid down,
    /// unseated, at whatever angle the player dropped it. It reads before the colour does, which
    /// matters for the one player who cannot tell the gold tint from the cream plate.</para>
    ///
    /// <para><b>Why the row raises an event rather than acting.</b> Rows are a picture of the
    /// ledger and nothing more; they decide nothing. <see cref="Clicked"/>, <see cref="DragStarted"/>,
    /// <see cref="Dragging"/> and <see cref="DragEnded"/> all report — none of them moves paper.
    /// A row must never call the board directly: it does not know whether a drop landed on the
    /// board or on the cabinet (that is a screen-rectangle question, and the row does not own
    /// the rectangle), and a row that laid its own sheet down would have to be torn down and
    /// rebuilt by the very rebuild its own action triggered.</para>
    ///
    /// <para><b>The drag ghost (C7.5).</b> A drag from the drawer to the board carries a small,
    /// semi-transparent copy of the thumbnail under the pointer. It is <i>not</i> this row's
    /// thumbnail moved or reparented — the row must keep drawing, because the drag can be
    /// abandoned over the cabinet and a row with a hole in it for the duration is a worse
    /// picture of the drawer than a row with a copy floating over it. The ghost is parented to
    /// the <b>root canvas</b>, not to this row: anything under the cabinet's
    /// <c>RectMask2D</c> is clipped at the viewport edge, and a ghost that vanishes the instant
    /// it crosses onto the board is the one place it has to be visible. It carries a
    /// <see cref="CanvasGroup"/> with <c>blocksRaycasts</c> off, so the pointer keeps hitting
    /// what is under it — with the ghost eating raycasts the cabinet would report the pointer
    /// leaving it the moment a drag started, and every drop would read as "over the
    /// board".</para>
    ///
    /// <para><b>One row type serves sheets and groups both (G6.1).</b> A Groups row is the same
    /// object in the mockup's vocabulary — thumbnail, serif title, spaced sub-line, mark at the
    /// right, two states — so it is this class with <see cref="IsGroupRow"/> set rather than a
    /// second MonoBehaviour. A second class was the first plan and was dropped: it would have
    /// meant a second copy of the ghost, the tilt, the drag refusal and the two-state colouring,
    /// which are exactly the parts that must not drift apart, and Unity's one-behaviour-per-file
    /// convention would have forced a fourth file into a slice that owns three. What actually
    /// differs between the two is the <i>key</i> — a <see cref="SheetId"/> or a group id — and a
    /// key is a field, not a type.</para>
    ///
    /// <para><b>Events carry the row, not the key</b>, and the panel unpacks them. The first
    /// version raised <c>Action&lt;SheetId&gt;</c>, which cannot name a group; the obvious repair
    /// was four more group-keyed events beside the four sheet-keyed ones, and a row deciding
    /// which pair to raise. That is eight events on an object whose whole job is to report that
    /// it was touched. Handing the listener the row instead keeps the count at four and puts the
    /// one decision — "sheet event or group event?" — in <see cref="CabinetPanel"/>, which is
    /// already the conduit that re-raises them and already the only thing that constructs
    /// rows.</para>
    ///
    /// <para><b>A grouped sheet's row is inert (G6.2).</b> It keeps its place in its office
    /// section, so the office count still reads as the island's inventory, but it cannot be
    /// dragged and clicking it raises nothing: the only place an assembly can be picked up is
    /// its Groups row. The refusal is <i>marked</i>, not merely silent — see
    /// <see cref="BuildGroupMark"/> — and hovering it still lights the whole assembly
    /// (<see cref="HoverChanged"/>), so an inert row is visibly related to something rather than
    /// visibly dead.</para>
    /// </summary>
    public sealed class CabinetRow : MonoBehaviour,
                                     IPointerClickHandler,
                                     IPointerEnterHandler, IPointerExitHandler,
                                     IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        RectTransform thumbFrame;
        RawImage thumbImage;
        Image border;
        Image fill;
        Image kinMarker;
        Text nameText;
        Text codeText;
        GameObject tableMark;
        GameObject groupMark;
        Image[] groupMarkBars;

        RectTransform ghost;
        RawImage ghostRender;
        Canvas rootCanvas;
        bool dragging;

        bool onTable;

        /// <summary>The sheet this row stands for. Set once, at construction. Meaningless on a
        /// Groups row, in the sense <c>Placement.GroundX</c> is meaningless on a grouped
        /// placement — not stale, not approximate: there is no one sheet a group is. Test
        /// <see cref="IsGroupRow"/> first.</summary>
        public SheetId Id { get; private set; }

        /// <summary>
        /// The group this row is about, or 0.
        ///
        /// <para>It means two related things depending on <see cref="IsGroupRow"/>: on a Groups
        /// row it is the group the row <i>is</i>; on an office row it is the group its sheet
        /// <i>belongs to</i> (G6.2), 0 while the sheet is loose. Deliberately one field, because
        /// the one consumer that cares — the hover highlight of G6.3, which lights a Groups row
        /// and its members together — asks exactly the question "same group?" and would
        /// otherwise have to ask it two ways round.</para>
        /// </summary>
        public int GroupId { get; private set; }

        /// <summary>True for a row in the Groups section, false for a row in an office section.
        /// Set once, at construction: a row never changes which section it is in — the accordion
        /// is rebuilt instead.</summary>
        public bool IsGroupRow { get; private set; }

        /// <summary>True while the sheet, or the whole group, is out on the board rather than in
        /// the drawer.</summary>
        public bool OnTable { get { return onTable; } }

        /// <summary>True when this row reports nothing and carries nothing — G6.2's inert office
        /// row for a grouped sheet.</summary>
        public bool Inert { get { return !IsGroupRow && GroupId != 0; } }

        /// <summary>
        /// Raised when the row is clicked (C7.6 — a click selects). A handler must not assume
        /// the row will redraw itself in response; row state follows <see cref="BoardView"/>,
        /// never a click. A click that turned into a drag does not fire this: the event system
        /// clears <c>eligibleForClick</c> the frame a drag begins, which is exactly the
        /// behaviour wanted — dropping a sheet on the board must not also re-select it through
        /// a second path.
        ///
        /// <para>Not raised at all by an <see cref="Inert"/> row (G6.2).</para>
        /// </summary>
        public event Action<CabinetRow> Clicked;

        /// <summary>
        /// Raised once, when a drag off this row begins (C7.5). Not raised at all for a row
        /// whose sheet or group is already on the table, nor for an <see cref="Inert"/> one —
        /// see <see cref="OnTable"/>, <see cref="Inert"/> and the class comment.
        /// </summary>
        public event Action<CabinetRow> DragStarted;

        /// <summary>
        /// Raised every frame the pointer moves during a drag, carrying the pointer data so a
        /// listener can ask where on the screen it is. The row itself never asks: whether the
        /// pointer is over the board or over the cabinet is a question about rectangles the row
        /// does not own.
        /// </summary>
        public event Action<CabinetRow, PointerEventData> Dragging;

        /// <summary>
        /// Raised when the pointer is released to end a drag, again carrying the pointer data.
        /// This is the event that <i>can</i> lay a sheet or a group down, and the reason the
        /// decision is deferred to a listener: the drop point decides between "lay it on the
        /// board" and "it never left the drawer", and only <see cref="TableCanvas"/> knows the
        /// cabinet's rectangle.
        /// </summary>
        public event Action<CabinetRow, PointerEventData> DragEnded;

        /// <summary>
        /// The pointer entered (true) or left (false) this row. G6.3's cross-highlight is built
        /// on it: hovering a Groups row lights that group's rows in the office section above,
        /// and hovering one of those lights the Groups row and its siblings.
        ///
        /// <para>Raised by every row, grouped or loose, and the panel decides that a loose row
        /// lights nothing. The row does not filter because it cannot know: a row is told which
        /// group it belongs to, but "does anything else share it?" is a question about the whole
        /// accordion.</para>
        ///
        /// <para>This does not disturb <see cref="CabinetPanel.PointerOverChanged"/>. The event
        /// system sends enter and exit along the <i>difference</i> between the old and new
        /// hierarchies, so moving from one row to the next raises exit and enter on the two rows
        /// and nothing at all on their common ancestors — which is exactly what makes the
        /// panel's column-edge event trustworthy in the first place.</para>
        /// </summary>
        public event Action<CabinetRow, bool> HoverChanged;

        // --------------------------------------------------------------------

        /// <summary>
        /// Builds an office row under <paramref name="parent"/> and returns it.
        /// <paramref name="name"/> comes from <see cref="SheetNaming.NameFor"/> and
        /// <paramref name="code"/> from <see cref="SheetNaming.CodeFor"/>; neither is computed
        /// here, because a string invented on the UI side would not be a function of the seed
        /// (C7.7).
        /// </summary>
        public static CabinetRow Create(RectTransform parent, SheetId id, string name, string code)
        {
            var go = new GameObject("Row_" + code, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var row = go.AddComponent<CabinetRow>();
            row.Id = id;
            row.IsGroupRow = false;
            row.Build(name, code);
            return row;
        }

        /// <summary>
        /// Builds a Groups row (G6.1) under <paramref name="parent"/> and returns it.
        ///
        /// <para><paramref name="name"/> is the survey's name and year
        /// (<see cref="SheetNaming.SurveyLabelFor"/>) and <paramref name="code"/> is the lowest
        /// member's code with the member count after it
        /// (<see cref="SheetNaming.GroupCodeFor"/>) — the same two lines an office row draws,
        /// carrying the two facts G6.3 asks for. Both are composed by the caller for the same
        /// reason the sheet row's are: nothing about a name may be invented here.</para>
        /// </summary>
        public static CabinetRow CreateGroup(RectTransform parent, int groupId,
                                             string name, string code)
        {
            var go = new GameObject("GroupRow_" + groupId, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var row = go.AddComponent<CabinetRow>();
            row.IsGroupRow = true;
            row.GroupId = groupId;
            row.Build(name, code);
            return row;
        }

        void Build(string name, string code)
        {
            var rt = (RectTransform)transform;
            CabinetStyle.Stretch(rt);

            var element = gameObject.AddComponent<LayoutElement>();
            element.minHeight = CabinetStyle.RowHeight;
            element.preferredHeight = CabinetStyle.RowHeight;

            // Border and fill as two stacked images rather than an Outline effect: Outline
            // duplicates the graphic four times and offsets it, which frays a 1 px edge at
            // non-integer canvas scales. Two rects are exact at any scale.
            border = CabinetStyle.Plate(rt, "Border", CabinetStyle.RowBorder);
            border.raycastTarget = true;                       // the row's whole hit area

            fill = CabinetStyle.Plate(rt, "Fill", CabinetStyle.RowPlate);
            CabinetStyle.Inset(fill.rectTransform, CabinetStyle.HairlineWidth);

            BuildKinMarker(rt);

            // Thumbnail. Frame (border) → plate (blank paper) → the render itself. The plate is
            // what C5.6 asks for: a texture is still on a worker thread for the first frames of
            // an opening, and a row that waits for it is a row that pops in. A blank plate is
            // the honest picture of "issued, not yet drawn".
            var frameGo = new GameObject("Thumb", typeof(RectTransform));
            frameGo.transform.SetParent(rt, false);
            thumbFrame = (RectTransform)frameGo.transform;
            thumbFrame.anchorMin = thumbFrame.anchorMax = new Vector2(0f, 0.5f);
            thumbFrame.pivot = new Vector2(0f, 0.5f);
            thumbFrame.sizeDelta = new Vector2(CabinetStyle.ThumbWidth, CabinetStyle.ThumbHeight);
            thumbFrame.anchoredPosition = new Vector2(CabinetStyle.RowPadLeft, 0f);

            var frameImage = frameGo.AddComponent<Image>();
            frameImage.color = CabinetStyle.ThumbBorder;
            frameImage.raycastTarget = false;

            var plate = CabinetStyle.Plate(thumbFrame, "Plate", CabinetStyle.ThumbPlate);
            CabinetStyle.Inset(plate.rectTransform, CabinetStyle.HairlineWidth);

            var imageGo = new GameObject("Render", typeof(RectTransform));
            imageGo.transform.SetParent(thumbFrame, false);
            thumbImage = imageGo.AddComponent<RawImage>();
            thumbImage.raycastTarget = false;
            thumbImage.enabled = false;                        // nothing to draw yet
            CabinetStyle.Stretch(thumbImage.rectTransform);
            CabinetStyle.Inset(thumbImage.rectTransform, CabinetStyle.HairlineWidth);

            float textLeft = CabinetStyle.RowPadLeft + CabinetStyle.ThumbWidth + CabinetStyle.ThumbTextGap;

            nameText = CabinetStyle.Label(rt, "Name", name, CabinetStyle.Serif(),
                                          CabinetStyle.RowNameSize, CabinetStyle.Ink);
            CabinetStyle.LeftBlock(nameText.rectTransform, textLeft, CabinetStyle.RowNameOffsetY,
                                   CabinetStyle.RowNameHeight, CabinetStyle.RowPadRight);

            codeText = CabinetStyle.Label(rt, "Code", CabinetStyle.Spaced(code), CabinetStyle.Sans(),
                                          CabinetStyle.RowCodeSize, CabinetStyle.Muted);
            CabinetStyle.LeftBlock(codeText.rectTransform, textLeft, CabinetStyle.RowCodeOffsetY,
                                   CabinetStyle.RowCodeHeight, CabinetStyle.RowPadRight);

            tableMark = BuildTableMark(rt, CabinetStyle.Gold);
            PlaceMark(tableMark);

            // Built for every row, shown for none until SetGrouped says so. Building it lazily
            // was tried and is worse than it looks: a row learns it is grouped inside Refresh,
            // which runs on every BoardView.Changed — i.e. once per texture upload during an
            // opening (C5.6) — and building UI there would allocate on the frames the board is
            // already spending on uploads. Three quads at construction cost nothing.
            groupMark = BuildGroupMark(rt, CabinetStyle.Muted);
            PlaceMark(groupMark);
            groupMarkBars = groupMark.GetComponentsInChildren<Image>(true);

            SetOnTable(false);
            SetKin(false);
            ApplyMarks();
        }

        /// <summary>
        /// The kin bar of G6.3 — a short gold rule down the row's left edge, hidden until the
        /// pointer is on a row of the same group.
        ///
        /// <para><b>A bar rather than a tint.</b> The obvious highlight is a warmer fill, and it
        /// cannot be had: the fill already carries C7.4's two states, so a hover tint would need
        /// a second warm cream <i>per state</i> and would have to stay distinguishable from the
        /// gold tint it sits beside — two more colours off no mockup, competing with the one
        /// signal the mockup does define. A bar is additive. It is legible on both fills, it
        /// cannot be confused with either state because neither state has one, and it points
        /// along the run of rows it is grouping, which is the fact being shown.</para>
        ///
        /// <para>Inset by a hairline top, bottom and left so it sits <i>inside</i> the row's
        /// border rather than straddling it — the cabinet's own <c>EdgeRule</c> straddles,
        /// which is invisible at one pixel and would not be at three.</para>
        /// </summary>
        void BuildKinMarker(RectTransform parent)
        {
            var go = new GameObject("Kin", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            kinMarker = go.AddComponent<Image>();
            kinMarker.color = CabinetStyle.KinMarker;
            kinMarker.raycastTarget = false;

            var rt = kinMarker.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(CabinetStyle.KinMarkerWidth,
                                       -2f * CabinetStyle.HairlineWidth);
            rt.anchoredPosition = new Vector2(CabinetStyle.HairlineWidth, 0f);
        }

        static void PlaceMark(GameObject mark)
        {
            var rt = (RectTransform)mark.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-CabinetStyle.RowPadRight, 0f);
        }

        // --------------------------------------------------------------------

        /// <summary>
        /// Shows <paramref name="texture"/>, or a blank plate when it is null. <b>Null is normal,
        /// not an error</b> (C5.6): renders run on a worker thread and upload one texture per
        /// frame, so a cabinet opened on twenty sheets spends twenty frames filling in. Nothing
        /// here waits, retries or logs — the owner re-calls this on <c>BoardView.Changed</c>.
        /// </summary>
        public void SetThumbnail(Texture2D texture)
        {
            if (thumbImage == null) return;

            thumbImage.texture = texture;
            thumbImage.enabled = texture != null;

            // A drag can start on a row whose render has not landed yet — the table opens on
            // the mounting sheet and fills in over the following frames (C5.6), and a player
            // can be dragging inside that window. The ghost is a copy, so it has to be told.
            if (ghostRender != null)
            {
                ghostRender.texture = texture;
                ghostRender.enabled = texture != null;
            }
        }

        /// <summary>
        /// Switches between the two states of C7.4. Idempotent — the owner calls it on every
        /// refresh rather than tracking which rows changed, because a cabinet is at most a few
        /// dozen rows and a diff would be more code than the work it saves.
        ///
        /// <para>On a Groups row the same two states mean the same two things, one level up: a
        /// group is on the table or parked in the cabinet (G6.4), and <c>GroupRecord.OnTable</c>
        /// is the flag. That is not an analogy stretched to fit — parking is <i>where</i> a
        /// group is, not <i>what</i> it is, and its members are not on the board while it is
        /// parked, so C4.5's two states survive a group exactly as they survive a sheet.</para>
        /// </summary>
        public void SetOnTable(bool value)
        {
            onTable = value;

            if (border != null) border.color = value ? CabinetStyle.GoldBorder : CabinetStyle.RowBorder;
            if (fill != null) fill.color = value ? CabinetStyle.GoldTint : CabinetStyle.RowPlate;
            if (nameText != null) nameText.color = value ? CabinetStyle.Gold : CabinetStyle.Ink;

            if (thumbFrame != null)
            {
                float tilt = value ? CabinetStyle.OnTableTiltDegrees : 0f;
                thumbFrame.localRotation = Quaternion.Euler(0f, 0f, tilt);
            }

            ApplyMarks();
        }

        /// <summary>
        /// G6.2: which group this row's sheet belongs to, 0 while it is loose. Setting it
        /// non-zero marks the row and makes it <see cref="Inert"/> in one move, because the mark
        /// and the refusal are the same fact stated twice — a mark with a live drag behind it
        /// would be a lie, and a silent refusal would be a dead patch of cabinet.
        ///
        /// <para>Ignored on a Groups row, whose <see cref="GroupId"/> is set once at construction
        /// and identifies the row rather than describing it.</para>
        /// </summary>
        public void SetGrouped(int groupId)
        {
            if (IsGroupRow) return;

            GroupId = groupId;
            ApplyMarks();
        }

        /// <summary>Shows or hides the kin bar (G6.3). Idempotent, and called for every row on
        /// every hover change rather than only for the rows that changed: the accordion is a few
        /// dozen rows and a hover is a human gesture, so the diff would be more code than the
        /// work it saves — the same argument <see cref="SetOnTable"/> makes.</summary>
        public void SetKin(bool value)
        {
            if (kinMarker != null) kinMarker.enabled = value;
        }

        /// <summary>
        /// Which mark the right-hand slot shows, and in what colour.
        ///
        /// <para><b>One slot, never two marks.</b> A grouped sheet whose group is out on the
        /// table is <i>both</i> grouped and on the table, so the two marks compete for the same
        /// place. Drawing both — group mark, then table mark hard right — was the first
        /// arrangement and is rejected: it puts two icons on a row the mockup gives one, and it
        /// makes the group mark's position depend on the other mark's presence, so a mark slides
        /// sideways when a group is parked. The group mark wins the slot outright, and the fact
        /// it displaces is not lost: the gold tint, the gold title and the tilted thumbnail all
        /// still say "on the table", and C7.4 is explicit that tint and weight carry the state
        /// rather than the icon alone.</para>
        /// </summary>
        void ApplyMarks()
        {
            bool grouped = Inert;

            if (tableMark != null) tableMark.SetActive(!grouped && onTable);

            if (groupMark != null)
            {
                groupMark.SetActive(grouped);
                Tint(groupMarkBars, onTable ? CabinetStyle.Gold : CabinetStyle.Muted);
            }
        }

        static void Tint(Image[] images, Color colour)
        {
            if (images == null) return;
            for (int i = 0; i < images.Length; i++)
                if (images[i] != null) images[i].color = colour;
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            if (Inert) return;                                 // G6.2 — clicking lays nothing

            var handler = Clicked;
            if (handler != null) handler(this);
        }

        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
        {
            var handler = HoverChanged;
            if (handler != null) handler(this, true);
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            var handler = HoverChanged;
            if (handler != null) handler(this, false);
        }

        // ---- drag (C7.5, G6.5) ---------------------------------------------

        /// <summary>
        /// Begins a drag out of the drawer, unless the sheet or group is already on the table,
        /// or the row is inert.
        ///
        /// <para><b>The refusal has to be remembered, not re-derived.</b> The event system
        /// assigns this row as <c>pointerDrag</c> before it asks, so <see cref="IDragHandler"/>
        /// and <see cref="IEndDragHandler"/> arrive here whether or not the drag was accepted.
        /// <see cref="dragging"/> is what makes a refusal stick; testing <see cref="onTable"/>
        /// again in those two would be wrong as well as redundant, because a refresh can flip
        /// the flag underneath a live drag and half a drag is worse than none.</para>
        ///
        /// <para><b>Why a refused drag does not scroll the cabinet instead.</b> It could —
        /// forwarding the drag up to <c>CabinetPanel</c>, which owns the column's scrolling
        /// since G10.4, is three lines. It is not
        /// done because it would make the two states of C7.4 differ in a way the mockup never
        /// promises: an identical gesture would scroll on a gold row and carry paper on a plain
        /// one. The wheel scrolls over any row, and the section headers still drag-scroll, so
        /// nothing is unreachable.</para>
        /// </summary>
        void IBeginDragHandler.OnBeginDrag(PointerEventData eventData)
        {
            dragging = false;

            // C7.4 allows a row two states, and a drag is only meaningful from one of them: a
            // sheet already out on the board cannot be laid down a second time, and the archive
            // holds one of each — there is no copy to drag. The same is true of a group that is
            // already out (G6.5 retrieves a PARKED group, not a laid one).
            if (onTable) return;

            // G6.2 — the only place an assembly can be picked up is its Groups row.
            if (Inert) return;

            dragging = true;
            CreateGhost();
            MoveGhost(eventData);

            var handler = DragStarted;
            if (handler != null) handler(this);
        }

        void IDragHandler.OnDrag(PointerEventData eventData)
        {
            if (!dragging) return;

            MoveGhost(eventData);

            var handler = Dragging;
            if (handler != null) handler(this, eventData);
        }

        void IEndDragHandler.OnEndDrag(PointerEventData eventData)
        {
            // Destroyed before the event, not after: a listener lays a sheet on the board, which
            // raises Changed, which rebuilds the accordion and destroys this row. Anything left
            // until "after" would be left forever.
            DestroyGhost();

            if (!dragging) return;
            dragging = false;

            var handler = DragEnded;
            if (handler != null) handler(this, eventData);
        }

        /// <summary>
        /// A row is deactivated when its section is collapsed and destroyed when the accordion
        /// rebuilds — both of which can happen with a drag in flight, and neither of which sends
        /// <see cref="IEndDragHandler"/>. The ghost lives on the canvas root, so it would
        /// outlive its row: a sprite under the pointer that nothing owns and no gesture can
        /// remove. This is the only place that can catch that.
        ///
        /// <para>The hover state goes the same way and for the same reason: a row destroyed
        /// under the pointer never receives <see cref="IPointerExitHandler"/>, so a kin
        /// highlight lit by it would be left burning on rows the player is no longer near.</para>
        /// </summary>
        void OnDisable()
        {
            DestroyGhost();
            dragging = false;

            var handler = HoverChanged;
            if (handler != null) handler(this, false);
        }

        // --------------------------------------------------------------------

        /// <summary>
        /// Builds the ghost under the root canvas, last in the sibling order so it draws over
        /// the header and the cabinet both. Its geometry is the thumbnail's — same size, same
        /// frame, same blank plate under a render that may not have arrived — knocked off-square
        /// by <see cref="CabinetStyle.OnTableTiltDegrees"/>, which is not decoration either: it
        /// is the same tilt the row will wear once the sheet is down (C7.4), so the drag shows
        /// the state it is about to produce.
        /// </summary>
        void CreateGhost()
        {
            DestroyGhost();

            RectTransform parent = GhostParent();
            if (parent == null) return;

            var go = new GameObject("DragGhost", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            ghost = (RectTransform)go.transform;
            ghost.anchorMin = ghost.anchorMax = ghost.pivot = new Vector2(0.5f, 0.5f);
            ghost.sizeDelta = new Vector2(CabinetStyle.ThumbWidth, CabinetStyle.ThumbHeight);
            ghost.localRotation = Quaternion.Euler(0f, 0f, CabinetStyle.OnTableTiltDegrees);
            ghost.SetAsLastSibling();

            var frame = go.AddComponent<Image>();
            frame.color = CabinetStyle.ThumbBorder;
            frame.raycastTarget = false;

            var plate = CabinetStyle.Plate(ghost, "Plate", CabinetStyle.ThumbPlate);
            CabinetStyle.Inset(plate.rectTransform, CabinetStyle.HairlineWidth);

            var renderGo = new GameObject("Render", typeof(RectTransform));
            renderGo.transform.SetParent(ghost, false);
            ghostRender = renderGo.AddComponent<RawImage>();
            ghostRender.raycastTarget = false;
            ghostRender.texture = thumbImage != null ? thumbImage.texture : null;
            ghostRender.enabled = ghostRender.texture != null;
            CabinetStyle.Stretch(ghostRender.rectTransform);
            CabinetStyle.Inset(ghostRender.rectTransform, CabinetStyle.HairlineWidth);

            // Transparent as a whole rather than per-graphic: one alpha on a CanvasGroup cannot
            // drift out of step across frame, plate and render the way three tinted colours can.
            // blocksRaycasts off is the load-bearing half — see the class comment.
            var group = go.AddComponent<CanvasGroup>();
            group.alpha = CabinetStyle.GhostAlpha;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        void MoveGhost(PointerEventData eventData)
        {
            if (ghost == null) return;

            var parent = ghost.parent as RectTransform;
            if (parent == null) return;

            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, GhostCamera(), out local))
                ghost.anchoredPosition = local;
        }

        void DestroyGhost()
        {
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = null;
            ghostRender = null;
        }

        /// <summary>
        /// The root canvas's rect. <c>rootCanvas</c> rather than the nearest one: a nested
        /// canvas exists to isolate a subtree's rebuilds, and a ghost drawn inside one would be
        /// ordered against that subtree instead of against the screen.
        /// </summary>
        RectTransform GhostParent()
        {
            if (rootCanvas == null)
            {
                Canvas found = GetComponentInParent<Canvas>();
                if (found != null) rootCanvas = found.rootCanvas != null ? found.rootCanvas : found;
            }

            return rootCanvas != null ? (RectTransform)rootCanvas.transform : null;
        }

        /// <summary>Null for a screen-space-overlay canvas, which is what
        /// <see cref="TableCanvas"/> builds; asked of the canvas anyway so a render-mode change
        /// there does not silently strand every ghost in a corner.</summary>
        Camera GhostCamera()
        {
            if (rootCanvas == null) return null;
            return rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        }

        // --------------------------------------------------------------------

        /// <summary>
        /// The table mark of <c>2a-cabinet-states.png</c> — a trestle seen end-on — drawn from
        /// three rectangles rather than a glyph.
        ///
        /// <para><b>Why not a character.</b> The only font this project may use is the built-in
        /// one (no font assets, by instruction), and the glyph that would serve is not reliably
        /// in it. A missing glyph in a dynamic font renders as a hollow box, which reads as a
        /// *different* row state rather than as a missing character — exactly the failure C7.4's
        /// "icon, tint and weight carry it" cannot survive. Three quads are always three quads.
        /// Shared with the section header, which shows the same mark when a whole section is out
        /// on the table (C7.2).</para>
        /// </summary>
        public static GameObject BuildTableMark(RectTransform parent, Color colour)
        {
            var go = new GameObject("TableMark", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(CabinetStyle.MarkWidth, CabinetStyle.MarkHeight);

            Bar(rt, "Top", new Vector2(CabinetStyle.MarkWidth, CabinetStyle.MarkBarThickness),
                new Vector2(0f, CabinetStyle.MarkHeight * 0.5f - CabinetStyle.MarkBarThickness * 0.5f), colour);

            float legX = CabinetStyle.MarkWidth * 0.5f - CabinetStyle.MarkLegInset;
            float legY = -CabinetStyle.MarkBarThickness * 0.5f;

            Bar(rt, "LegLeft", new Vector2(CabinetStyle.MarkBarThickness, CabinetStyle.MarkLegHeight),
                new Vector2(-legX, legY), colour);
            Bar(rt, "LegRight", new Vector2(CabinetStyle.MarkBarThickness, CabinetStyle.MarkLegHeight),
                new Vector2(legX, legY), colour);

            return go;
        }

        /// <summary>
        /// The group mark of G6.2 — a bracket, drawn from three rectangles for exactly the
        /// reason <see cref="BuildTableMark"/> gives about the trestle. ⟨proposed: no mockup
        /// covers groups.⟩
        ///
        /// <para><b>Why a bracket.</b> It is the typographic sign that already means <i>these
        /// belong together</i>, so it needs no legend, and its meaning survives being seen once
        /// on a row and once beside a run of them. It is also unmistakably not the trestle: a
        /// spine with two arms against a table top with two legs, at a glance, in the same
        /// 18 × 14 box.</para>
        ///
        /// <para><b>Rejected: two overlapping sheet outlines</b>, which is the more literal
        /// picture of an assembly. Each outline is a coloured quad with a fill quad inset inside
        /// it (the row's own border-and-fill idiom), so the mark is four quads instead of three
        /// — and, fatally, the inner fills have to be the <i>row's</i> fill colour, which means
        /// the mark has to be retinted twice on every state change and would show as a solid
        /// blob against any surface that is not the plate it was built for. Rejected also: a
        /// glyph — 🔗, §, ⧉ — for the hollow-box reason above.</para>
        ///
        /// <para>Colour is a parameter and the caller changes it with state: gold while the
        /// group is out on the table, muted while it is parked. The mark says <i>grouped</i>;
        /// the tint says <i>where</i>.</para>
        /// </summary>
        public static GameObject BuildGroupMark(RectTransform parent, Color colour)
        {
            var go = new GameObject("GroupMark", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(CabinetStyle.GroupMarkWidth, CabinetStyle.GroupMarkHeight);

            float t = CabinetStyle.GroupMarkThickness;
            float spineX = -CabinetStyle.GroupMarkWidth * 0.5f + t * 0.5f;
            float armY = CabinetStyle.GroupMarkHeight * 0.5f - t * 0.5f;
            float armX = spineX + CabinetStyle.GroupMarkArmWidth * 0.5f;

            Bar(rt, "Spine", new Vector2(t, CabinetStyle.GroupMarkHeight),
                new Vector2(spineX, 0f), colour);
            Bar(rt, "ArmTop", new Vector2(CabinetStyle.GroupMarkArmWidth, t),
                new Vector2(armX, armY), colour);
            Bar(rt, "ArmBottom", new Vector2(CabinetStyle.GroupMarkArmWidth, t),
                new Vector2(armX, -armY), colour);

            return go;
        }

        static void Bar(RectTransform parent, string name, Vector2 size, Vector2 offset, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;

            var rt = image.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
        }
    }
}
