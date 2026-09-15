# Casual map voting weights

On each map document in CDB, set the optional property `casual_vote_weight`:

```json
{
  "_id": "map_sr2_sky_bridge_don_edit_conquest",
  "category": "map",
  "casual_vote_weight": 4
}
```

This is a field-edit example, not a complete replacement document. Retain all existing fields and `_rev` when editing the map card.

- Missing/null weight means **1**, so existing maps retain equal relative weight by default.
- Positive numbers are relative weights: **4** gives four times the weight of **1** on each draw; **0.5** gives half the weight.
- Zero, negative and non-finite values fall back to **1**. Remove a map from the pool to exclude it.
- Weights influence which maps appear on the ballot, not the players' votes or the final vote winner.
- Casual (`friendly` and `graveyard`) ballots draw distinct maps without replacement until the configured option count is reached (normally three). Duplicate pool IDs never add chances or occupy extra slots. Short pools show the distinct available maps.
- Only existing eligible map cards with map payloads in the selected pool are considered. A high weight does not add a map to the casual pool.
- Ranked and automatic custom-map selection remain uniform; explicit custom-map choices bypass this selector.
- CDB updates affect newly created voting ballots through the existing catalogue watcher. Current ballots remain intact. The field is server-only and adds no client wire data; no client update is required.

Weight is not a percentage or a guaranteed appearance. With six maps, five at weight 1 and one at weight 4, the boosted map's chance of appearing among three options is approximately **88.10%**, versus **50%** when all six weights are equal. The draw removes each selected map before choosing the next option.

Validation from the server repository:

```powershell
dotnet run -c Release --project tests/BNLReloadedServer.MapVoteFixture -- path/to/catalogue-export.json
```

The fixture checks distribution against an independent probability calculation, unique ballots, duplicate pool IDs, short pools, invalid/extreme weights, casual/ranked separation, CDB parsing/round-trip, unchanged client wire bytes, and incremental catalogue updates. No production weights or pools have been modified; deployment is pending.
