interesting seed: 905386350
---

## Interaction UI — state smuggled through `Label` (noted 2026-08-23)

The prompt is well isolated: `UnityEngine.UI` appears in exactly one runtime
file, and the whole coupling is `Show(verb, bindingHint, canInteract)` /
`Hide()`. Replacing it later is cheap. **Except for one thing.**

`CanInteract` returns a bare bool, so an interactable that wants to say *why* it
is refusing has nowhere to put that — and encodes it in the label string
instead. `MapCrate:50` already does: `busy ? busyLabel : base.Label`. To the UI,
"crate is working" and "hands are full" are the same state: dim the text.

Fine at one class. The cost grows with every interactable, because each invents
its own encoding — so a nicer UI wanting a spinner, an icon, or a reason has to
unpick N classes rather than one. Seams that stay cheap forever (unsealing
`InteractionPrompt`, rewriting `RoomBuilder.BuildInteractionUi`) are one file
each; this one is not.

**Act when the second interactable needs to refuse for a reason** — racks
(full), the map table (occupied) — and not before. Widen the contract then:
`CanInteract` returns a reason, not a bool, and `Label` goes back to naming the
verb only. Doing it before the pattern spreads is one class; after is all of them.





create a SheetFolder prefab
  ASSET: /Users/camillo.camarda/Sites/prototypes/games/archivist/Archivist/Assets/Models/Placeholders/classic_paper_envelope.glb

  concept:
  - it contains the reference to an island
  - it contains sheets
  - more sheets can be added during the game

  - it will have its own interaction later on
  - it can be held in hand, like the current way of holding a sheet
  ---

  I want to create a new Interactable prefab, the cartography table.
  1 - the visual is /Users/camillo.camarda/Sites/prototypes/games/archivist/Archivist/Assets/Models/Placeholders/wooden_table.glb
  2 - the action is "Open Cartography table"
  3 - opening it creates a new view (is it a new scene?)

  we want to create a view that allows the user to have a big area where to move , place , rotate multiple map sheets
  on the right side we willl have a column that act as a menu with different accordions  (note : they are not options menu they will be in-game
  visuals like colored and so on)
  each accordion item will be an island that "we have available"
---

## Ledger — the count that is missing on purpose (noted 2026-08-23)

`SheetLedger` now answers the questions a collection screen asks: which islands
the archive has met (draw order), which sheets of one are out (issue order), and
how much of an island is still in the crates — `IslandHolding.IssuedPercent`.

**Filed-correctly is not there.** It is the number the game is actually about,
and it needs the racks first: "correct" is a shelving rule, and there is nowhere
to be correct yet. When racks exist it goes in as a second set of `SheetId` per
island in `SheetLedgerStore` — the same shape as issuance, so `IslandHolding`
grows `Filed` / `FiledPercent` beside `Issued` and nothing else moves.

Deliberately not stubbed at zero: a field that always reads zero is
indistinguishable from a real answer, and a UI would end up drawing "0% filed"
under a shelf nobody has built.

**The two memos.** `Describe(island)` hands the ledger a name and a sheet count
that are pure functions of the seed — recomputable, never persisted, never
authoritative. They exist only so listing thirty islands does not cost thirty
generations (~340 ms each). `TotalKnown` false means nobody has counted yet;
percentages report -1 rather than 0, so "nothing issued" and "never counted"
stay different states.

---

## `Office.Antiquarian` is a placeholder name (noted 2026-08-23)

The project owner intends to rename it. Recording what makes that safe, and what
would make it unsafe, because the answer is not obvious from `Office`'s own
"append only" warning.

**Renaming the member is safe. Changing its value is not.** The warning on
`Office` is about *ordinals*: several streams index by `(int)office` —
`Streams.For(seed, StreamNames.Year, (int)office)` among them — and Unity
serialises enum fields by value, not by name. So `Antiquarian = 3` may become
anything else `= 3` without moving a single island or breaking a single scene.
Renumbering it would rewrite every island in the collection.

**Two call sites must stay switch-on-member, never string.**
`SheetNaming.PrefixFor` and anything else that maps an office to display text
must switch on the enum member, so the rename is a compile-safe refactor the
compiler walks you through. A string lookup or an `Enum.Parse` would turn it into
a silent runtime failure — and §4.1 already forbids enum reflection.

The two-letter code `AQ` is invented and carries no weight; nothing is keyed by
it. It changes with the name.

---

## The coastline has no name, and that is a real gap (noted 2026-08-23)

Found while specifying the cartography table's cabinet. The generator names
**settlements** (all of them) and **peaks** (only the top `Tuning.PeakNamedCount`
= 3). `River` has no name field. `Poi` is unnamed by design — POC-03 §5 keeps
text and labels out of scope. **The coastline is a bare polyline with no naming
of any kind.**

So the archive has no way to say *Cape Vela*, *Gull Spit*, *Cold Harbour* or
*Long Reef* — and an island whose entire premise is that three offices survey the
same shore cannot name a single feature of that shore. Every Hydrographic sheet
is a document about ground the archive cannot talk about.

**Worked around, not fixed.** `Naming/SheetNames.cs` names the *sheet* from the
ground it covers (C7.7), which gives the cabinet real names without inventing
feature names. That is a different thing from naming the coast, and it does not
close this gap — it routes around it. Coastal features getting their own names
remains open, and would be a new `StreamNames` entry plus a siting pass, in the
shape `Settlements` already uses.

Worth doing when something other than a sheet label needs to refer to a place on
the shore — R7.2's references ("a few named places"), or R7.6's marking. Not
before: names nothing reads are names nobody can check.

