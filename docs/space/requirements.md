# The Archive Space — Requirements

Working document for the **3D half** of the project: the building the player
walks in, and everything physical in it. Companion to `../requirements.md` (the
game) and `../generation_for_agents.md` (the data).

Authority order: `../requirements.md` (game intent) → this file (space intent) →
`decisions.md` / `findings.md` → `space_for_agents.md` (as built, once it exists).

**Nothing in this document touches generation.** `Archivist.Generation` must
never reference `UnityEngine` (§14 of `generation_for_agents.md`) and nothing
here changes that. The two halves meet at anchors and at textures, later.

---

## 0. What this document is for

Every 3D asset the project will ever buy, commission, or generate is priced in
the numbers below. The numbers come first because they are the only part only a
human can decide, and the only part that is expensive to change afterwards.

Status markers: **approved** — settled, build against it. **provisional** —
usable now, expected to move. **deferred** — deliberately not decided, listed in
§9.

---

## 1. Metric standard (S1) — approved

- **S1.1** 1 Unity unit = 1 metre. Every imported asset is checked against this,
  and an asset that needs a scale factor other than 1.0 is a defect, not a
  preference.
- **S1.2** Blockout grid: **0.25 m**. Nothing structural sits off-grid.
- **S1.3** Walls are **solid boxes, 0.2 m thick**, never planes. A plane is
  one-sided: it breaks collision from behind and lights wrong. This is cheap now
  and unfixable later once furniture is aligned to wall faces.
- **S1.4** +Y up, +Z forward. Prefab pivots sit at the **contact point** — where
  the object meets the floor — not at the bounding-box centre. Prefab root scale
  is always `(1,1,1)`.

## 2. The player (S2) — approved

- **S2.1** Capsule height **1.8 m**, radius **0.3 m**, eye height **1.65 m**.
- **S2.2** Walk speed **1.8 m/s**. When carrying exists (R5.2), carried speed is
  expressed as a **fraction of walk speed**, never as a second constant. The
  fraction is deferred until there is something to carry.
- **S2.3** Camera vertical FOV **60°**. This is a readability parameter, not a
  taste one — it sets how much of a rack face fills the screen at aisle
  distance, and §4.1 of `../requirements.md` depends on that.
- **S2.4** **No crouch.** Cut, not deferred. Nothing is stored below the reach
  band (S3.6), so nothing requires it.
- **S2.5** **Jump: the template input map is left intact.** `Jump` stays bound
  and the POC controller honours it, because a jump is a useful probe for
  collision and floor-contact bugs. The shipping default is expected to be
  *off* — T1 describes a calm building and R5.6 gets easier without it — but
  that call is not made here.
- **S2.6** The player never falls through anything. Floor and walls carry solid
  colliders; the player is a `CharacterController`, which is swept and cannot
  tunnel at walking speed. No `Rigidbody` player until something requires one.

## 3. The room (S3)

- **S3.1 (provisional)** The POC-04 room is **10 × 10 m internal, 3.2 m ceiling**.
  It is a **debug room**, not the archive. It exists to stand in, walk across,
  and measure against — it is explicitly not level art and nothing should be
  designed to fit it.
- **S3.2 (deferred)** The real archive's dimensions are **derived, not chosen**:

  ```
  room size = f(archive capacity, slot pitch, rack height, aisle width)
  ```

  Archive capacity (R4.1) is the missing input and it is a design decision, not
  a spatial one. See §9.
- **S3.3** A room is a **prefab**, pivot at floor centre, `y = 0` at the walkable
  surface. Scenes compose prefabs; nothing structural is authored directly into
  a scene.
- **S3.4** Every room prefab carries an empty **`Anchors/`** group. Anchors are
  where the generated collection meets physical space — one anchor per rack,
  table, or crate position. POC-04 creates the group empty; the convention is
  established now so that adding furniture is never a re-parenting exercise.
- **S3.5** Layers: **Ground** (walkable), **Structure** (blocking), **Player**.
  Collision matrix is explicit, not inherited from the template.
- **S3.6 (provisional)** Reach band for stored items: **0.3 m – 2.0 m**. Below
  it, R4.3 fails — a slot at ankle height has no readable face. Above it, the
  player cannot reach without equipment the game does not have.

## 4. Sky and light (S4) — deferred

- **S4.1 (deferred)** Art direction — palette, material language, period look —
  is **not decided**, and it is a hard prerequisite for any real lighting work.
  No lighting decision is made in this document.
- **S4.2** POC-04 therefore uses a **flat, calm, neutral pastel sky**: solid
  colour camera background, flat ambient, one low-intensity directional light
  purely so surfaces read as separate planes. No HDRI, no procedural sun disc,
  no baked lighting, no post-processing.
