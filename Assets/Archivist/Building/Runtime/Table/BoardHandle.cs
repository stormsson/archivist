using UnityEngine;
using UnityEngine.UI;

namespace Archivist.Building.Table
{
    /// <summary>
    /// C8.10's first input: the knob from mockup <c>1c</c>, drawn at a screen point and hit
    /// tested at one.
    ///
    /// <para><b>It knows a pixel, not a corner.</b> Which corner the knob belongs at — a lone
    /// sheet's +X/+Z one, or G5.4's union corner for an assembly — is
    /// <c>BoardInteractor</c>'s answer, and so is what a grab on it then turns. This class
    /// places a disc and reports whether the pointer is on it.</para>
    ///
    /// <para><b>Its own overlay canvas, above <c>TableCanvas</c>.</b> The knob is chrome, so it
    /// cannot be a board slab — it would scale with the island and be unusable on a small sheet.
    /// No <c>CanvasScaler</c>: positions come from <c>WorldToScreenPoint</c> in raw pixels and a
    /// scaler would silently apply a second transform.</para>
    ///
    /// <para><b>It does not raycast</b> — <see cref="Hit"/> tests it in screen pixels. A UGUI
    /// target would be an <c>EventSystem</c> participant, delivering drag events on a different
    /// clock from the mouse polling the board does.</para>
    /// </summary>
    public sealed class BoardHandle
    {
        /// <summary>Radius of the knob in screen pixels, and of the disc that grabs it. The grab
        /// radius is deliberately the larger: a 14 px target is a 28 px object on a mockup
        /// rendered at 1440 wide, and the thing being aimed at is a corner that moves.</summary>
        const float RadiusPixels = 14f;
        const float GrabRadiusPixels = 22f;

        static readonly Color Body = new Color(0x2A / 255f, 0x1F / 255f, 0x16 / 255f);
        static readonly Color Ring = new Color(0xC9 / 255f, 0xA0 / 255f, 0x63 / 255f);

        /// <summary>Side of the knob's texture. 64 for a disc never drawn wider than 30 px:
        /// only ever minified, so bilinear does the antialiasing.</summary>
        const int DiscTexturePixels = 64;

        GameObject root;
        RectTransform rect;
        Texture2D discTexture;
        Sprite disc;
        Vector2 screenPoint;

        /// <summary>Shows the knob centred on a screen point, in the raw pixels
        /// <c>Camera.WorldToScreenPoint</c> returns.</summary>
        public void Place(Vector2 screen)
        {
            Ensure();

            screenPoint = screen;
            rect.anchoredPosition = screen;
            Show(true);
        }

        public void Hide()
        {
            Show(false);
        }

        /// <summary>C8.10's grab test, in screen pixels — see the class comment for why this is
        /// not a UGUI raycast. False while hidden, so a knob that is not on screen cannot be
        /// grabbed at the pixel it was last drawn at.</summary>
        public bool Hit(Vector2 screen)
        {
            if (root == null || !root.activeSelf) return false;

            return (screen - screenPoint).sqrMagnitude <= GrabRadiusPixels * GrabRadiusPixels;
        }

        /// <summary>Destroys the canvas, the sprite and its texture. Called from the driver's
        /// <c>OnDestroy</c>; both are <c>DontSave</c> and would otherwise outlive the domain they
        /// were made in.</summary>
        public void Dispose()
        {
            Discard(root);
            Discard(disc);
            Discard(discTexture);

            root = null;
            rect = null;
            disc = null;
            discTexture = null;
        }

        void Show(bool visible)
        {
            if (root != null && root.activeSelf != visible) root.SetActive(visible);
        }

        void Ensure()
        {
            if (root != null) return;

            root = new GameObject("BoardCornerHandle", typeof(Canvas));

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above the cabinet and the header: the knob belongs to a sheet on the board, and a
            // sheet dragged under the cabinet's edge must not have its handle clipped away by
            // chrome the player is not aiming at.
            canvas.sortingOrder = TableCanvas.SortingOrder + 10;

            // Drawn here rather than imported — see DiscTexture. One texture serves all three
            // rings: an Image tints its sprite, so the disc carries shape and the colour comes
            // from Body / Ring.
            discTexture = DiscTexture();
            disc = Sprite.Create(discTexture,
                                 new Rect(0f, 0f, DiscTexturePixels, DiscTexturePixels),
                                 new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect);
            disc.name = "BoardHandleDisc";
            disc.hideFlags = HideFlags.DontSave;

            // Anchored to the canvas's bottom-left corner, so anchoredPosition IS the screen
            // point WorldToScreenPoint returns — no scaler, no offset, no conversion.
            rect = Knob(root.transform, "Body", disc, Body, RadiusPixels * 2f, Vector2.zero);

            // The mockup's rotate mark as three circles rather than a glyph: a glyph needs a
            // font that has it, and the OS faces CabinetStyle borrows may not. Anchored to the
            // BODY'S CENTRE — these are concentric, and a (0,0) anchor would hang them off the
            // knob's bottom-left.
            Vector2 centre = new Vector2(0.5f, 0.5f);
            Knob(rect, "Ring", disc, Ring, RadiusPixels * 1.05f, centre);
            Knob(rect, "Core", disc, Body, RadiusPixels * 0.62f, centre);

            Show(false);
        }

        /// <summary>The knob's circle, drawn at load rather than imported. Unity's round UI
        /// sprite (<c>UI/Skin/Knob.psd</c>) lives in the editor's builtin-extra bundle, which
        /// only <c>AssetDatabase</c> reaches; the runtime lookup returns null and leaves a
        /// square knob. White, with the circle in the alpha, because the caller tints it — and
        /// with a one-texel feathered rim, so the edge does not stair-step at the largest
        /// size.</summary>
        static Texture2D DiscTexture()
        {
            const int Size = DiscTexturePixels;
            const float Radius = Size * 0.5f - 0.5f;
            const float Centre = (Size - 1) * 0.5f;

            var pixels = new Color32[Size * Size];

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = x - Centre;
                float dy = y - Centre;
                float coverage = Mathf.Clamp01(Radius - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * Size + x] =
                    new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage * 255f));
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32,
                                    mipChain: false, linear: false);
            tex.name = "BoardHandleDisc";
            tex.hideFlags = HideFlags.DontSave;      // see BoardSheetView
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
        }

        static RectTransform Knob(Transform parent, string name, Sprite sprite, Color colour,
                                  float diameter, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.raycastTarget = false;      // see the class comment

            var rt = image.rectTransform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(diameter, diameter);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>Destroy is illegal in edit mode, and the board rig is routinely built and
        /// torn down there by the bench.</summary>
        static void Discard(UnityEngine.Object thing)
        {
            if (thing == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(thing);
            else UnityEngine.Object.DestroyImmediate(thing);
        }
    }
}
