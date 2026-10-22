/* ---------- completed match history ---------- */

let historyRows = [];
let historyNextBefore = null;
let historyLoaded = false;
let historyLoading = false;
let historyExpandedId = null;
const historyDetails = new Map();
const historyModes = new Set();

/* Archive keys deliberately carry both their stable CDB key and display name. The UI
   only renders the latter; normalise at the API boundary so objects never leak into
   text, chips, filters, or the loadout timeline as "[object Object]". */
function archiveLabel(value) {
  if (value != null && typeof value === "object")
    return String(value.name ?? value.key ?? "Unknown");
  return String(value ?? "Unknown");
}

function normaliseHistoryMatch(match) {
  return {
    ...match,
    map: archiveLabel(match.map),
    mode: archiveLabel(match.mode),
  };
}

function normaliseHistoryDetail(detail) {
  return {
    ...detail,
    map: archiveLabel(detail.map),
    mode: archiveLabel(detail.mode),
    players: (detail.players || []).map((player) => ({
      ...player,
      presences: (player.presences || []).map((presence) => ({
        ...presence,
        hero: archiveLabel(presence.hero),
        skin: archiveLabel(presence.skin),
        devices: (presence.devices || []).map((device) => ({
          ...device,
          device: archiveLabel(device.device),
        })),
        perks: (presence.perks || []).map((perk) => ({
          ...perk,
          perk: archiveLabel(perk.perk),
        })),
      })),
    })),
  };
}

function formatHistoryTime(value) {
  return new Date(Number(value)).toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });
}

function formatHistoryDuration(seconds) {
  const total = Math.max(0, Math.floor(Number(seconds) || 0));
  return Math.floor(total / 60) + ":" + String(total % 60).padStart(2, "0");
}

function historyMap(match) {
  return typeof mapRows === "undefined"
    ? null
    : mapRows.find((row) => row.name === match.map);
}

function historyImage(match) {
  const map = historyMap(match);
  return map ? mapImageSrc(map) : null;
}

async function loadHistory(reset = false) {
  if (historyLoading || (!reset && historyLoaded && historyNextBefore == null))
    return;
  historyLoading = true;
  if (reset) {
    historyRows = [];
    historyNextBefore = null;
    historyDetails.clear();
    historyExpandedId = null;
  }
  renderHistory();
  try {
    const query = new URLSearchParams({ limit: "50" });
    if (!reset && historyNextBefore != null)
      query.set("before", historyNextBefore);
    const res = await fetch("/api/matches?" + query);
    if (!res.ok) throw new Error("request failed");
    const data = await res.json();
    const items = (data.items || []).map(normaliseHistoryMatch);
    historyRows.push(...items);
    items.forEach((match) => historyModes.add(match.mode));
    historyNextBefore = data.next_before ?? null;
    historyLoaded = true;
    /* Maps are only needed for optional thumbnails, and their own module owns this cache. */
    if (typeof loadMaps === "function" && !mapsLoaded) await loadMaps();
  } catch {
    if (!historyRows.length) historyLoaded = false;
    showToast("error", "Failed to load match history.");
  } finally {
    historyLoading = false;
    renderHistory();
  }
}

function loadOlderHistory() {
  loadHistory(false);
}

function filterHistory() {
  renderHistory();
}

function selectedHistoryModes() {
  return [
    ...document.querySelectorAll("#historyModeFilters input:checked"),
  ].map((input) => input.value);
}

function renderHistoryModeFilters() {
  const selected = new Set(selectedHistoryModes());
  const root = document.getElementById("historyModeFilters");
  const hasExistingChoices = root.querySelector("input") !== null;
  root.innerHTML = [...historyModes]
    .sort()
    .map(
      (mode) =>
        '<label><input type="checkbox" value="' +
        esc(mode) +
        '"' +
        (!hasExistingChoices || selected.has(mode) ? " checked" : "") +
        ' onchange="filterHistory()"> <span>' +
        esc(mode) +
        "</span></label>",
    )
    .join("");
}

