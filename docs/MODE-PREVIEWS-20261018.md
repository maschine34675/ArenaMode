# Planned-mode previews

Capture the Flag and Domination appear after Zombies as gray, disabled rows in
Arena's location-screen mode panel. They are presentation only: neither enters
the playable mode catalog, persisted selection, participant rules or raid arm.
The cloned native button event is replaced by an empty event, keyboard navigation
is disabled, and the tooltip explicitly says the mode is planned.
The preview clones also disable their inherited Animators before styling: the
native disabled animation otherwise overwrites the neutral gray label after
activation. The shared prefab and playable rows keep their animations.
The preview surface itself also uses a darker, neutral gray disabled tint;
the regular blue-gray theme remains on the playable rows. This override belongs
only to the two clones and does not alter the shared theme or native prefab.

Seven compact rows fit above the existing facts divider. The panel footprint,
native map controls, area selector and rule rows retain their previous positions.
Existing real modes keep their order and PFT restrictions.

Independent review: preview buttons are deliberately excluded from the real
button/marker lists, so refresh, PFT switching and teardown cannot promote a
preview to a selectable mode. The root owns all preview objects; closing the
screen destroys them, and they retain no callbacks.

Initial validation: production-linked TdmMatchMenuChecks passed 2,526 assertions,
including empty events even under direct dispatch, gray text, PFT refresh,
out-of-range state refusal and non-overlapping button/facts geometry. A positive
native-color-animation double runs on playable rows while preview gray survives.
The October 18 surface follow-up (`53fa560`) passes the current 2,527 menu
assertions. GPU2 confirms that both complete surfaces are neutral gray;
clicking either leaves all 224 Arena settings unchanged. See
[the merged-log/native test report](GPU-LOG-AUDIT-20261018.md) for the paired
loaded build and screenshots. Earlier layout evidence remains in the combined
GPU feedback report.
