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
