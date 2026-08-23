using System;
using Archivist.Building.Collection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Archivist.Building.Table
{
    /// <summary>
    /// One line of the cabinet: a thumbnail, the sheet's name, its code (C7.3). Built entirely
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
    /// </summary>
    public sealed class CabinetRow : MonoBehaviour,
                                     IPointerClickHandler,
                                     IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        RectTransform thumbFrame;
        RawImage thumbImage;
        Image border;
        Image fill;
        Text nameText;
        Text codeText;
        GameObject tableMark;

        RectTransform ghost;
        RawImage ghostRender;
        Canvas rootCanvas;
        bool dragging;

        bool onTable;

        /// <summary>The sheet this row stands for. Set once, at construction.</summary>
        public SheetId Id { get; private set; }

        /// <summary>True while the sheet is out on the board rather than in the drawer.</summary>
        public bool OnTable { get { return onTable; } }

        /// <summary>
        /// Raised when the row is clicked (C7.6 — a click selects). A handler must not assume
        /// the row will redraw itself in response; row state follows <see cref="BoardView"/>,
        /// never a click. A click that turned into a drag does not fire this: the event system
        /// clears <c>eligibleForClick</c> the frame a drag begins, which is exactly the
        /// behaviour wanted — dropping a sheet on the board must not also re-select it through
        /// a second path.
        /// </summary>
        public event Action<SheetId> Clicked;

        /// <summary>
        /// Raised once, when a drag off this row begins (C7.5). Not raised at all for a row
        /// whose sheet is already on the table — see <see cref="OnTable"/> and the class
        /// comment.
        /// </summary>
        public event Action<SheetId> DragStarted;

        /// <summary>
        /// Raised every frame the pointer moves during a drag, carrying the pointer data so a
        /// listener can ask where on the screen it is. The row itself never asks: whether the
        /// pointer is over the board or over the cabinet is a question about rectangles the row
        /// does not own.
        /// </summary>
        public event Action<SheetId, PointerEventData> Dragging;

        /// <summary>
        /// Raised when the pointer is released to end a drag, again carrying the pointer data.
        /// This is the event that <i>can</i> lay a sheet down, and the reason the decision is
        /// deferred to a listener: the drop point decides between "lay it on the board" and
        /// "it never left the drawer", and only <see cref="TableCanvas"/> knows the cabinet's
        /// rectangle.
        /// </summary>
        public event Action<SheetId, PointerEventData> DragEnded;

        // --------------------------------------------------------------------

        /// <summary>
        /// Builds the row under <paramref name="parent"/> and returns it. <paramref name="name"/>
        /// comes from <see cref="SheetNaming.NameFor"/> and <paramref name="code"/> from
        /// <see cref="SheetNaming.CodeFor"/>; neither is computed here, because a string invented
        /// on the UI side would not be a function of the seed (C7.7).
        /// </summary>
        public static CabinetRow Create(RectTransform parent, SheetId id, string name, string code)
        {
            var go = new GameObject("Row_" + code, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var row = go.AddComponent<CabinetRow>();
            row.Id = id;
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
            var markRt = (RectTransform)tableMark.transform;
            markRt.anchorMin = markRt.anchorMax = new Vector2(1f, 0.5f);
            markRt.pivot = new Vector2(1f, 0.5f);
            markRt.anchoredPosition = new Vector2(-CabinetStyle.RowPadRight, 0f);

            SetOnTable(false);
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
        /// </summary>
        public void SetOnTable(bool value)
        {
            onTable = value;

            if (border != null) border.color = value ? CabinetStyle.GoldBorder : CabinetStyle.RowBorder;
            if (fill != null) fill.color = value ? CabinetStyle.GoldTint : CabinetStyle.RowPlate;
            if (nameText != null) nameText.color = value ? CabinetStyle.Gold : CabinetStyle.Ink;
            if (tableMark != null) tableMark.SetActive(value);

            if (thumbFrame != null)
            {
                float tilt = value ? CabinetStyle.OnTableTiltDegrees : 0f;
                thumbFrame.localRotation = Quaternion.Euler(0f, 0f, tilt);
            }
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            var handler = Clicked;
            if (handler != null) handler(Id);
        }

        // ---- drag (C7.5) ---------------------------------------------------

        /// <summary>
        /// Begins a drag out of the drawer, unless the sheet is already on the table.
        ///
        /// <para><b>The refusal has to be remembered, not re-derived.</b> The event system
        /// assigns this row as <c>pointerDrag</c> before it asks, so <see cref="IDragHandler"/>
        /// and <see cref="IEndDragHandler"/> arrive here whether or not the drag was accepted.
        /// <see cref="dragging"/> is what makes a refusal stick; testing <see cref="onTable"/>
        /// again in those two would be wrong as well as redundant, because a refresh can flip
        /// the flag underneath a live drag and half a drag is worse than none.</para>
        ///
        /// <para><b>Why a refused drag does not scroll the cabinet instead.</b> It could —
        /// forwarding the drag up to the enclosing <c>ScrollRect</c> is three lines. It is not
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
            // holds one of each — there is no copy to drag.
            if (onTable) return;

            dragging = true;
            CreateGhost();
            MoveGhost(eventData);

            var handler = DragStarted;
            if (handler != null) handler(Id);
        }

        void IDragHandler.OnDrag(PointerEventData eventData)
        {
            if (!dragging) return;

            MoveGhost(eventData);

            var handler = Dragging;
            if (handler != null) handler(Id, eventData);
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
            if (handler != null) handler(Id, eventData);
        }

        /// <summary>
        /// A row is deactivated when its section is collapsed and destroyed when the accordion
        /// rebuilds — both of which can happen with a drag in flight, and neither of which sends
        /// <see cref="IEndDragHandler"/>. The ghost lives on the canvas root, so it would
        /// outlive its row: a sprite under the pointer that nothing owns and no gesture can
        /// remove. This is the only place that can catch that.
        /// </summary>
        void OnDisable()
        {
            DestroyGhost();
            dragging = false;
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
