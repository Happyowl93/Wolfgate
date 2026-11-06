## Traits

trait-wf-hemophagia-name = Hemophagia
trait-wf-hemophagia-desc = A tumorous symbiont has taken your heart's place. You no longer eat or breathe; only blood keeps you going, and you lose it all the time. Drink it, or drain it from a living body you are pulling. The tumor makes your stomach and liver reject anything but blood.
trait-wf-hemophagia-masquerade-name = Hemophagia (Masquerade)
trait-wf-hemophagia-masquerade-desc = As Hemophagia, but your tumor has learned to put up with ordinary food and drink, so you can eat in company without retching.

wf-hemophage-spawn-text = You are a [color=#c00000]Hemophage[/color]. You will slowly but constantly lose blood. Gain more by drinking it, or by pulling a living victim and using Drain Victim. The blood gauge shows your reserves; click it for the exact amount.

## Organs

ent-WFOrganHemophageTumor = pulsating tumor
    .desc = This pulsating organ nearly resembles a heart, but it has been twisted beyond any human shape and turned the colour of coal. The fact that it is what keeps its host alive makes it all the more terrifying.

ent-WFOrganHemophageTongue = tongue
    .desc = A fleshy muscle, mostly used for lying. This one is used for drinking.

wf-tumor-corrupted-organ = corrupted { $organ }
wf-tumor-atrophied-organ = atrophied { $organ }
wf-tumor-corrupted-organ-desc = This shares the shape of a normal organ, but it has been filled with midnight-black, pulsing tissue, engorged with some infectious mass.

## Actions

ent-WFActionHemophageDormant = Enter Dormant State
    .desc = Puts your tumor to sleep so it needs only a trickle of blood. It no longer lets you taste or use your other powers, and the lack of blood leaves you slow and terribly fragile. It takes time to rouse it again.
wf-hemophage-action-dormant-enter = Enter Dormant State
wf-hemophage-action-dormant-enter-desc = Puts your tumor to sleep so it needs only a trickle of blood. It no longer lets you taste or use your other powers, and the lack of blood leaves you slow and terribly fragile. It takes time to rouse it again.
wf-hemophage-action-dormant-exit = Exit Dormant State
wf-hemophage-action-dormant-exit-desc = Wakes the pitch-black mass inside you, letting blood pump freely again. It isn't a switch: once awake, it takes time to subdue again.

ent-WFActionHemophageRegen = Hemokinetic Regeneration
    .desc = While active, blood from your reserves mends minor wounds as they occur. It can't keep up with heavy damage.
ent-WFActionHemophageClot = Hemokinetic Clot
    .desc = Clots your worst bleeding wound for a measure of blood. It is a temporary fix: the wound reopens the next time that limb is hurt.
ent-WFActionHemophageMaster = Master of the House
    .desc = Wrest control of your lungs from the tumor. You must breathe air again and move sluggishly with half your stamina, but your blood slowly returns.
ent-WFActionHemophageDrain = Drain Victim
    .desc = Drink blood from the living body you are pulling.

## Alerts

wf-alert-hemophage-blood-name = Blood Reserves
wf-alert-hemophage-blood-desc = How much blood your tumor has left to live on. Click to check the exact amount.
wf-alert-hemophage-satiated-name = Thirst Satiated
wf-alert-hemophage-satiated-desc = Substitutes keep your pale body standing, but nothing slakes the infection like the real thing: hot blood from a real person. Your tumor uses blood more slowly for now.
wf-alert-hemophage-regen-name = Hemokinetic Regeneration
wf-alert-hemophage-regen-desc = Your wounds are healing at the expense of blood.
wf-alert-hemophage-master-name = Master of the House
wf-alert-hemophage-master-desc = You are holding your lungs back from the tumor. Breathing needs air again, but your enriched blood soothes the hunger within. You are more sluggish while you keep this up.
wf-alert-hemophage-slave-name = Slave to the Tumor
wf-alert-hemophage-slave-desc = You have given your lungs back to the tumor. It is going to take some time to repair the damage.

## Messages

wf-hemophage-ran-out = You ran out of blood!
wf-hemophage-convulse = { CAPITALIZE(THE($target)) } convulses, black veins throbbing outward along { POSS-ADJ($target) } body.
wf-hemophage-blood-level = Blood: { $current }u of { $max }u.
wf-hemophage-blood-too-low = Your blood is too low!

wf-hemophage-dormant-entering = Entering dormant state...
wf-hemophage-dormant-leaving = Leaving dormant state...
wf-hemophage-dormant-cancelled = You stop changing state.
wf-hemophage-dormant-start = You feel your tumor's pulse slow as it goes dormant. You suddenly feel weak and vulnerable to everything, and every step is an effort; only your most vital functions remain.
wf-hemophage-dormant-end = A rush runs through your veins as your tumor pulses at its regular pace again. You no longer feel so vulnerable, and moving is easier.

wf-hemophage-regen-on = Hemokinetic regeneration activated.
wf-hemophage-regen-off = Hemokinetic regeneration deactivated.
wf-hemophage-regen-too-damaged = Your body is too damaged to be healed with hemokinesis!

wf-hemophage-clot-nothing = No wounds to clot!
wf-hemophage-clot-done = You use hemokinesis to clot the wound on your { $part }.
wf-hemophage-clot-reopened = The clot on your { $part } comes apart as it is hurt again!
wf-hemophage-blood-warmth = A euphoric warmth washes through you as the blood takes hold. Your wounds feel healthier.

wf-hemophage-master-recovering = You are still recovering...
wf-hemophage-master-on = You wrest control of your lungs from the tumor. You can't keep this up forever.
wf-hemophage-master-off = You release your lungs back to the tumor.
wf-hemophage-slave-start = You feel a sense of relief as you embrace the tumor once more...

wf-hemophage-tasted-awful = That tasted awful...
wf-hemophage-nausea = Your stomach turns. You feel queasy.

wf-hemophage-drain-no-target = You aren't pulling anyone you can drain!
wf-hemophage-drain-full = You're already full!
wf-hemophage-drain-dead = You need a living victim!
wf-hemophage-drain-unsuitable = { CAPITALIZE(THE($target)) } doesn't have suitable blood!
wf-hemophage-drain-holy-victim = { CAPITALIZE(THE($user)) } tries to bite you, but stops before touching you!
wf-hemophage-drain-holy = { CAPITALIZE(THE($target)) } is blessed! You stop just in time to avoid catching fire.
wf-hemophage-drain-garlic-victim = { CAPITALIZE(THE($user)) } tries to bite you, but recoils in disgust!
wf-hemophage-drain-garlic = { CAPITALIZE(THE($target)) } reeks of garlic! You can't bring yourself to drain such tainted blood.
wf-hemophage-drain-animal-full = Their inferior blood can't sate you any further!
wf-hemophage-drain-stopped = You stop feeding.
wf-hemophage-drain-victim = { CAPITALIZE(THE($user)) } drains some of your blood!
wf-hemophage-drain-animal = You choke back tepid mouthfuls of foul blood from { THE($target) }. The taste is absolutely vile.
wf-hemophage-drain-foreign = You force down mouthfuls of { THE($target) }'s strange blood. Your tumor recoils at every drop.
wf-hemophage-drain-person = You pull greedy gulps of precious lifeblood from { THE($target) }'s veins!
wf-hemophage-drain-player = You pull greedy gulps of precious lifeblood from { THE($target) }'s veins! That tasted particularly good!
wf-hemophage-drain-slaked = Your thirst is slaked for now; you can hold no more new blood.
wf-hemophage-drain-pale = That definitely left them looking pale...
wf-hemophage-drain-pale-victim = A groaning lethargy creeps into your muscles as you begin to feel clammy...
wf-hemophage-drain-dry = A final sputter of blood trickles from { THE($target) }'s collapsing veins as your hunger drains them almost dry.
wf-hemophage-drain-hesitate = Hesitation gnaws at you: taking much more blood from { THE($target) } WILL kill them. ...but another part of you sees only opportunity.

## Food and drink

ent-WFFoodBloodRiceRaw = uncooked blood rice
    .desc = A clump of raw rice, drenched in blood.
ent-WFFoodBloodRicePearl = kessen shinju
    .desc = Little clumps of sticky rice soaked and rolled in fresh blood, giving them a crimson hue. Best served hot.
ent-WFFoodBloodNoodles = boiled blood noodles
    .desc = A plain dish of blood-soaked noodles. It would probably be better with more ingredients.
ent-WFFoodBoatNoodles = boat noodles
    .desc = Blood curds and curly noodles, seasoned and steeped in fresh blood until they turned crimson. It reeks of iron.
ent-WFFoodBloodCurd = blood curd
    .desc = Also known as blood tofu or blood pudding: congealed and cooked blood. Soft, smooth and slightly chewy.
ent-WFFoodBloodCake = ti hoeh koe
    .desc = A pig's blood cake of steamed blood and sticky rice, coated in sugar. The amount of blood in this one has been turned up a lot.
ent-WFFoodBloodSoup = dinuguan
    .desc = A savoury stew of blood curds in a rich, spicy gravy of fresh blood and vinegar, with chili and garlic.

wf-recipe-blood-rice-pearl = kessen shinju recipe
wf-recipe-blood-curd = blood curd recipe
wf-recipe-blood-noodles = blood noodles recipe
wf-recipe-boat-noodles = boat noodles recipe
wf-recipe-blood-cake = ti hoeh koe recipe
wf-recipe-blood-soup = dinuguan recipe

wf-flavor-complex-congealed-blood = like congealed blood
wf-flavor-complex-bloodshot = like blood filled to the brim with nutrients
wf-flavor-complex-bloodshot-base = like a nutritious mix with an alcoholic kick

reagent-name-wf-bloodshot-base = bloodshot base
reagent-desc-wf-bloodshot-base = The bootleg blend of nutrients and alcohol that goes into a Bloodshot. Doesn't taste great on its own, to a hemophage at least.
reagent-name-wf-bloodshot = bloodshot
reagent-desc-wf-bloodshot = A cocktail first mixed to carry nutrients to a hemophage's tumorous organs. It still tastes like a hangover cure, and it restores lost blood quickly. It smells like iron.
reagent-name-wf-taste-suppressor = taste suppressor
reagent-desc-wf-taste-suppressor = A colourless medicine that dulls the sense of taste while it is in the body. A hemophage's tumor won't reject what they eat or drink while it lasts.

wf-reagent-effect-guidebook-suppress-taste =
    { $chance ->
        [1] Dulls
       *[other] chance to dull
    } the sense of taste, so a hemophage's tumor accepts any food
