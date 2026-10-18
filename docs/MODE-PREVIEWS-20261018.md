# Planned-mode previews

Capture the Flag and Domination appear after Zombies as gray, disabled rows in
Arena's location-screen mode panel. They are presentation only: neither enters
the playable mode catalog, persisted selection, participant rules or raid arm.
The cloned native button event is replaced by an empty event, keyboard navigation
is disabled, and the tooltip explicitly says the mode is planned.
The preview clones also disable their inherited Animators before styling: the
native disabled animation otherwise overwrites the neutral gray label after
activation. The shared prefab and playable rows keep their animations.

Seven compact rows fit above the existing facts divider. The panel footprint,
native map controls, area selector and rule rows retain their previous positions.
Existing real modes keep their order and PFT restrictions.

Independent review: preview buttons are deliberately excluded from the real
button/marker lists, so refresh, PFT switching and teardown cannot promote a
preview to a selectable mode. The root owns all preview objects; closing the
screen destroys them, and they retain no callbacks.

Validation: production-linked TdmMatchMenuChecks passes 2,526 assertions,
including empty events even under direct dispatch, gray text, PFT refresh,
out-of-range state refusal and non-overlapping button/facts geometry. A positive
native-color-animation double runs on playable rows while preview gray survives.
Native
layout evidence is recorded separately in the combined GPU feedback report.
