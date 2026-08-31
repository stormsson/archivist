using UnityEngine;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The rig of spec §5.1 — where a board sits, what it sits on, the camera that looks down at
    /// it, and the layer that keeps the room from seeing any of it.
    ///
    /// <para><b>One rig, two callers.</b> <see cref="BoardView"/> and the editor bench both need
    /// exactly this geometry, and a second copy of it drifts silently: a board that looks right
    /// in the bench and mirrored in the game is the hardest kind of wrong to see. What genuinely
    /// differs between the two — a camera's depth and whether it renders — is a parameter.</para>
    /// </summary>
    public static class BoardRig
    {
        /// <summary>C5.1. The main camera's culling mask must exclude this layer and a board
        /// camera's must contain only it.</summary>
        public const string TableLayerName = "Table";

        /// <summary>URP Unlit's albedo map. §3.4: unlit, so the board is independent of the
        /// room's lighting and of where its root sits — which is what makes C5.2's offset
        /// free.</summary>
        public const string MapTextureProperty = "_BaseMap";

        const string UnlitShader = "Universal Render Pipeline/Unlit";

        /// <summary>Where a board is built, in world space. C5.2 puts it well clear of the room
        /// so nothing on it can be seen, hit or lit from there.</summary>
        public static readonly Vector3 DefaultOrigin = new Vector3(0f, -500f, 0f);

        /// <summary>The Table layer, or -1 when the project has none — in which case the room's
        /// camera draws the board.</summary>
        public static int TableLayer { get { return LayerMask.NameToLayer(TableLayerName); } }

        /// <summary>
        /// A ground rotation as a board yaw.
        ///
        /// <para>Ground X maps to board X and ground Y to board Z, so a ground rotation that
        /// takes +X toward +Y is a Unity yaw that takes +X toward +Z — and Unity's positive yaw
        /// goes the other way. Hence the negation. Get this wrong and the board looks plausible
        /// but mirrored, which is the hardest kind of wrong to see; F-S1.2 verified the sign by
        /// outcome, so do not "fix" it.</para>
        /// </summary>
        public static Quaternion BoardRotation(double groundRotationDeg)
        {
            return Quaternion.Euler(0f, -(float)groundRotationDeg, 0f);
        }

        /// <summary>The pale surface the sheets sit on, and its material, which the caller owns.
        /// A quad, because the board has no thickness worth modelling and a plane would import a
        /// mesh nobody can tune. Its collider goes: C8.8 raycasts the Table layer for slabs, and
        /// a full-board collider would swallow every miss.</summary>
        public static Material BuildMountingSheet(Transform parent, BoardSpace space, int layer)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "MountingSheet";
            Discard(quad.GetComponent<Collider>());

            quad.transform.SetParent(parent, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3((float)space.BoardWidth, (float)space.BoardHeight, 1f);
            quad.transform.localPosition = new Vector3(0f, -0.01f, 0f);

            Material material = Unlit("M_MountingSheet");
            material.color = new Color(0.94f, 0.94f, 0.93f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;

            if (layer >= 0) quad.layer = layer;
            return material;
        }

        /// <summary>
        /// The board camera of §5.1: orthographic, looking down −Y, seeing nothing but
        /// <paramref name="layer"/>.
        ///
        /// <para><c>orthographicSize</c> is the caller's: a board holds its framing in a
        /// <see cref="BoardViewport"/> and a bench frames the whole mounting sheet once.</para>
        /// </summary>
        public static Camera BuildCamera(Transform parent, int layer, float depth, bool enabled)
        {
            var go = new GameObject("BoardCamera");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 50f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.13f, 0.10f);
            cam.depth = depth;
            cam.enabled = enabled;

            if (layer >= 0) cam.cullingMask = 1 << layer;
            return cam;
        }

        /// <summary>The slab template of §3.4. A fresh instance every call, owned by whoever
        /// asked for it.</summary>
        public static Material UnlitSlab()
        {
            return Unlit("M_BoardSheet");
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }

        static Material Unlit(string name)
        {
            var material = new Material(Shader.Find(UnlitShader));
            material.name = name;
            material.hideFlags = HideFlags.DontSave;
            return material;
        }

        /// <summary>Destroy is illegal in edit mode, and a rig is routinely built and torn down
        /// there — by a bench, by a rebuild, by deleting the root in the Hierarchy.</summary>
        static void Discard(UnityEngine.Object thing)
        {
            if (thing == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(thing);
            else UnityEngine.Object.DestroyImmediate(thing);
        }
    }
}
