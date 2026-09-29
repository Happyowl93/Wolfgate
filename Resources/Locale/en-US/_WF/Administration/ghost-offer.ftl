# Wolfgate admin: offer a mob or ghost role to every ghost

## Verb
wf-admin-verbs-offer-to-ghosts = Offer to Ghosts
wf-admin-verbs-offer-to-ghosts-description = Makes this a ghost role if it isn't one and asks every ghost whether they want it.

## Server messages
wf-ghost-offer-default-description = An admin is offering control of {$name}.
wf-ghost-offer-sent = Offered {$name} to {$count} {$count ->
    [one] ghost
    *[other] ghosts
}.
wf-ghost-offer-error-deleted = That entity no longer exists.
wf-ghost-offer-error-has-player = {$entity} already has a player.
wf-ghost-offer-error-unavailable = The ghost role on {$entity} is taken or not available.

## Ghost sign-up prompt
wf-ghost-offer-prompt-title = Ghost Role Offer
wf-ghost-offer-prompt-raffle = Signing up enters you into a short draw.
wf-ghost-offer-prompt-first-come = The first ghost to sign up takes it.
wf-ghost-offer-prompt-sign-up = Sign up
wf-ghost-offer-prompt-follow = Follow
wf-ghost-offer-prompt-all-roles = All ghost roles
wf-ghost-offer-signup-joined = You're in the draw for {$name}.
wf-ghost-offer-signup-taken = You took {$name}.
wf-ghost-offer-signup-already = You've already signed up.
wf-ghost-offer-signup-failed = You can't sign up for {$name}.
wf-ghost-offer-signup-not-ghost = Only ghosts can sign up.
wf-ghost-offer-signup-closed = This offer has closed.

## offertoghosts command
cmd-offertoghosts-desc = Makes an entity a ghost role if it isn't one and asks every current ghost whether they want it.
cmd-offertoghosts-help = Usage: {$command} <entity> [name] [description] [rules]
    Blank texts keep the ghost role's own, or the entity's name and a default description for a new one.
cmd-offertoghosts-hint-entity = <entity>
cmd-offertoghosts-hint-name = [name]
cmd-offertoghosts-hint-description = [description]
cmd-offertoghosts-hint-rules = [rules]
cmd-offertoghosts-invalid-args = Expected an entity and up to three texts.
cmd-offertoghosts-invalid-entity = {$entity} is not a valid entity.