---

## The crate delivers a binder, not a pile (noted 2026-08-23)

`MapCrate` no longer tips five loose sheets onto the floor. One opening now
produces one `BinderView` — `Binder_1`, `Binder_2`, … — holding the `SheetId`s of
the sheets it drew, plus **one loose sheet** while `looseDebugSheet` is on.

**The model was already decided.** The cartography table's spec settled it
(§13, D-C1): *the player's physical item is the folder, never the sheet.* Loose
paper is N things to pick up one at a time and, once racks exist, N things to
file individually — for a game whose unit of work is meant to be a document. The
binder is what gets carried, what gets shelved, and what a table takes its island
from (C4.2).

**A binder holds identities.** `SheetId` values, never geometry, never a texture,
never a `SheetView`. So an opening now renders **one** sheet where it used to
render five, and forty filed sheets cost forty small structs. Nothing is
rasterised until something wants to look at a sheet — which is the whole bargain
of R1.1/R1.11, applied to storage instead of to a sheet lying on the floor.

**The loose sheet is a debug affordance and is labelled as one.** Nothing can yet
take a sheet *out* of a binder, so without one there would be nothing to test
"file this into that" against when that verb is built. It is a real issued sheet
of the same island, deliberately *not* one of the binder's — it could not be
filed into it otherwise. Turn the flag off and a delivery is a binder alone,
which is what the crate should eventually do.

**One island per binder**, enforced in `BinderView.Add`. C4.2 has a table adopt
its island from the first folder laid on it, which only means something if a
folder names exactly one island. A mixed binder would make the binding ambiguous
at the moment it is established, with no good answer available.

**Binders are swept like sheets**, at scene start and before a scene is saved.
They hold sheets the ledger recorded as issued, and the ledger starts empty on
every load — a surviving binder would claim sheets nothing remembers issuing
(R2.10). Their contents are not serialised anyway, so what came back would be an
empty folder holding a number.

---

## `ICarryable` — the hands stopped knowing about paper (noted 2026-08-23)

`PlayerHands` held a `SheetView` and a `SheetSpawner`, and asked the spawner where
a released sheet lands. With a binder as a second carried thing that becomes a
type switch inside the component that models a pair of hands: sheets go to the
sheet spawner, binders to the binder spawner.

So the question moved to the item. `ICarryable` is a root, a collider, a name, a
scatter seed, `RestingPose(releasedAt, yaw)` and `Settled()`. `SheetView`
delegates to `SheetSpawner`, `BinderView` to `BinderSpawner`, and the hands take,
carry and let go.

**An interface, not a base class.** The two things carried today share nothing
else: one owns a mesh, a material and a texture it built and must destroy; the
other owns an imported model and a list of identities. A common ancestor would be
inventing a parent for two objects whose only shared fact is that hands can hold
them.

**Each spawner is found, never stored.** A reference handed in at spawn time does
not survive a domain reload and comes back null with no symptom but a thing that
lands in the wrong plane — the lesson already written on `SheetPickup`.

`SheetFall` became `ItemFall` in the same pass. A binder falls by the same rules
and for the same reason: the resting place is decided at release (R5.6), and the
fall is only how it gets there.

**A real bug found on the way.** `RestingPose`'s downward probe — the one that
stacks a dropped thing on what is already lying there — was querying stale
collider positions. `Physics.autoSyncTransforms` is off by default, so a collider
positioned this frame is still queried where it used to be; in play mode the next
`FixedUpdate` hides it, and in edit mode there is no next `FixedUpdate` and the
probe finds *nothing*, which reads as "the floor is clear" and puts one thing
straight through another. Both spawners now call `Physics.SyncTransforms()` first.

---

## Item prefabs are authored, not generated (decided by the project owner, 2026-08-23)

`PF_Binder.prefab` was first produced by a `BinderPrefabBuilder` editor script —
load the `.glb`, measure it, scale it to a target size, lay it flat, fit a
collider, save. The project owner rejected that: **item prefabs are ordinary
assets in the Project window, opened and tuned in the Inspector.** The builder
has been deleted; `PF_Binder.prefab` and `PF_CartographyTable.prefab` are now
plain hand-editable assets and nothing regenerates them.

**This scopes the "scripts build geometry" rule in `CLAUDE.md`, it does not
repeal it.** That rule exists because the *room's* dimensions come from
`space/requirements.md` — they are spec-derived, cited by S-number, and have to
be cheap to rebuild when a number in the spec moves. `RoomBuilder` still owns the
room for exactly that reason.

An item prefab is the opposite case. How big a binder is, which way up it lies,
how thick its collider is and what its verb says are judged by *looking at one* —
there is no spec line to derive them from, and a generator that re-derives them
overwrites the judgement on its next run. The cost of hand-authoring is that the
numbers are no longer reproducible from source; that is the correct trade for
values whose only authority is somebody's eye.

**Consequence worth knowing:** the placeholder envelope imports at 1.894 × 0.172
× 1.817 m, so the prefab carries a scale of ~0.18 on its `Visual` child to reach
0.34 × 0.031 × 0.326 m. If the model is ever replaced, that scale and the
`BoxCollider` are hand-corrected in the prefab, not recomputed.

`BinderPickup` follows from this too: its verb is the serialised
`Interactable.label` with a `Reset()` default, not a hard-coded `Label` override —
the same pattern `CartographyTable` uses. `SheetPickup` keeps its constant,
because no sheet is ever authored in the editor to type a verb into.
