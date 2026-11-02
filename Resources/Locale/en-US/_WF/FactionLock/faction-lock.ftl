# Why the job list locks a job for this character
wf-faction-lock-played = This character has served { $faction ->
        [TSF] the Trans-Solarian Federation
        [PDV] the Phaethon Dynasty
        [MMC] the Mieyo Manufacturing Concern
        [VG] the Viper Group
        [USSP] the USSP
        [Underground] the Underground
        [ASF] the Astraeus Solar Federation
       *[other] another faction
    } and can't serve an opposing faction.
wf-faction-lock-company = This character is in a company. Company members can't serve a faction.

cmd-factionunlock-desc = Lists a player's characters and their faction locks, or clears one character's lock.
cmd-factionunlock-help = Usage: factionunlock <player> [slot or character name]
cmd-factionunlock-hint-player = Player
cmd-factionunlock-hint-character = Character
cmd-factionunlock-no-player = No player matches "{ $player }".
cmd-factionunlock-no-characters = { $player } has no saved characters.
cmd-factionunlock-no-character = No single character matches "{ $character }". Use a slot number:
cmd-factionunlock-not-locked = { $character } has no faction lock.
cmd-factionunlock-done = Cleared the faction lock on { $character } ({ $player }).
cmd-factionunlock-character = { $slot }: { $character } - { $factions }
cmd-factionunlock-free = no lock
