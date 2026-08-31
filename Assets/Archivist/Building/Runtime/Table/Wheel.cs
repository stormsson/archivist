using UnityEngine;

namespace Archivist.Building.Table
{
    /// <summary>
    /// One reading of the mouse wheel, in notches. The single place the table states what a
    /// wheel event means, so nothing a wheel drives here can disagree about the hardware.
    ///
    /// <para><b>The Input System does not normalise scroll, and that is the whole reason this
    /// file exists.</b> On Windows one detent is <c>WHEEL_DELTA</c>, 120. On macOS and Linux a
    /// detent is about 1. A trackpad on either is not detented at all: it reports a continuous
    /// stream of small values, several of which can land inside a single frame. Code that takes
    /// the raw number as a notch count is 120x too fast on one platform and a hundredth of the
    /// speed on another, which is what G10.3 was written about.</para>
    ///
    /// <para>So the reading is <b>bucketed</b>, not scaled: at or above half a detent it is
    /// treated as a Windows-style delta and divided; below, it is taken as a count of notches
    /// directly. It is a heuristic and it is written down as one. The alternative — a constant
    /// per platform — is a number that is wrong on the platform nobody is testing on, and the
    /// remaining error is absorbed by <see cref="TableOptions.WheelSensitivity"/>, which is a
    /// serialised field precisely so it can be dialled against the hardware in the room.</para>
    ///
    /// <para><b>Read the device, not the event.</b> Callers pass the raw
    /// <c>Mouse.scroll.ReadValue().y</c> even when they are inside a UGUI
    /// <c>IScrollHandler</c>. <c>PointerEventData.scrollDelta</c>
    /// has already been through <c>InputSystemUIInputModule</c>'s own normalisation, so taking
    /// it would mean two different units on one table and two sensitivities to keep in step.
    /// The event is used for <i>where the pointer is</i>; the device is used for <i>how far the
    /// wheel turned</i>.</para>
    /// </summary>
    public static class Wheel
    {
        /// <summary>One mouse wheel detent as Windows reports it: <c>WHEEL_DELTA</c>.</summary>
        public const float Delta = 120f;

        /// <summary>Half a detent — at or above this, a reading is a Windows-style delta.
        /// </summary>
        public const float DeltaThreshold = 60f;

        /// <summary>The raw reading in notches, before any sensitivity is applied.</summary>
        public static float Notches(float raw)
        {
            return Mathf.Abs(raw) >= DeltaThreshold ? raw / Delta : raw;
        }

        /// <summary>
        /// The raw reading in notches, scaled by the device dial and capped at
        /// <paramref name="maxPerFrame"/>.
        ///
        /// <para>The cap is not politeness. A trackpad flick, or a frame that swallowed several
        /// events, otherwise arrives as one enormous reading — which reads as the thing jumping
        /// rather than as the wheel turning, and on the board could cross the whole zoom range
        /// between two draws.</para>
        /// </summary>
        public static float Notches(float raw, float sensitivity, float maxPerFrame)
        {
            return Mathf.Clamp(Notches(raw) * sensitivity, -maxPerFrame, maxPerFrame);
        }
    }
}
