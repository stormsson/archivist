# Archivist — The Island Survey Archive

Unity 6000.0.34f1, URP. Single-player, first person, no fail state, no timer.

## The game

Islands have been surveyed many times, by different offices, over many years.
Nobody ever put the results in order. The sheets arrive in crates.

The player is the archivist: receive maps, shelve them correctly. One building,
a fixed set of shelves, no visitor and no deadline. There are always more
islands. The sea they sit in is never drawn.

A second, optional activity: a map table where sheets can be laid out and
joined, recovering the shape of one island at a time. Nobody asked for this.

## The two ideas everything else follows from

1. **An island is a function of its seed.** One seed → one island, completely
   and identically, forever. Nothing geometric is ever persisted — only the
   seed. A sheet in the world stores an *identity*, not its ground; asking what
   it covers means regenerating the island.
2. **The island is never shown.** No world geometry above it, no spatial
   relationship between islands. The player's only access to the ground is
   paper. Showing the island would answer the question the game is about.

Three offices survey the same ground with different remits, so two sheets of one
hillside are genuinely different documents. That difference is the game.

## Where things are

`docs/architecture.md` is the map of the code — read it first. Assemblies:

- `Archivist.Generation` — what an island *is*. **Never references UnityEngine.**
- `Archivist.Render` — what it *looks like*. Also never references UnityEngine.
- `Archivist.Building` — the room, the player, the paper. Engine side.

The engine-free rule on the first two is load-bearing: it is what lets the
acceptance suite run headless (`Tools/run-acceptance.sh`). Do not break it.

## Docs, and their authority

Intent → construction → measured → as built. Later never overrules earlier;
where they disagree, that is recorded, not silently fixed.

- `docs/requirements.md` — the game. Top authority.
- `docs/space/requirements.md` — the 3D half (S-numbers). Every dimension in the
  room comes from here.
- `docs/poc01/`, `docs/poc02/`, `docs/poc03/` — requirements → spec → findings,
  per POC. Findings are measured, not reasoned.
- `docs/generation_for_agents.md` — the generator **as built**. Read before
  touching `Archivist.Generation`.

Requirements are cited by number (R1.11, S3.1, F-02.2). Use them when explaining
a change; if code and a numbered requirement disagree, say so rather than
quietly picking one.

## Working here

- Where the POC stands: POC-01 generator, POC-02 renderer, POC-03 POIs,
  POC-04 the room, POC-05 interaction. The game's scene is
  `POC04_Room.unity`; `Debug_Generator.unity` is the crate on its own, for
  generation work.
- Determinism is a hard contract. New randomness goes through a **named
  sub-stream** (`StreamNames`) so an unrelated draw cannot move an island.
- Tuning constants live in one place per assembly: `Tuning`, `RenderTuning`,
  `HandlingOptions`. Do not scatter magic numbers into behaviours.
- Class-level XML doc comments here explain *why*: the rule, and the trap that
  must not be "fixed". State the claim once, in as few lines as carry it. Do
  **not** narrate the change — no "it used to", "the first version", "both are
  gone". Decision history belongs in `docs/`, cited by number (D-C10, G7.1
  superseded); a comment that retells it is a second copy that drifts. A
  rejected alternative earns a sentence only when someone would otherwise
  reintroduce it. Match that when adding code.
- Scripts build geometry (`RoomBuilder`), rather than geometry being hand-placed
  — provisional numbers have to be cheap to rebuild.
