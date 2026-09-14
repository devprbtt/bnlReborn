# Piggy Bank stored balances

Piggy Banks expose their current stored bricks through the existing UnitUpdate
Resource field. Full snapshots compute the amount from server creation time, so
late joins and reconnects do not reset the displayed balance. During matches,
only changes to the whole-brick amount are broadcast, on the existing 200 ms
maintenance cadence. Dead banks are excluded.

Payout and snapshots share PiggyBankBalance.Calculate, preserving the existing
seconds * resourcePerInterval / (generationInterval * 5) rule. Invalid rates,
intervals and ages return zero. The client must not estimate age from receipt
of a unit-create message or treat this device balance as the player's wallet.

Validated with the PiggyBankFixture (94 checks including snapshot/wire round-trip)
and ConquestFixture (77 checks). Existing protocol layout is unchanged.