function renderHistory() {
  const body = document.getElementById("historyBody");
  if (!body) return;
  renderHistoryModeFilters();
  const query = document
    .getElementById("historySearch")
    .value.trim()
    .toLowerCase();
  const selectedModes = new Set(selectedHistoryModes());
  const rows = historyRows.filter(
    (match) =>
      selectedModes.has(match.mode) &&
      (!query || match.map.toLowerCase().includes(query)),
  );
  document.getElementById("historyCount").textContent = historyRows.length
    ? rows.length === historyRows.length
      ? historyRows.length + " loaded"
      : rows.length + " of " + historyRows.length + " loaded"
    : "";
  if (!historyLoaded && historyLoading) {
    body.innerHTML =
      '<tr class="empty-row"><td colspan="5">Loading match history…</td></tr>';
  } else if (!historyRows.length) {
    body.innerHTML =
      '<tr class="empty-row"><td colspan="5">No completed matches have been recorded yet.</td></tr>';
  } else if (!rows.length) {
    body.innerHTML =
      '<tr class="empty-row"><td colspan="5">No loaded matches match that filter.</td></tr>';
  } else {
    body.innerHTML = rows
      .map((match) => historyRowHtml(match) + historyDetailHtml(match))
      .join("");
    bindHistoryRows();
  }
  const more = document.getElementById("historyMore");
  more.hidden = historyNextBefore == null;
  more.disabled = historyLoading;
  more.textContent = historyLoading ? "Loading…" : "Load older matches";
}

function historyRowHtml(match) {
  const open = historyExpandedId === match.id;
  const image = historyImage(match);
  const result = historyResultLabel(match.end_reason, match.winner);
  return (
    '<tr class="history-row' +
    (open ? " open" : "") +
    '" data-history-match="' +
    encodeURIComponent(match.id) +
    '" aria-expanded="' +
    open +
    '"><td><span class="history-map">' +
    '<span class="history-caret">' +
    (open ? "&#9662;" : "&#9656;") +
    "</span>" +
    (image ? '<img class="map-thumb" src="' + esc(image) + '" alt="">' : "") +
    esc(match.map) +
    "</span></td><td>" +
    esc(match.mode) +
    "</td><td>" +
    formatHistoryTime(match.started_at) +
    "</td><td>" +
    formatHistoryDuration(match.duration_seconds) +
    '</td><td><span class="history-result' +
    (match.end_reason === "Abandoned" ? " draw" : " win") +
    '">' +
    esc(result) +
    "</span></td></tr>"
  );
}

function historyResultLabel(reason, winner) {
  switch (reason) {
    case "ObjectivesDestroyed":
      return "Objectives destroyed";
    case "Surrender":
      return "Surrender";
    case "ObjectivesCompleted":
      return "Objectives completed";
    case "Abandoned":
      return "Abandoned";
    default:
      return winner && winner !== "Neutral"
        ? "Match complete"
        : "No result recorded";
  }
}

function historyDetailHtml(match) {
  if (historyExpandedId !== match.id) return "";
  const detail = historyDetails.get(match.id);
  const content =
    detail === undefined
      ? '<div class="history-detail-loading">Loading match details…</div>'
      : detail === null
        ? '<div class="history-detail-error">Could not load this match.</div>'
        : renderHistoryDetail(detail);
  return (
    '<tr class="history-detail"><td colspan="5"><div class="history-detail-box">' +
    content +
    "</div></td></tr>"
  );
}

function bindHistoryRows() {
  document
    .querySelectorAll("[data-history-match]")
    .forEach((row) =>
      row.addEventListener("click", () =>
        toggleHistoryMatch(decodeURIComponent(row.dataset.historyMatch)),
      ),
    );
}

async function toggleHistoryMatch(id) {
  historyExpandedId = historyExpandedId === id ? null : id;
  renderHistory();
  if (historyExpandedId !== id || historyDetails.has(id)) return;
  try {
    const res = await fetch("/api/matches/" + encodeURIComponent(id));
    if (!res.ok) throw new Error("request failed");
    historyDetails.set(id, normaliseHistoryDetail(await res.json()));
  } catch {
    historyDetails.set(id, null);
  }
  if (historyExpandedId === id) renderHistory();
}

function latestPresence(player) {
  const presences = player.presences || [];
  return presences.length ? presences[presences.length - 1] : null;
}

function stat(player, name) {
  return Number((player.stats || {})[name] || 0);
}

