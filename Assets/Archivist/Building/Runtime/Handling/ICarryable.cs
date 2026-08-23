using UnityEngine;

namespace Archivist.Building.Handling
{
    /// <summary>
    /// Something the player can pick up, carry, and put down. A sheet is one; a binder is
    /// another; a crate of them will be a third.
    ///
    /// <para><b>Why an interface rather than a base class.</b> The two things that are carried
    /// today have nothing else in common. <c>SheetView</c> owns a mesh, a material and a
    /// texture it built at runtime and must destroy; <c>BinderView</c> owns an imported model
    /// and a list of identities. Making them share an ancestor would mean inventing a common
    /// parent for two objects whose only shared fact is that hands can hold them — which is
    /// exactly what an interface says and a base class does not.</para>
    ///
    /// <para><b>Why the resting pose is asked of the item.</b> <c>PlayerHands</c> used to hold
    /// a <c>SheetSpawner</c> reference and ask it where a released sheet lands. That works
    /// while there is one kind of carried thing and becomes a type switch the moment there are
    /// two — the hands would have to know that sheets go to the sheet spawner and binders to
    /// the binder spawner, which is knowledge about paper living in the component that models
    /// a pair of hands. Here the item answers for itself, and the hands stay a pair of hands:
    /// they take, they carry, they let go.</para>
    ///
    /// <para><b>The pose is decided at release, not on arrival</b> — see <c>ItemFall</c>. An
    /// implementation must answer <see cref="RestingPose"/> without having moved anything.</para>
    /// </summary>
    public interface ICarryable
    {
        /// <summary>The transform the hands move. Not named <c>transform</c>: this is the
        /// object's <i>carried</i> root, which for a compound item need not be its
        /// <c>MonoBehaviour</c>'s own transform.</summary>
        Transform Root { get; }

        /// <summary>What the player aims at. Switched off while carried, so an item held in
        /// front of the eye does not swallow every interaction ray cast past it.</summary>
        Collider Body { get; }

        /// <summary>What this item is called in a log. Not shown to the player.</summary>
        string CarryName { get; }

        /// <summary>A stable per-item number that drives deterministic scatter and the phase
        /// of the fall, so two items dropped together do not swing in step — and so the same
        /// item always falls the same way, which is what keeps a report reproducible.</summary>
        int CarrySeed { get; }

        /// <summary>
        /// How this item is turned while carried, in the hold anchor's local space. The
        /// anchor decides <i>where</i> a carried thing sits — move it in the scene view and
        /// everything in the hands moves — and this decides how <i>this</i> thing is turned
        /// once it is there.
        ///
        /// <para>Per item because the right answer differs by item and cannot be one pose on
        /// the anchor. A sheet is read face-on and wants no turn at all; a binder is held the
        /// way you would actually hold a folder, which is not how it lies on a floor. Identity
        /// means "exactly as the anchor is turned", which is what a sheet returns.</para>
        /// </summary>
        Quaternion CarriedRotation { get; }

        /// <summary>
        /// Where this item comes to rest if it is let go above <paramref name="releasedAt"/>
        /// while the player faces <paramref name="yaw"/>. Called once, at release, before the
        /// item has moved at all.
        /// </summary>
        void RestingPose(Vector3 releasedAt, float yaw, out Vector3 position, out Quaternion rotation);

        /// <summary>Called once the item has actually arrived. Where an item tells whatever
        /// keeps track of the floor that it is part of it again.</summary>
        void Settled();
    }
}