- **S4.3** Consequence worth stating: because POC-04 is unbaked, **no asset needs
  a second UV set yet**. When S4.1 resolves and lighting is chosen, that
  decision changes the mesh import contract retroactively. Do not commission
  meshes before it is made.

## 5. Structure and naming (S5) — applied, not yet ratified

Applied as proposed so POC-04 has somewhere to live. Cheap to change now,
expensive after the first fifty assets.

- **S5.1** New assembly **`Archivist.Building`**. It may reference `UnityEngine`
  and `Archivist.Generation`. This is what keeps Generation headless forever:
  the dependency points one way, and nothing in Building is ever a prerequisite
  for `Tools/run-acceptance.sh`.
- **S5.1a** The discipline is called *space*; the assembly is called
  **Building**, and the divergence is deliberate. `Archivist.Space` collides
  with `UnityEngine.Space` — inside that namespace, `Space.Self` resolves to the
  namespace and fails to compile. This assembly is where transform-heavy code
  lives, so the collision would bite constantly. Docs keep `docs/space/`; code
  says `Building`.
- **S5.2** Folders: `Assets/Archivist/Building/{Runtime,Prefabs,Materials,Models,Textures,Scenes}`.
- **S5.3** Asset naming: `SM_` static mesh, `M_` material, `T_` texture
  (`_BC` base colour, `_N` normal, `_MRA` packed), `PF_` prefab, `POC04_` scene.
- **S5.4** Everything is a prefab. Scenes are composition only. A change made in
  a scene and not in a prefab is a bug.
- **S5.5** Placeholder assets are named `*_Placeholder_*` so that the day they
  are replaced, finding all of them is a `grep`.

## 6. Placeholder materials (S6) — approved

- **S6.1** Three flat URP Lit materials: **floor** darker, **walls** warm
  paper-neutral, **ceiling** lighter. Unlit-flat is wrong — the point is to see
  the shape of the room.
- **S6.2** One checker material at a fixed **texel density of 512 px/m**. This
  is a measuring instrument, not art: it is how scale errors in an imported
  asset become visible instead of merely wrong.
- **S6.3** Placeholders are not a style. They are deliberately bland so that
  nobody mistakes the POC for a look.

## 7. Interaction (S7) — as built

The archive is made almost entirely of things you do something to: a crate you open,
a rack you file into, a table you lay a sheet on. S7 is the contract they share.

- **S7.1** One contract, three members: a **label**, an **availability gate**, and
  the **act**. Anything the player can act on exposes exactly these. Nothing
  larger has been needed yet, and nothing should be added to it speculatively.
- **S7.2** **Aim and proximity are one test** — a ray from the eye with a maximum
  distance is both. Reach is **2.5 m**. There is no separate trigger volume, no
  proximity list, and no second radius to keep in agreement with the first.
- **S7.3** The ray is stopped by geometry. Nothing is reachable through a wall or
  through furniture, and no interactable carries a rule saying so.
- **S7.4** The collider that stops the ray need not be the interactable. Resolution
  walks up the hierarchy — **never down** — so a collider anywhere in an object
  resolves to the component that owns the interaction. *Amended*: the original
  clause read "one object with many colliders — a rack with a collider per slot
  (R4.2) — is one interactable, not thirty", and the rack is the one case where
  that does not hold. Which component owns the interaction follows from S7.1, not
  from the collider count:
  - **Many colliders, one act.** A crate hit anywhere is the same crate: one
    label, one gate, one act. The walk passes the colliders and the interactable
    sits at the root. This is what the original clause described.
  - **Many colliders, one act each.** A rack's slots are not interchangeable
    surfaces of one object — each is a filing address with its own occupancy, and
    its label and availability differ by which is aimed at. Each slot's own object
    is the interactable, and the walk exists to pass decoration above it. A
    rack-level component could not answer S7.1's three members without first
    deriving which slot the ray hit, which is a second hit resolution behind the
    one S7.10 already did. `ShelfSlot : Interactable`, forty-eight to a rack at
    the default four rows of twelve.

  Because the walk only goes up, a slot volume enclosing the binder standing in it
  shadows that binder's own verbs — which is how `BinderPickup`'s merge is kept
  off the rack (Q3.3). That follows from the collider geometry, not from where the
  interactable sits.
- **S7.5 (the point of the other nine)** An interactable decides **what** happens
  and **what it is called**. It never decides reach, input binding, highlight, or
  how the label is drawn. Adding a kind of interactable is one class and no edit
  anywhere else.
