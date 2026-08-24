using System;
using UnityEngine;

namespace Archivist.Building.Handling
{
    /// <summary>
    /// A carried thing being set down on a surface that has already chosen the pose for it.
    ///
    /// <para><b>Why this is not <see cref="ItemFall"/>.</b> The obvious move was to reuse the
    /// falling component with the sway turned down, and it does not work — not because of the
    /// sway but because of the path. A fall snaps the item to the destination's X and Z on its
    /// very first frame and then descends, because that is what "letting go" means: the item is
    /// already above where it will land, and the only remaining journey is downward. Over a
    /// floor that is exactly right. Onto a table anchor a metre in front of the eye it is
    /// wrong in a way that cannot be tuned out: the item disappears sideways onto the table's
    /// footprint in a single frame and then drops the last few centimetres, which reads as a
    /// teleport followed by a small unexplained hop, not as putting something down.</para>
    ///
    /// <para>Placing is a deliberate gesture, and a deliberate gesture travels along its whole
    /// path — through the air, from the hands to the surface, all three axes moving together.
    /// That is the take (<c>PlayerHands.Advance</c>) run backwards, so it uses the same
    /// smoothstep easing, and deliberately so: taking and placing are reverses of each other
    /// and should read as reverses of each other. Matching the curve is what makes them feel
    /// like one verb and its undo rather than two unrelated animations that happen to move an
    /// object.</para>
    ///
    /// <para><b>No sway, no tilt.</b> The wave and per-item phase in <see cref="ItemFall"/>
    /// exist because a dropped sheet tumbles — nothing is holding it, so it rocks and slides on
    /// the air. A placed item is guided the whole way down by a hand that knows where it is
    /// going. Adding a wobble here would not be more realistic, it would be a hand that cannot
    /// hold still. For the same reason there is no per-item phase and nothing deterministic to
    /// seed: two items placed at once should arrive alike, because the same hand placed
    /// them.</para>
    ///
    /// <para>The destination is given, not asked for. Unlike a fall, which asks the item where
    /// it comes to rest through <c>ICarryable.RestingPose</c>, a place is commanded by whatever
    /// is receiving the item — a table anchor, a shelf slot. The item has no say, which is why
    /// <c>Begin</c> takes a pose rather than an <see cref="HandlingOptions"/> to derive one
    /// from.</para>
    /// </summary>
    public sealed class ItemPlace : MonoBehaviour
    {
        ICarryable item;
        Action<ICarryable> onLanded;

        Vector3 fromPosition;
        Quaternion fromRotation;
        Vector3 toPosition;
        Quaternion toRotation;

        float duration;
        float elapsed;

        public void Begin(ICarryable carried, Vector3 to, Quaternion toRot, float seconds,
                          Action<ICarryable> landed)
        {
            item = carried;
            onLanded = landed;

            // Captured here rather than at the first Update: the journey starts where the item
            // was when the player let it go, and a frame's worth of anything else moving it
            // would otherwise bend the start of the path.
            fromPosition = transform.position;
            fromRotation = transform.rotation;

            toPosition = to;
            toRotation = toRot;

            duration = seconds;
            elapsed = 0f;
        }

        void Update()
        {
            if (item == null || item.Root == null)
            {
                Destroy(this);
                return;
            }

            elapsed += Time.deltaTime;

            // A zero or negative duration completes on the first tick rather than dividing by
            // it. Someone will eventually set the seconds to nought to see the end state.
            float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);   // smoothstep, as the take uses

            transform.position = Vector3.Lerp(fromPosition, toPosition, eased);
            transform.rotation = Quaternion.Slerp(fromRotation, toRotation, eased);

            if (t < 1f) return;

            // Exactly, not eased-to: the surface was given a pose and must get that pose, not
            // one a lerp came close to.
            transform.SetPositionAndRotation(toPosition, toRotation);

            if (onLanded != null) onLanded(item);
            Destroy(this);
        }
    }
}