function historyMmr(value) {
  const points = Math.round(Number(value) * 100);
  return Number.isFinite(points) ? points.toLocaleString() : "";
}

function historyMmrHtml(player) {
  if (player.starting_mmr == null || player.mmr_delta == null) return "";
  const start = historyMmr(player.starting_mmr);
  const delta = Math.round(Number(player.mmr_delta) * 100);
  if (!start || !Number.isFinite(delta)) return "";
  const direction = delta > 0 ? " gain" : delta < 0 ? " loss" : "";
  return (
    '<br><span class="history-mmr">MMR ' +
    start +
    ' <b class="history-mmr-delta' +
    direction +
    '">' +
    (delta >= 0 ? "+" : "") +
    delta.toLocaleString() +
    "</b></span>"
  );
}

function historyPortrait(icon) {
  const portraits = typeof SPRITES === "undefined" ? null : SPRITES.portraits;
  return icon && portraits && portraits.has(icon)
    ? "/assets/portraits/" + icon + ".png"
    : null;
}

function historySprite(folder, icon) {
  const sprites = typeof SPRITES === "undefined" ? null : SPRITES[folder];
  return icon && sprites && sprites.has(icon)
    ? "/assets/" + folder + "/" + icon + ".png"
    : null;
}

function historyLoadoutIcon(folder, icon, label) {
  const source = historySprite(folder, icon);
  return source
    ? '<img src="' + esc(source) + '" alt="" title="' + esc(label) + '">'
    : "";
}

function historyLoadoutHtml(presence) {
  const devices = presence.devices || [];
  const blocks = Array.from({ length: 6 }, (_, index) => {
    const slot = index + 1;
    const device = devices.find((item) => Number(item.slot) === slot);
    const label = device
      ? "Slot " +
        slot +
        ": " +
        device.device +
        (device.level != null ? " (Lv" + device.level + ")" : "")
      : "Slot " + slot + ": empty";
    return (
      '<div class="history-kit-slot' +
      (device ? "" : " empty") +
      '" title="' +
      esc(label) +
      '">' +
      historyLoadoutIcon("devices", device?.icon, label) +
      "<span>" +
      slot +
      "</span></div>"
    );
  }).join("");
  const perks = Array.from({ length: 3 }, (_, index) => {
    const perk = (presence.perks || []).find(
      (item) => Number(item.slot) === index,
    );
    const label = perk ? perk.perk : "Empty perk slot";
    return (
      '<div class="history-kit-slot history-perk-slot' +
      (perk ? "" : " empty") +
      '" title="' +
      esc(label) +
      '">' +
      historyLoadoutIcon("perks", perk?.icon, label) +
      "</div>"
    );
  }).join("");
  return (
    '<div class="history-loadout"><div class="history-kit-group"><span>Blocks</span><div class="history-kit-row">' +
    blocks +
    '</div></div><div class="history-kit-group"><span>Perks</span><div class="history-kit-row history-perk-row">' +
    perks +
    "</div></div></div>"
  );
}

function formatHistoryElapsed(timestamp, startedAt) {
  return formatHistoryDuration(
    Math.max(0, (Number(timestamp) - Number(startedAt)) / 1000),
  );
}

function presenceHtml(presence, startedAt) {
  const end =
    presence.left_at == null
      ? "match end"
      : formatHistoryElapsed(presence.left_at, startedAt);
  const leave = presence.leave_kind ? " · " + presence.leave_kind : "";
  return (
    '<div class="history-presence"><b>' +
    esc(presence.join_kind) +
    "</b> · " +
    formatHistoryElapsed(presence.joined_at, startedAt) +
    " – " +
    end +
    leave +
    '<div class="history-loadout">' +
    esc(presence.hero || "Unknown hero") +
    (presence.skin ? " · " + esc(presence.skin) : "") +
    "</div>" +
    historyLoadoutHtml(presence) +
    "</div>"
  );
}

