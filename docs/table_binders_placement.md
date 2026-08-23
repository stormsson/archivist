 i want to work on the interaction with the table when holding a binder.
  
  
  The binding anchors are children of "BindingAnchors" empty object inside the table prefab.

  When the table is created or loaded entering the scene, the table must count how many children there are in "BindingAnchors", to identify the max capacity of the table in "binders" units.

  the table will be able to contain up to MAX_ANCHORS binders on it.

# interaction
  when the player interacts with the table while holding a binder:
IF:
    - the table has no previous binders 
THEN:
    => the binder gets positioned in the first available anchor point
    => the table receives the sheets contained in that binder
    => binder is removed from player hands
IF:
    - the table has at least one previous binders 
    AND
    - the first binder on the table is related to the same island of the binder the player is holding 

THEN:
    => the table receives the sheets contained in that binder, in addition to the ones it had before.
    => binder is removed from player hands

  # binder positioning
  - positioning the binder in an anchor point assigns to the binder a random rotation +/- 20deg on the y axis, chosen at runtime (differnt interactions => different rotation)