# Racks at scale (K)

**Status: proposed, not built.** The archive has one rack, built the way a single
piece of furniture should be — a slot is a real object you can see, nudge and
delete. This document is about what happens when there are two hundred, why the
first design cannot go there, and what to build instead. Nothing here is
implemented; it is written now so that the switch is a decision rather than a
discovery.

Numbers are **K**, so they can be cited the way S- and R-numbers are.

---

## 1. Why the built design stops (K1)

- **K1.1** A slot is currently two GameObjects: the slot itself (carrying
  `ShelfSlot` and a `BoxCollider`) and an `Aim` highlight box. Multiplied out:

  | | 1 rack | 200 racks |
  |---|---|---|
  | GameObjects | 216 | **43,200** |
  | Renderers | 108 | 21,600 |
  | Colliders | 108 | 21,600 |

  It was three until the always-visible debug cube was cut, which halved the
  renderers and changed nothing about the conclusion.

- **K1.2** **The binding constraint is scene size and load time, not physics.**
  21,600 static box colliders are nothing to PhysX — static shapes sit in a
  pruning tree built once, and the game casts exactly one ray per frame. What
  does not survive is 64,800 objects of serialised YAML in a scene file. The
  second-largest cost is the 43,200 renderers, each of which is a culling entry
  whether or not it draws.

- **K1.3** **The trigger is about twenty racks** (~6,500 objects), where the
  scene file becomes unpleasant to open and to merge. Not two hundred: by then
  the migration is being done under duress.

- **K1.4** The switch should happen **before** hand-authored exceptions are
  scattered across many prefabs, because the design below cannot hold one — see
  K4.1.

---

## 2. The design (K2)

- **K2.1** **A rack is one GameObject.** Furniture mesh, one `Shelf`, one box
  collider across its face. Slots stop being objects and become arithmetic: the
  ray hits the face, `hit.point` converts to shelf-local, and `Shelf.AnchorLocal`
  run backwards gives `(row, column)` in two divisions.

  The face collider is also the wall the furniture owes the room anyway (S3.5).

- **K2.1a** **The face answers about empty space only, never about a binder.** A
  plane cannot answer a question about a volume: a binder sits behind the face,
  so an angled ray crosses the plane some columns to the side of the slot the
  binder is actually in, and the arithmetic returns a cell the player is not
  looking at. The per-slot boxes of the built design do not have this fault —
  they are 0.42 m deep, so an angled ray genuinely enters the right one — and it
  is introduced by flattening them.

  So **what is aimable is decided by what is in the hands**, and each state asks
  only the question it can answer:

  | hands | target | resolved by |
  |---|---|---|
  | empty | the shelved **binders** | their own colliders — correct from any angle, no arithmetic |
  | holding a binder | the **empty slots** | the face, `hit.point` → `(row, column)` |
  | holding a sheet | nothing | a sheet is filed into a binder, never onto a shelf (D-B2) |

  Refusal still works: with a binder in hand the face gives a cell, and a cell
  that is occupied refuses — the shelf knows its own occupancy without needing
  to have hit the binder.

- **K2.1b** **A shelved binder aimed at directly must not offer merge.** With
  empty hands the ray reaches the binder itself, which resolves to
  `BinderPickup`, whose floor behaviour is to merge same-island binders — and
  Q3.3 puts merging at the map table. It hands the verb over to the `Shelf`
  among its parents, exactly as it already does for `CartographyTable` through
  `speakingForTable`. An existing pattern, not a new one.

- **K2.1c** **The choice belongs to the rack, not to the interactor.** K2.1a is a
  reach toggle — the rack switches its face collider and its binders' colliders
  as the hands change — and not a hand-dependent layer mask inside
  `PlayerInteractor`. The interactor owns how far, which button and what is
  highlighted, and knows nothing about binders or shelves; `IInteractable` says
  so, and a mask keyed on what the player is carrying would put game content
  inside the one class deliberately kept free of it. Two toggles per rack on a
  hand change, against the 108 the per-slot design needs.

- **K2.1d** **A rack needs two colliders, because solidity and aimability are
  different jobs.**

  | | job | layer | toggled |
  |---|---|---|---|
  | body | keeps the player out of the furniture | Structure (S3.5) | never |
  | face | which empty slot is being pointed into | Rack | by hand state |

  Fusing them is what makes a toggled collider open a hole the player can walk
  through — the fault the per-slot design has today, where the slot boxes are
  the only solid thing on the bookcase.