- **S7.6** Availability is separate from presence. *Here, but not now* — a crate
  mid-draw, a full rack — shows the label greyed and refuses the act. There is no
  third state and no error message: R6.5 already establishes that a refused
  action in this game is silent.
- **S7.7** The prompt is the **only screen text the game has**. T2 forbids
  counters, scores and percentage complete; a verb under the reticle is none of
  those. It names the act and the key, and nothing else ever goes there.
- **S7.8** The key shown is read from the input action, never written into the
  prompt. Rebinding is then free, and the template action map (S2.5) stays the
  single source of what is bound to what.
- **S7.9** What an interaction *produces* — sheets, a board, a sound, a carried
  stack — is outside this contract. S7 ends at "the act happened". This is what
  keeps it from growing into a god-interface as the furniture arrives.
- **S7.10** No registry. Interactables are **found** by the ray, not registered on
  enable, so there is no list that can fall out of step with the scene and no
  bookkeeping when something is spawned, pooled or destroyed. Cost is one
  raycast per frame.

| piece | owns |
|---|---|
| `IInteractable` | the contract: label, availability, act |
| `Interactable` | base behaviour; what a ray resolves to |
| `PlayerInteractor` | ray, reach, binding, what is aimed at |
| `InteractionPrompt` | how a label is drawn, and nothing else |

### 7.1 Known strain

**R5.1 — "picking up a stack is one motion, not one action per item"** is not
obviously expressible in S7 as written. One act on one interactable is the shape
here; a stack is many items and one motion. Either the pile is itself the
interactable (likely right, and cheap), or S7 needs a second verb. This is not
decided, and it should be decided by building the pile rather than by argument.

## 8. Explicitly out of scope

Racks, drawers, the map table, carrying, stacking, filing, audio, doors,
windows, baked lighting, art direction, and the asset import contract.

**POC-04** asks one question: does the space stand up and can it be walked?
**POC-05** asks a second: can a thing in the room be acted on, and does the
generator reach the floor as paper? Everything else is a later document.

Not out of scope but not specified here either: the **sheet ledger** — which
sheets of which island have been issued into the world — and the **sheet as a
physical object**. Both are built; both belong to a collection document that does
not exist yet, because neither is a property of the space.

## 9. Acceptance

- **A1** The player spawns standing on the floor and does not fall through it.
- **A2** The player cannot leave the room through any of the four walls, at any
  approach angle, including into corners.
- **A3** The player cannot get stuck (R5.6) — walking into a corner and turning
  always recovers.
- **A4** The scene opens and enters play mode with an empty console.
- **A5** The room prefab has root scale `(1,1,1)`, pivot at `y = 0`, and every
  structural child carries a collider on the correct layer.
- **A6** Metric sanity: crossing the 10 m room at 1.8 m/s takes ~5.5 s. If it
  does not, S1.1 is being violated somewhere.

**POC-05 — interaction**

- **A7** Aiming at the crate from within reach shows its label; looking away
  hides it; walking out of reach hides it.
- **A8** The label does not appear through a wall from the far side.
- **A9** The bound key fires the act, and the key shown in the prompt is the key
  that works (S7.8).
- **A10** While the crate is working, its label reads as unavailable and a second
  press does nothing (S7.6).
- **A11** Opening the crate never repeats a sheet, within a batch or across
  batches of the same island. This is R2.10, and it is the ledger's whole job.
- **A12** Opening the crate does not visibly stall the room. Generation and
  rendering run off the main thread; measured at 340 ms for one island plus five
  sheets, none of it on the main thread.

## 10. Open questions

- **Archive capacity (R4.1).** Blocks S3.2, and therefore blocks the real room,
  the rack count, and the first furniture asset. This is the highest-leverage
  unanswered question in the project's 3D half.
- **Art direction.** Blocks S4.1, and through S4.3 blocks every mesh and texture
  purchase.
- **Carried speed fraction** (S2.2). Needs something to carry.
- **Jump in the shipping build** (S2.5).
- **Whether §5 of the workflow — the asset import contract — is ratified before
  or after the first real asset arrives.** Before is cheaper. It was deferred
  deliberately, not forgotten.
- **Stack pickup against S7** (§7.1). Blocks the first pile.
- **Whether an aimed interactable needs a highlight as well as a label.** T5 asks
  for plain and even; an outline shader is neither. Probably not, but it will be
  asked the first time someone cannot find the crate.
- **Sheet render resolution.** At 1.2 px per paper mm the line work disappears —
  a 0.35 mm coast stroke is 0.42 px — and a sheet reads as colour blocks only.
  That is the correct *pile* LOD (R3.2) and wrong for anything in hand, so the
  three LODs of R3.2 need real numbers rather than one tunable field.