function historyPlayerHtml(player, startedAt) {
  const presence = latestPresence(player);
  const portrait = historyPortrait(presence?.hero_icon);
  const k = stat(player, "Kill"),
    d = stat(player, "Death"),
    a = stat(player, "Assist");
  const markers = [
    player.was_initial ? '<span class="history-marker">Initial</span>' : "",
    player.was_backfiller
      ? '<span class="history-marker backfill">Backfill</span>'
      : "",
  ].join("");
  const presences = player.presences || [];
  return (
    '<article class="history-player"><div class="history-player-top"><div><div class="history-player-identity">' +
    (portrait
      ? '<img class="history-player-portrait" src="' +
        esc(portrait) +
        '" alt="">'
      : "") +
    '<div><div class="history-player-name">' +
    playerLink(player.nickname, player.player_id) +
    '</div><div class="history-player-hero">' +
    esc(presence?.hero || "No recorded presence") +
    (presence?.skin ? " · " + esc(presence.skin) : "") +
    "</div>" +
    (markers ? '<div class="history-markers">' + markers + "</div>" : "") +
    '</div></div></div><div class="history-player-stats">' +
    "<b>" +
    player.total_score +
    "</b> score<br>" +
    k +
    " / " +
    d +
    " / " +
    a +
    " K/D/A" +
    historyMmrHtml(player) +
    "</div></div>" +
    (presences.length
      ? '<details class="history-presences"><summary>' +
        presences.length +
        " presence" +
        (presences.length === 1 ? "" : "s") +
        " and loadout</summary>" +
        presences
          .map((presence) => presenceHtml(presence, startedAt))
          .join("") +
        "</details>"
      : "") +
    "</article>"
  );
}

/* The archive records the smaller line cubes and the main/base cube separately. Map
   metadata tells us whether a base exists, so report the physical cube total a player
   sees (3/3 on Mountain Express, 1/1 on base-only maps) instead of leaking that
   storage split into the panel. If a historical map cannot be resolved, retain only
   the facts stored in the archive rather than guessing it had a main cube. */
function historyObjective(detail, team) {
  const smallAtStart = Number(team.cubes_at_start) || 0;
  const smallRemaining = Number(team.cubes_remaining) || 0;
  // The archive records the main/base cube independently through `base_destroyed`.
  // Do not rely on the current map catalogue here: archived maps can be removed or,
  // like this map-edit variant, have no CardMap entry at all.
  const hasMainCube = Object.hasOwn(team, "base_destroyed");
  if (hasMainCube) {
    const totalAtStart = smallAtStart + 1;
    const totalRemaining = smallRemaining + (team.base_destroyed ? 0 : 1);
    return totalRemaining + "/" + totalAtStart + " cubes remained";
  }
  return [
    smallAtStart > 0
      ? smallRemaining + "/" + smallAtStart + " cubes remained"
      : "",
    team.base_destroyed ? "base destroyed" : "",
  ]
    .filter(Boolean)
    .join(" · ");
}

// These are the labels used by the stock client’s Zone UI: its Team1 panel says
// “Team 1”, while the panel bound to Team2 says “Team A”. Keep the archive's
// TeamType value for grouping; this only mirrors the client-facing wording.
function historyTeamLabel(team) {
  return team === "Team1"
    ? "Team 1"
    : team === "Team2"
      ? "Team A"
      : archiveLabel(team);
}

function renderHistoryDetail(detail) {
  return (
    '<div class="history-teams">' +
    (detail.teams || [])
      .map((team) => {
        // Highest score first, matching the in-game match history scoreboard.
        const players = (detail.players || [])
          .filter((player) => latestPresence(player)?.team === team.team)
          .sort(
            (a, b) =>
              Number(b.total_score || 0) - Number(a.total_score || 0) ||
              String(a.nickname || "").localeCompare(String(b.nickname || "")),
          );
        const objective = historyObjective(detail, team);
        return (
          '<section class="history-team"><header class="history-team-head"><span class="history-team-name' +
          (team.is_winner ? " winner" : "") +
          '">' +
          esc(historyTeamLabel(team.team)) +
          (team.is_winner ? " · winner" : "") +
          "</span>" +
          (objective
            ? '<span class="history-team-objective">' +
              esc(objective) +
              "</span>"
            : "") +
          "</header>" +
          (players.length
            ? players
                .map((player) => historyPlayerHtml(player, detail.started_at))
                .join("")
            : '<p class="history-detail-loading">No final roster recorded.</p>') +
          "</section>"
        );
      })
      .join("") +
    "</div>"
  );
}

registerView("history", { enter: () => loadHistory() });