- **K2.1e** *Rejected: two raycasts in priority order* — binders first, the face
  on a miss. It fails the case it looks like it handles: holding a binder and
  aiming into an empty slot with another binder further along the same line, the
  binder pass hits first and refuses a placement the player was plainly aiming
  at. Fixable with a distance comparison between the passes, which is two casts
  and an ordering rule where K2.1a is one cast and none.

- **K2.1f** *Rejected: marching the grid analytically.* Convert the ray to
  shelf-local space, walk the cells front to back with a DDA, and test each
  occupied cell's binder box. Exact, and needs no collider on a binder at all.
  It is also thirty lines that have to be right where a raycast simply is, and
  it duplicates a traversal physics already does well. Worth revisiting only if
  binders stop being real objects within reach.

- **K2.2** **The interaction contract has to carry the hit.**
  `PlayerInteractor.Probe` resolves a collider to an `Interactable` and discards
  the `RaycastHit`, which is the whole reason a slot is currently its own object
  with its own collider. `IInteractable` already assumes the other shape — *"a
  collider anywhere in an object's hierarchy resolves to the one component that
  owns the interaction, which is what lets a rack have thirty slot colliders and
  still be one interactable."* Passing the hit through is the enabling change,
  and it is small.

- **K2.3** **One highlight per rack**, moved to the hovered slot's computed pose.
  Only one slot is ever lit, so a box per slot was always 43,199 spare. This
  makes the code shorter, not longer.

- **K2.4** **Binders are data until the player is close.** R1.11 already says
  nothing geometric is persisted: a filed binder is an identity plus
  `(shelf, row, column)`. So a rack draws its filled slots with
  `Graphics.RenderMeshInstanced` — every binder in the building shares one mesh
  and one material, so 21,600 of them is roughly 22 draw calls — and a real
  `BinderView` is instantiated only within a few metres, demoted again when the
  player walks away. Interaction reach is 2.5 m, so the promoted set is tiny.

---

## 3. What is unaffected (K3)

- **K3.1** **`(row, column)` stays the save key.** It was chosen because a slot
  index shifts when `slotsPerRow` changes; under K2.1 it is also the only
  identity a slot has, since there is no object to point at.
- **K3.2** **The standing pose stays measured off the binder's own collider**,
  not off a constant — the arithmetic moves from `ShelfSlot` to `Shelf` and is
  otherwise unchanged.
- **K3.2a** **Per-slot reach toggling becomes per-rack.** It exists today because
  an empty slot's 0.42 m box intercepts a ray aimed past it at a binder. The
  rule survives K2.1a unchanged — what the player can act on is what the ray can
  reach — but a rack has two colliders to switch instead of 108, and the switch
  answers the same question about the hands.
- **K3.3** **The verbs and their rules are untouched**: file, take, no merging at
  a rack (Q3.3), and any binder into any slot with no correctness readout
  (R4.5, R4.9). All of it sits above the storage layer.

---

## 4. What it costs (K4)

- **K4.1** **Per-slot exceptions are gone.** A slot that is arithmetic cannot be
  nudged, deleted, or made special; only the rule can change. This is the one
  real loss, and it is a genuine trade rather than a free win — though at two
  hundred racks an exception on rack 147 that someone has to remember is a
  liability of its own.
- **K4.2** **Spine labels at distance need an atlas** and per-instance UVs, or
  they are blank until a binder is promoted. Blank is acceptable and honest: a
  spine is not readable across a room.
- **K4.3** **Promotion and demotion are a new source of bugs** — an object that
  exists only sometimes is harder to reason about than one that always does. The
  boundary wants to be well outside interaction reach so that nothing is ever
  promoted in the frame it is acted on.

---

## 5. The missing input (K5)

- **K5.1** **200 racks is an assumption, not a requirement.** S3.2 makes room
  size a function of archive capacity, and R4.1's capacity is exactly what §9 of
  `requirements.md` calls the missing input. Two hundred racks is 21,600 binders
  and something like half a million sheets.

  Twenty racks and two hundred racks want different architectures, and only one
  of them needs any of this. **Settle R4.1 before building it.**
