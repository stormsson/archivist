using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Archivist.Building.Collection;
using Archivist.Building.Handling;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Binders
{
    /// <summary>
    /// A binder: the physical object a crate's sheets actually arrive in, and the thing the
    /// player carries around the archive. One island's sheets, in one folder, under one
    /// number.
    ///
    /// <para><b>The player's physical item is the folder, never the sheet</b> (§13, C4.1–C4.4,
    /// D-C1). Loose sheets are N objects to pick up one at a time, N things to lose under a rack
    /// and — the part that matters — N items to file individually once the racks exist. This is
    /// the folder: what gets carried, what gets shelved, and what a table adopts an island
    /// from.</para>
    ///
    /// <para><b>It holds identities, not sheets.</b> The contents are <see cref="SheetId"/>
    /// values — island, office, whole-island flag, number — and never geometry, never a
    /// texture, never a <c>SheetView</c>. That is the whole bargain of R1.1/R1.11: a sheet is a
    /// pure function of its island's seed, so storing anything else would be caching what can
    /// be recomputed. A binder of forty sheets costs forty small structs and no rendering at
    /// all; nothing is rasterised until something actually wants to look at a sheet.</para>
    ///
    /// <para><b>One island per binder</b>, enforced by <see cref="Add"/>. C4.2 has a table
    /// take its island from the first folder laid on it, which is only meaningful if a folder
    /// names exactly one island. A mixed binder would make the table's binding ambiguous at
    /// the moment it is established — and there would be no good answer.</para>
    ///
    /// <para><b>Contents are not serialised, and must not be.</b> The ledger — the only record
    /// that a sheet has been issued (R2.10) — starts empty on every load, so a binder surviving a
    /// scene load would hold sheets nothing remembers issuing, and a crate could issue them
    /// again. <see cref="BinderSpawner"/> sweeps binders at startup for the reason it sweeps
    /// sheets.</para>
    /// </summary>
    public sealed class BinderView : MonoBehaviour, ICarryable
    {
        /// <summary>The internal name is <c>Binder_1</c>, <c>Binder_2</c>, … — see
        /// <see cref="BinderSpawner"/>, which owns the counter.</summary>
        public const string NamePrefix = "Binder_";

        [Header("Identity (set by the spawner; shown here to be read, not edited)")]
        [SerializeField] int number;

        // Unity does not serialise ulong. The seed is kept as its bit pattern and read back
        // unchecked, which is exactly what IslandGenerator does with its collection seed.
        [SerializeField] long islandSeedBits;

        [Tooltip("A memo of a pure function of the seed, kept so a binder can be identified " +
                 "in the Hierarchy without regenerating an island to ask its name.")]
        [SerializeField] string islandName;

        [Header("Carried pose")]
        [Tooltip("How the binder is turned once it is in the hands, relative to the hold " +
                 "anchor. Where it sits is the anchor's job — move that transform to tune " +
                 "the position; this is only which way round it is held.")]
        [SerializeField] Vector3 carriedEuler = new Vector3(0f, 90f, 0f);

        readonly List<SheetId> contents = new List<SheetId>();

        Collider body;
        BinderSpawner floor;

        // ---- identity --------------------------------------------------------------------

        /// <summary>Its number in the archive: the <c>n</c> of <c>Binder_n</c>.</summary>
        public int Number { get { return number; } }

        /// <summary>The internal name. Also the GameObject's name, but this is the
        /// authority — a binder renamed in the Hierarchy is still <c>Binder_3</c>.</summary>
        public string BinderName { get { return NamePrefix + number; } }

        /// <summary>The one island this binder is about (R1.11: the only thing persisted).</summary>
        public ulong IslandSeed { get { return unchecked((ulong)islandSeedBits); } }

        /// <summary>The island's name, or empty if nobody has said. A memo, never a fact.</summary>
        public string IslandName { get { return islandName; } }

        // ---- contents --------------------------------------------------------------------

        /// <summary>How many sheets are in this binder.</summary>
        public int SheetCount { get { return contents.Count; } }

        /// <summary>Nothing has been filed here yet.</summary>
        public bool IsEmpty { get { return contents.Count == 0; } }

        /// <summary>What is in it, in the order it was filed. Read-only: a binder's contents
        /// change through <see cref="Add"/> and nowhere else, so that the one-island rule and
        /// the no-duplicates rule cannot be walked around.</summary>
        public IReadOnlyList<SheetId> Contents { get { return contents; } }

        /// <summary>How many sheets from one office are in here. What a spine label would
        /// show, and what the cartography table's accordion counts by (C1.3).</summary>
        public int CountFor(Office office)
        {
            int found = 0;
            for (int i = 0; i < contents.Count; i++)
                if (contents[i].Office == office) found++;
            return found;
        }

        public bool Contains(SheetId id) { return contents.Contains(id); }

        /// <summary>
        /// Files one sheet. False — and nothing changes — if it is already in here, or if it
        /// belongs to a different island.
        ///
        /// <para>Returning false rather than throwing because both refusals are things a
        /// player will do: putting a sheet back into the binder it came from, and trying to
        /// file a Driftcombe sheet into the Cold Harbour binder. Neither is a program error,
        /// and the caller decides what to say about it.</para>
        /// </summary>
        public bool Add(SheetId id)
        {
            if (id.IslandSeed != IslandSeed) return false;
            if (contents.Contains(id)) return false;

            contents.Add(id);
            return true;
        }

        /// <summary>Takes a sheet out. False if it was never in here.</summary>
        public bool Remove(SheetId id) { return contents.Remove(id); }

        /// <summary>
        /// Binds a fresh binder to its number and its island. Called once, by
        /// <see cref="BinderSpawner"/>; a binder does not choose its own number any more than
        /// an island chooses its own seed.
        /// </summary>
        public void Bind(int binderNumber, ulong islandSeed, string island)
        {
            number = binderNumber;
            islandSeedBits = unchecked((long)islandSeed);
            islandName = island ?? string.Empty;

            gameObject.name = BinderName;
        }

        /// <summary>One line for a log or a label: what it is called, what it is about, and
        /// how much is in it.</summary>
        public string Summary
        {
            get
            {
                string island = string.IsNullOrEmpty(islandName)
                    ? IslandSeed.ToString("X16")
                    : islandName;

                return $"{BinderName} — {island}, {SheetCount} sheet{(SheetCount == 1 ? "" : "s")}";
            }
        }

        /// <summary>The summary plus a breakdown by office. Longer than anything that belongs
        /// on screen; this is for the console and the test bench.</summary>
        public string Describe()
        {
            var text = new StringBuilder(Summary);

            // Offices.All, never a loop over ordinals: adding a fifth office must not
            // silently drop out of the breakdown (§4.1 forbids enum reflection for the same
            // reason).
            for (int i = 0; i < Offices.All.Length; i++)
            {
                Office office = Offices.All[i];
                int count = CountFor(office);
                if (count > 0) text.Append($"  {office}:{count}");
            }
            return text.ToString();
        }

        // ---- ICarryable ------------------------------------------------------------------

        public Transform Root { get { return transform; } }

        /// <summary>Resolved lazily — Awake never runs for objects made in edit mode, and a
        /// binder is routinely built by a bench there.</summary>
        public Collider Body
        {
            get
            {
                if (body == null) body = GetComponent<Collider>();
                if (body == null) body = GetComponentInChildren<Collider>();
                return body;
            }
        }

        public string CarryName { get { return BinderName; } }

        /// <summary>Its number, not a hash of its contents: a binder that has had a sheet
        /// added must not start falling differently.</summary>
        public int CarrySeed { get { return number * 397; } }

        /// <summary>
        /// A quarter turn by default: a binder lies flat on the floor and is held the other
        /// way round, the way you would actually carry a folder. A serialised field rather
        /// than a constant because which way round is right is judged by picking one up and
        /// looking at it — see the prefab, where the same argument put the verb in a field.
        /// </summary>
        public Quaternion CarriedRotation { get { return Quaternion.Euler(carriedEuler); } }

        public void RestingPose(Vector3 releasedAt, float yaw,
                                out Vector3 position, out Quaternion rotation)
        {
            BinderSpawner spawner = Spawner;
            if (spawner != null)
            {
                spawner.RestingPose(releasedAt, yaw, out position, out rotation);
                return;
            }

            position = releasedAt;
            rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public void Settled()
        {
            BinderSpawner spawner = Spawner;
            if (spawner != null) spawner.Register(this);
        }

        /// <summary>Found rather than remembered: a reference handed in at spawn time does not
        /// survive a domain reload, and comes back null with no symptom but a binder that
        /// lands in the floor.</summary>
        BinderSpawner Spawner
        {
            get
            {
                if (floor == null) floor = FindAnyObjectByType<BinderSpawner>();
                return floor;
            }
        }
    }
}
