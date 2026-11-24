# Recommended Blocks duplicate hero-device fix - 2026-11-24

Player note: Fixed Recommended Blocks allowing multiple hero blocks through
different skins. Hero blocks and their cosmetic variants can occupy only slot 1.

Prepared hotfix source; not deployed. Branch is based directly on production
`ed896f6bd255c3ce4a5e9e700135b487ab335f0d`, independently of pending map changes.
No CDB, protocol or schema migration is required. The server protection can be
deployed independently of the client update.

## Cause

The profile block picker's `OnRecommendDevices` appended **all** entries in
`UnitDataPlayer.SpecialDevices` before the five regular recommendations.
For heroes with cosmetic variants, these entries share a single device group.
Recommending each variant repeatedly moved that group through successive slots.
Tony has three entries and Vander has six in the recovered catalogue; Cogwheel
and Kira have two. The server repaired the missing/invalid first slot with the
default hero block, but its loadout rules allowed the extra hero-block skin in
a regular slot. The lobby Recommended Blocks RPC itself already uses only the
first special-device entry; the profile picker was the faulty client route.

## Server protection

`LoadoutRules.IsHeroDevice` recognizes the hero's special-device keys and their
non-empty cosmetic group identity. `CanUseDeviceInSlot` retains existing slot 1
validation and rejects any hero-device variant in slots above 1. Existing class
and inventory checks still apply to normal devices.

- Saved-loadout submission, saved-loadout reads, lobby hero selection and match
  preparation already run `LoadoutRules.Sanitize`; those routes now remove extras.
- Direct lobby device writes use the same slot-specific rule, including slot 7.
- Profile catalogue sanitization removes extra hero devices and reports the
  change for the existing profile persistence mechanism. A valid chosen skin in
  slot 1 is preserved; missing or invalid slot 1 retains the existing repair.

This does not add a general ban on repeated normal building blocks or change
legitimate regular-device skins, class permissions or device slot counts.
Already-running matches retain their initial loadouts until they end/restart.

## Client repair and validation

The matching client recommends `SpecialDevices.Take(1)` plus the five regular
defaults. It no longer moves the hero group repeatedly for cosmetic variants.

Server tests pass: 27 special-device checks (actual lobby writes, profile repair,
slot 7, valid skin changes and existing Brains perk behavior), plus 220
real-catalogue loadout/RPC checks across all 15 heroes and 23 special-device
variants. All defaults remain valid; slots 2..7 reject all hero variants;
SetLoadout retains the chosen skin only in slot 1. Server build passes with
warnings as errors, zero warnings/errors.

Matching Unity Editor validation passes 576 checks through the actual
recommended-button method: 1/2/3/6 cosmetic entries, six/seven slots, every
starting position for the hero group, and three successive presses. Each yields
one hero block at the front, all five regular recommendations and no duplicate
groups. No production or installed-client match was used in these checks.
