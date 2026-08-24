using System;
using UnityEngine;

namespace Archivist.Building.Handling
{
    /// <summary>
    /// A carried thing falling out of the hands and settling on the floor.
    ///
    /// <para><b>Scripted, not a Rigidbody.</b> Paper does not fall the way a rigid body falls
    /// — it reaches a slow terminal speed almost immediately and then rocks and slides on the
    /// way down, which is behaviour a solver gives you only with a soft body and a great deal
    /// of luck. It is also behaviour that must not be able to go wrong: R5.6 says nothing
    /// collides badly and nothing gets stuck, and a loose rigid body on a floor covered in
    /// other paper is exactly how a sheet ends up wedged under a rack forever. Here the
    /// resting place is decided at release, and the fall is how it gets there.</para>
    ///
    /// <para>One oscillation drives both the tip and the slide, because that is what couples
    /// them in real paper: it slides in the direction it is tipping. The swing fades to
    /// nothing as the sheet nears the floor, so it arrives flat and settled rather than
    /// snapping straight at the last moment.</para>
    ///
    /// <para><b>Not paper-specific</b>, despite the tuning: a binder falls by the same rules and
    /// for the same reason. The sway and tilt are tuned for paper, and a heavier item that wants
    /// to drop like a book turns them down through <see cref="HandlingOptions"/> rather than
    /// growing a second falling component here.</para>
    /// </summary>
    public sealed class ItemFall : MonoBehaviour
    {
        ICarryable item;
        Action<ICarryable> onLanded;

        Vector3 restPosition;
        Quaternion restRotation;
        Quaternion startRotation;
        float startY;

        Vector3 rockAxis;    // horizontal; the item tips about this
        Vector3 driftAxis;   // horizontal, perpendicular; the item slides along this

        float terminalSpeed;
        float swayMetres;
        float swayHz;
        float tiltDegrees;
        float phase;

        float velocity;
        float elapsed;

        public void Begin(ICarryable carried, Vector3 rest, Quaternion restRot,
                          HandlingOptions options, Vector3 horizontalAxis,
                          Action<ICarryable> landed)
        {
            item = carried;
            onLanded = landed;

            restPosition = rest;
            restRotation = restRot;
            startRotation = transform.rotation;
            startY = transform.position.y;

            rockAxis = Vector3.ProjectOnPlane(horizontalAxis, Vector3.up);
            if (rockAxis.sqrMagnitude < 0.0001f) rockAxis = Vector3.right;
            rockAxis.Normalize();
            driftAxis = Vector3.Cross(Vector3.up, rockAxis).normalized;

            terminalSpeed = options != null ? options.FallSpeed : 1.1f;
            swayMetres = options != null ? options.FallSwayMetres : 0.08f;
            swayHz = options != null ? options.FallSwayHz : 1.2f;
            tiltDegrees = options != null ? options.FallTiltDegrees : 16f;

            // Deterministic per item, so two dropped together do not swing in step — and so
            // the same item always falls the same way, which keeps a report reproducible.
            phase = Mathf.Abs(carried.CarrySeed % 1000) / 1000f;
        }

        void Update()
        {
            if (item == null || item.Root == null)
            {
                Destroy(this);
                return;
            }

            elapsed += Time.deltaTime;

            // Gravity until terminal speed, then nothing more. Air resistance, to the eye.
            velocity = Mathf.Min(velocity + 9.81f * Time.deltaTime, terminalSpeed);

            float y = transform.position.y - velocity * Time.deltaTime;
            bool landed = y <= restPosition.y;
            if (landed) y = restPosition.y;

            float drop = startY - restPosition.y;
            float progress = drop <= 0.0001f ? 1f : Mathf.Clamp01((startY - y) / drop);

            // The swing fades out as the floor approaches, so the item settles instead of
            // being cut off mid-swing.
            float fade = 1f - progress;
            float wave = Mathf.Sin((elapsed * swayHz + phase) * Mathf.PI * 2f);

            Vector3 offset = driftAxis * (wave * swayMetres * fade);
            transform.position = new Vector3(restPosition.x + offset.x, y, restPosition.z + offset.z);

            Quaternion settling = Quaternion.Slerp(startRotation, restRotation, progress);
            transform.rotation = Quaternion.AngleAxis(wave * tiltDegrees * fade, rockAxis) * settling;

            if (!landed) return;

            transform.SetPositionAndRotation(restPosition, restRotation);

            if (onLanded != null) onLanded(item);
            Destroy(this);
        }
    }
}
