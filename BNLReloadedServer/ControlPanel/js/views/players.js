/* ---------- players ---------- */

async function loadPlayers() {
  const body = document.getElementById("playersBody");
  if (!allPlayers.length)
    body.innerHTML = '<tr class="empty-row"><td colspan="7">Loading…</td></tr>';
  try {
    const res = await fetch("/api/players");
    if (!res.ok) throw new Error("request failed");
    applyPlayers(await res.json());
  } catch {
    body.innerHTML =
      '<tr class="empty-row error-row"><td colspan="7">Failed to load players.</td></tr>';
  }
}

function applyPlayers(data) {
  allPlayers = data.players || [];
  document.getElementById("railPlayers").textContent = allPlayers.length || "";
  const banPlayerNames = document.getElementById("banPlayerNames");
  if (banPlayerNames) {
    banPlayerNames.innerHTML = allPlayers
      .map((p) => '<option value="' + esc(p.nickname) + '"></option>')
      .join("");
  }
  filterPlayers();
  if (!mmrEditing) {
    buildMmrRows();
    renderMmr();
  }
  renderModeration();
}

let sortKey = "presence",
  sortDir = -1; // -1 desc, 1 asc

/* Numeric and ordinal columns open descending — newest ID, highest role, longest online —
   because that's the answer you wanted when you clicked. Text opens A-Z. */
const SORT_OPENS_ASC = { nickname: true };

/* Presence runs from longest-online to longest-offline: online presence starts earliest
   first, then offline presence ends most recently first. Missing timestamps are last. */
function comparePresence(a, b) {
  if (a.online !== b.online) return a.online ? 1 : -1;

  const aTimestamp = a.online ? a.online_since : a.last_online;
  const bTimestamp = b.online ? b.online_since : b.last_online;
  if (aTimestamp == null || bTimestamp == null)
    return aTimestamp == null ? (bTimestamp == null ? 0 : -1) : 1;

  return a.online ? bTimestamp - aTimestamp : aTimestamp - bTimestamp;
}

/* Queue sorting answers the operational question first: who is online but has not
   joined a queue? Queued players follow, then unavailable and offline entries. */
function compareQueue(a, b) {
  const rank = (p) =>
    isNotQueued(p) ? 3 : p.queue_mode ? 2 : p.online ? 1 : 0;
  const rankDifference = rank(a) - rank(b);
  if (rankDifference !== 0) return rankDifference;
  return (a.queue_mode || "").localeCompare(b.queue_mode || "", undefined, {
    sensitivity: "base",
  });
}

const COMPARE = {
  id: (a, b) => a.id - b.id,
  nickname: (a, b) =>
    a.nickname.localeCompare(b.nickname, undefined, {
      sensitivity: "base",
      numeric: true,
    }),
  role: (a, b) => a.role_id - b.role_id, // by rank, not alphabetically
  presence: comparePresence,
  queue: compareQueue,
};

function formatElapsed(milliseconds) {
  const seconds = Math.max(0, Math.floor(milliseconds / 1000));
  if (seconds < 60) return "less than a minute";
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return minutes + " minute" + (minutes === 1 ? "" : "s");
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return hours + " hour" + (hours === 1 ? "" : "s");
  const days = Math.floor(hours / 24);
  return days + " day" + (days === 1 ? "" : "s");
}

function presenceLabel(p) {
  if (p.online && p.online_since)
    return "Online for " + formatElapsed(Date.now() - p.online_since);
  if (!p.online && p.last_online)
    return "Last online " + formatElapsed(Date.now() - p.last_online) + " ago";
  return p.online ? "Online" : "Offline";
}

function isNotQueued(p) {
  return p.online && p.queue_available && !p.queue_mode;
}

function queueLabel(p) {
  if (p.queue_mode)
    return (
      '<span class="queue-status queued">' +
      esc(p.queue_mode) +
      (p.queue_confirming ? " · confirming" : "") +
      "</span>"
    );
  if (!p.online) return '<span class="queue-status">Offline</span>';
  if (!p.queue_available)
    return '<span class="queue-status unavailable">Queue unavailable</span>';
  return '<span class="queue-status">Not queued</span>';
}

function updatePresenceLabels() {
  document
    .querySelectorAll("[data-presence-online-since]")
    .forEach((element) => {
      element.textContent = presenceLabel({
        online: element.dataset.presenceOnline === "true",
        online_since: Number(element.dataset.presenceOnlineSince) || null,
        last_online: Number(element.dataset.presenceLastOnline) || null,
      });
    });
}

function setSort(key) {
  if (sortKey === key) sortDir = -sortDir;
  else {
    sortKey = key;
    sortDir = SORT_OPENS_ASC[key] ? 1 : -1;
  }
  document.querySelectorAll(".th-sort").forEach((b) => {
    const active = b.dataset.key === sortKey;
    b.dataset.dir = active ? (sortDir < 0 ? "desc" : "asc") : "none";
    b.closest("th").setAttribute(
      "aria-sort",
      active ? (sortDir < 0 ? "descending" : "ascending") : "none",
    );
  });
  filterPlayers();
}

/* Ties fall back to player ID ascending regardless of direction, so equal roles and
   matching presence times keep a stable order instead of shuffling between renders — and
   this table re-renders on every poll. */
function sortPlayers(players) {
  if (!sortKey) return players;
  const cmp = COMPARE[sortKey];
  return players.slice().sort((a, b) => {
    const d = cmp(a, b);
    return d !== 0 ? d * sortDir : a.id - b.id;
  });
}

function renderPlayers(list) {
  const players = sortPlayers(list);
  const body = document.getElementById("playersBody");
  if (!players.length) {
    body.innerHTML =
      '<tr class="empty-row"><td colspan="7">No players match that filter.</td></tr>';
    return;
  }
  body.innerHTML = players
    .map(
      (p) =>
        "<tr>" +
        "<td>" +
        p.id +
        "</td>" +
        '<td><a class="steam-btn" href="https://steamcommunity.com/profiles/' +
        encodeURIComponent(p.steam_id) +
        '" target="_blank" rel="noopener noreferrer"' +
        ' title="Open Steam profile">' +
        esc(p.nickname) +
        "</a></td>" +
        '<td><span class="status-dot ' +
        (p.online ? "online" : "") +
        '" data-presence-online="' +
        p.online +
        '" data-presence-online-since="' +
        (p.online_since || "") +
        '" data-presence-last-online="' +
        (p.last_online || "") +
        '">' +
        esc(presenceLabel(p)) +
        "</span></td>" +
        "<td>" +
        (p.online
          ? esc(p.game_location || "Menu")
          : '<span class="queue-status">Offline</span>') +
        "</td>" +
        "<td>" +
        queueLabel(p) +
        "</td>" +
        '<td><span class="role-badge role-' +
        esc(p.role) +
        '">' +
        esc(p.role) +
        "</span></td>" +
        '<td class="row-action">' +
        (window.controlPanelAdmin && p.online
          ? '<button class="message-btn" onclick="sendPlayerMessage(' +
            p.id +
            ')">Message</button> '
          : window.controlPanelAdmin
            ? '<button class="message-btn" onclick="schedulePlayerMessage(' +
              p.id +
              ')">Schedule message</button> '
            : "") +
        '<button class="edit-btn" onclick="showPlayerEdit(' +
        p.id +
        ')">' +
        (window.controlPanelAdmin ? "Edit" : "View") +
        "</button>" +
        "</td>" +
        "</tr>",
    )
    .join("");
  updatePresenceLabels();
}

async function sendPlayerMessage(id) {
  openMessageModal(id, false);
}

async function schedulePlayerMessage(id) {
  openMessageModal(id, true);
}

function openBroadcastMessage() {
  currentMessagePlayerId = null;
  currentMessageNickname = "";
  currentMessageScheduled = false;
  currentMessageBroadcast = true;
  messageSelection = { start: 0, end: 0 };
  document.getElementById("messageModalTitle").textContent =
    "Broadcast announcement";
  document.getElementById("messageModalTarget").textContent =
    "To all currently online players";
  document.getElementById("messageText").value = "";
  updateMessagePreview();
  document.getElementById("messageModalBackdrop").classList.add("active");
  document.getElementById("messageModal").classList.add("active");
  document.getElementById("messageText").focus();
}

function openMessageModal(id, scheduled) {
  const player = allPlayers.find((p) => p.id === id);
  const nickname = player ? player.nickname : "player #" + id;
  currentMessagePlayerId = id;
  currentMessageNickname = nickname;
  currentMessageScheduled = scheduled;
  currentMessageBroadcast = false;
  messageSelection = { start: 0, end: 0 };
  document.getElementById("messageModalTitle").textContent = scheduled
    ? "Schedule announcement"
    : "Send announcement";
  document.getElementById("messageModalTarget").textContent = scheduled
    ? "Will be delivered when " + nickname + " comes online."
    : "To " + nickname + " (#" + id + ")";
  document.getElementById("messageText").value = "";
  updateMessagePreview();
  document.getElementById("messageModalBackdrop").classList.add("active");
  document.getElementById("messageModal").classList.add("active");
  document.getElementById("messageText").focus();
}

let currentMessagePlayerId = null;
let currentMessageNickname = "";
let currentMessageScheduled = false;
let currentMessageBroadcast = false;
let messageSelection = { start: 0, end: 0 };

function closeMessageModal() {
  currentMessagePlayerId = null;
  currentMessageNickname = "";
  currentMessageScheduled = false;
  currentMessageBroadcast = false;
  document.getElementById("messageModalBackdrop").classList.remove("active");
  document.getElementById("messageModal").classList.remove("active");
}

function rememberMessageSelection() {
  const input = document.getElementById("messageText");
  messageSelection = { start: input.selectionStart, end: input.selectionEnd };
}

function wrapMessageSelection(tag, value) {
  const input = document.getElementById("messageText");
  const hasCurrentSelection = input.selectionStart !== input.selectionEnd;
  const start = hasCurrentSelection
    ? input.selectionStart
    : messageSelection.start;
  const end = hasCurrentSelection ? input.selectionEnd : messageSelection.end;
  if (start === end) return;
  const selected = input.value.slice(start, end);
  const attribute = value ? "=" + value : "";
  input.setRangeText(
    "<" + tag + attribute + ">" + selected + "</" + tag + ">",
    start,
    end,
    "select",
  );
  input.focus();
  messageSelection = {
    start,
    end:
      start +
      ("<" + tag + attribute + ">" + selected + "</" + tag + ">").length,
  };
  updateMessagePreview();
  if (tag === "size") document.getElementById("messageSize").value = "";
}

function sanitizeMessageMarkup(raw) {
  const escaped = raw.replace(
    /[&<>"']/g,
    (character) =>
      ({
        "&": "&amp;",
        "<": "&lt;",
        ">": "&gt;",
        '"': "&quot;",
        "'": "&#39;",
      })[character],
  );
  return escaped
    .replace(/&lt;b&gt;/gi, "<b>")
    .replace(/&lt;\/b&gt;/gi, "</b>")
    .replace(/&lt;i&gt;/gi, "<i>")
    .replace(/&lt;\/i&gt;/gi, "</i>")
    .replace(/&lt;br\s*\/?&gt;/gi, "<br>")
    .replace(/&lt;color=(#[0-9a-f]{6,8})&gt;/gi, '<span style="color:$1">')
    .replace(/&lt;\/color&gt;/gi, "</span>")
    .replace(
      /&lt;size=(1[0-9]|2[0-9]|3[0-9])&gt;/gi,
      '<span style="font-size:$1px">',
    )
    .replace(/&lt;\/size&gt;/gi, "</span>");
}

function updateMessagePreview() {
  const text = document.getElementById("messageText").value;
  document.getElementById("messagePreview").innerHTML = sanitizeMessageMarkup(
    text,
  ).replace(/\n/g, "<br>");
}

async function submitPlayerMessage() {
  if (currentMessagePlayerId == null && !currentMessageBroadcast) return;
  const message = document.getElementById("messageText").value.trim();
  if (!message) {
    showToast("error", "Enter a message first.");
    return;
  }

  try {
    const endpoint = currentMessageBroadcast
      ? "/api/notification/broadcast"
      : currentMessageScheduled
        ? "/api/players/" + currentMessagePlayerId + "/notification/schedule"
        : "/api/players/" + currentMessagePlayerId + "/notification";
    const res = await fetch(endpoint, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ message }),
    });
    const data = await res.json();
    if (!res.ok) {
      showToast("error", data.error || "Could not send message");
      loadPlayers();
      return;
    }
    const nickname = currentMessageNickname;
    const scheduled = currentMessageScheduled;
    const broadcast = currentMessageBroadcast;
    closeMessageModal();
    showToast(
      "success",
      broadcast
        ? "Broadcast sent to " + (data.sent || 0) + " online players."
        : scheduled
          ? "Message scheduled for " + nickname + "."
          : "Message sent to " + nickname + ".",
    );
  } catch (e) {
    showToast("error", e.message);
  }
}

function filterPlayers() {
  const q = document.getElementById("searchInput").value.toLowerCase();
  const filtered = allPlayers.filter(
    (p) =>
      p.nickname.toLowerCase().includes(q) ||
      String(p.id).includes(q) ||
      String(p.steam_id).includes(q) ||
      (p.region || "").toLowerCase().includes(q),
  );
  renderPlayers(filtered);
  // Only says anything while a filter is narrowing things — the total lives in the rail.
  document.getElementById("playerCount").textContent =
    filtered.length === allPlayers.length
      ? ""
      : filtered.length + " of " + allPlayers.length + " players";
}

/* ---------- drawer ---------- */

let returnFocusTo = null;

function showPlayerEdit(id) {
  currentPlayerId = id;
  returnFocusTo = document.activeElement; // the Edit button that opened it
  document.getElementById("view-player").classList.add("active");
  document.getElementById("overlay").classList.add("active");
  loadPlayer(id);
  document.getElementById("view-player").focus();
}

function closePlayerEdit() {
  currentPlayerId = null;
  const view = document.getElementById("view-player");
  view.classList.remove("active");
  document.getElementById("overlay").classList.remove("active");
  // blur first: if the opener has gone (or was never focusable) focus would otherwise
  // stay parked on a drawer that is now hidden
  view.blur();
  if (returnFocusTo && document.contains(returnFocusTo)) returnFocusTo.focus();
  returnFocusTo = null;
}

async function loadPlayer(id) {
  document.getElementById("playerTitle").textContent = "Player #" + id;
  const playerRole = document.getElementById("playerRole");
  const playerSub = document.getElementById("playerSub");
  if (playerRole) playerRole.innerHTML = "";
  if (playerSub) playerSub.innerHTML = "";
  try {
    const res = await fetch("/api/players/" + id);
    if (!res.ok) throw new Error("Player not found");
    const p = await res.json();
    if (currentPlayerId !== id) return; // drawer moved on while we waited

    document.getElementById("playerTitle").textContent = p.nickname;
    if (window.controlPanelAdmin) {
      playerRole.innerHTML =
        '<span class="role-badge role-' +
        esc(p.role) +
        '">' +
        esc(p.role) +
        "</span>";
      playerSub.innerHTML =
        '<span class="status-dot ' +
        (p.online ? "online" : "") +
        '" data-presence-online="' +
        p.online +
        '" data-presence-online-since="' +
        (p.online_since || "") +
        '" data-presence-last-online="' +
        (p.last_online || "") +
        '">' +
        esc(presenceLabel(p)) +
        "</span>";
      updatePresenceLabels();
      document.getElementById("f-id").value = p.id;
      document.getElementById("f-steam").value = p.steam_id;
      document.getElementById("f-nickname").value = p.nickname;
      document.getElementById("f-role").value = p.role_id;
      loadedRoleId = String(p.role_id);
      roleChanged();

      const t = Date.now();
      const mm = document.getElementById("mmBanStatus");
      const mmBanned = p.matchmaker_ban_end != null && p.matchmaker_ban_end > t;
      mm.textContent = mmBanned
        ? "Until " + fmtUntil(p.matchmaker_ban_end)
        : "Not banned";
      mm.className = "ban-status " + (mmBanned ? "banned" : "clear");

      const gy = document.getElementById("gyBanStatus");
      const perm = p.graveyard_permanent === true;
      const gyBanned =
        perm || (p.graveyard_leave_time != null && p.graveyard_leave_time > t);
      gy.textContent = perm
        ? "Permanent"
        : gyBanned
          ? "Until " + fmtUntil(p.graveyard_leave_time)
          : "Not banned";
      gy.className = "ban-status " + (gyBanned ? "banned" : "clear");
    }

    renderProfile(p.badges, p.badge_icons);
    renderLoadouts(p.loadouts || []);
    if (window.controlPanelAdmin) loadInventory(id);
  } catch (e) {
    showToast("error", "Failed to load player: " + e.message);
  }
}

/* ---------- inventory (admin) ---------- */

const INVENTORY_CATEGORY = {
  Unit: "heroes",
  Skin: "skins",
  Device: "devices",
  Perk: "perks",
  Badge: "badges",
};

async function loadInventory(id) {
  const counts = document.getElementById("invCounts");
  const list = document.getElementById("invPrivate");
  const stale = document.getElementById("invStale");
  counts.textContent = "Loading…";
  list.innerHTML = "";
  stale.innerHTML = "";
  try {
    const res = await fetch("/api/players/" + id + "/inventory");
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || res.statusText);
    if (currentPlayerId !== id) return; // drawer moved on while we waited
    renderInventory(data);
  } catch (e) {
    counts.textContent = "Inventory unavailable: " + e.message;
  }
}

function grantLine(g) {
  const when = fmtUntil(Date.parse(g.granted_at));
  return (
    "Granted " +
    esc(when) +
    " by " +
    esc(g.granted_by) +
    (g.note ? " · " + esc(g.note) : "")
  );
}

// Card ids and names only ever go into attributes, never inline script, so catalogue text cannot break out.
function attr(s) {
  return esc(s).replace(/"/g, "&quot;");
}

function invButton(cls, action, item, name, label) {
  return (
    '<button class="' +
    cls +
    '" data-inv-action="' +
    action +
    '" data-inv-item="' +
    attr(item) +
    '" data-inv-name="' +
    attr(name) +
    '">' +
    label +
    "</button>"
  );
}

document.getElementById("inventoryCard")?.addEventListener("click", (e) => {
  const b = e.target.closest("button[data-inv-action]");
  if (b) changeInventory(b.dataset.invItem, b.dataset.invAction, b.dataset.invName);
});

function renderInventory(data) {
  const counts = Object.entries(data.owned_counts || {})
    .map(([cat, n]) => n + " " + (INVENTORY_CATEGORY[cat] || cat.toLowerCase()))
    .join(" · ");
  document.getElementById("invCounts").textContent =
    "Owns " + (counts || "nothing") + ".";

  const items = data.private_items || [];
  document.getElementById("invPrivate").innerHTML = items.length
    ? items
        .map((i) => {
          const kind =
            (i.hero ? esc(i.hero) + " " : "") +
            esc((INVENTORY_CATEGORY[i.category] || i.category).replace(/s$/, ""));
          const detail = i.grant
            ? grantLine(i.grant)
            : i.owned
              ? "Owned"
              : "Not owned";
          const action = i.grant
            ? invButton("ban-btn", "revoke", i.id, i.name, "Revoke")
            : invButton("save-btn", "grant", i.id, i.name, "Grant");
          return (
            '<div class="inv-row' +
            (i.owned ? " owned" : "") +
            '"><div class="inv-item"><span class="inv-name">' +
            esc(i.name) +
            '</span><span class="inv-kind">' +
            kind +
            " · <code>" +
            esc(i.id) +
            '</code></span><span class="inv-detail">' +
            detail +
            "</span></div>" +
            action +
            "</div>"
          );
        })
        .join("")
    : '<p class="inv-empty">No private items exist. Mark a card <code>"scope": "private"</code> in the catalogue to make it grantable.</p>';

  const stale = data.stale_grants || [];
  document.getElementById("invStale").innerHTML = stale.length
    ? '<span class="ident-label">Grants for items no longer private or in the catalogue</span>' +
      stale
        .map(
          (g) =>
            '<div class="inv-row"><div class="inv-item"><span class="inv-name"><code>' +
            esc(g.item) +
            '</code></span><span class="inv-detail">' +
            grantLine(g) +
            "</span></div>" +
            invButton("ban-btn", "revoke", g.item, g.item, "Revoke") +
            "</div>",
        )
        .join("")
    : "";
}

async function changeInventory(item, action, name) {
  const id = currentPlayerId;
  if (id == null) return;
  if (action === "revoke" && !confirm("Revoke " + (name || item) + " from this player?"))
    return;
  const noteInput = document.getElementById("invNote");
  const body = { item, action };
  if (action === "grant" && noteInput.value.trim()) body.note = noteInput.value.trim();
  try {
    const res = await fetch("/api/players/" + id + "/inventory", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || data.result || res.statusText);
    const done = { Granted: "Granted.", Revoked: "Revoked.", Unchanged: "Nothing to change." };
    showToast("success", done[data.result] || data.result);
    if (action === "grant") noteInput.value = "";
  } catch (e) {
    showToast("error", "Inventory change failed: " + e.message);
  }
  if (currentPlayerId === id) loadInventory(id);
}

let currentLoadouts = [];

/* Every sprite the API names, it names by the card's CDB `icon`. sprites.js lists what
   this panel actually ships; anything not in it draws an empty tile rather than a
   broken image, so a catalogue update costs a missing icon and nothing else. */
const SPRITE_INDEX = typeof SPRITES === "undefined" ? {} : SPRITES;

function sprite(folder, icon) {
  const set = SPRITE_INDEX[folder];
  return icon && set && set.has(icon)
    ? "/assets/" + folder + "/" + icon + ".png"
    : null;
}

/* The three profile slots. `badges` arrives keyed by BadgeType with card names in it and
   `badge_icons` maps those names to sprites — the shape /api/players/{id} sends. The
   caps decide how many slots are drawn, so an unfilled one is a dashed box rather than
   a shorter row. */
const BADGE_CAPS = { Title: 1, Border: 1, Icon: 3 }; // global_logic.max_badges_by_type

function renderProfile(badges, badgeIcons) {
  const b = badges || {};
  const title = (b.Title || [])[0];
  const border = (b.Border || [])[0];
  const icons = (b.Icon || []).slice(0, BADGE_CAPS.Icon);
  const artOf = (folder, name) =>
    sprite(folder, name ? (badgeIcons || {})[name] : null);

  const titleEl = document.getElementById("profileTitle");
  titleEl.textContent = title || "No title selected";
  titleEl.classList.toggle("profile-none", !title);

  /* Four rotated copies of the one corner sprite; CSS puts them in the corners. */
  const art = artOf("borders", border);
  const emblem = document.getElementById("profileBorder");
  emblem.classList.toggle("empty", !art);
  emblem.innerHTML = art
    ? '<img src="' +
      art +
      '" alt=""><img src="' +
      art +
      '" alt="">' +
      '<img src="' +
      art +
      '" alt=""><img src="' +
      art +
      '" alt="">'
    : "";
  const borderName = document.getElementById("profileBorderName");
  borderName.textContent = border || "None selected";
  borderName.classList.toggle("profile-none", !border);

  document.getElementById("profileBadgeCap").textContent =
    icons.length + " / " + BADGE_CAPS.Icon;
  document.getElementById("profileBadges").innerHTML = Array.from(
    { length: BADGE_CAPS.Icon },
    (_, i) => {
      const src = artOf("badges", icons[i]);
      return (
        '<div class="badge-slot' +
        (src ? "" : " empty") +
        '"' +
        (icons[i] ? ' title="' + esc(icons[i]) + '"' : "") +
        ">" +
        (src ? '<img src="' + src + '" alt="' + esc(icons[i]) + '">' : "") +
        "</div>"
      );
    },
  ).join("");
}

function renderLoadouts(loadouts) {
  currentLoadouts = loadouts;
  const select = document.getElementById("f-loadout-select");
  select.innerHTML = loadouts
    .map((l, i) => '<option value="' + i + '">' + esc(l.hero) + "</option>")
    .join("");
  renderHeroTabs();
  renderSelectedLoadout();
}

/* The visible picker. It writes to the hidden <select> and re-renders, so the selection
   still lives in one place. */
function renderHeroTabs() {
  const sel = document.getElementById("f-loadout-select");
  document.getElementById("loadoutTabs").innerHTML = currentLoadouts
    .map((l, i) => {
      const face = sprite("portraits", l.hero_icon);
      return (
        '<button class="hero-tab" type="button" role="tab" data-i="' +
        i +
        '"' +
        ' aria-selected="' +
        (String(i) === sel.value) +
        '" onclick="pickHero(' +
        i +
        ')">' +
        (face ? '<img src="' + face + '" alt="">' : "") +
        esc(l.hero) +
        "</button>"
      );
    })
    .join("");
}

function pickHero(i) {
  document.getElementById("f-loadout-select").value = String(i);
  renderHeroTabs();
  renderSelectedLoadout();
}

const BLOCK_SLOTS = 6,
  PERK_SLOTS = 3;

function renderSelectedLoadout() {
  const detail = document.getElementById("loadoutDetail");
  const l =
    currentLoadouts[
      parseInt(document.getElementById("f-loadout-select").value)
    ];
  document.getElementById("loadoutSkin").textContent =
    l && l.skin ? "Skin: " + l.skin : "";
  if (!l) {
    detail.innerHTML = '<p class="ban-status clear">No loadouts</p>';
    return;
  }

  const icon = (folder, iconName, label, cls) => {
    const src = sprite(folder, iconName);
    return src
      ? '<img class="kit-icon ' +
          cls +
          '" src="' +
          src +
          '" alt="" title="' +
          esc(label) +
          '">'
      : '<span class="kit-icon ' + cls + ' empty"></span>';
  };

  /* Fixed slot count: an unused slot is drawn, not skipped, so every loadout is the
     same shape and slot 4 is slot 4 for everyone. */
  const devices = (l.devices || []).filter(
    (d) => d.name && d.name !== "(empty)",
  );
  const blocks = Array.from({ length: BLOCK_SLOTS }, (_, i) => {
    const d = devices.find((x) => x.slot === i + 1);
    return (
      '<div class="slot-tile' +
      (d ? "" : " empty") +
      '">' +
      icon("devices", d && d.icon, d && d.name, "dev") +
      '<span class="slot-name">' +
      (d
        ? esc(d.name) +
          (d.variant
            ? ' <span class="device-variant">' + esc(d.variant) + "</span>"
            : "")
        : "Empty slot") +
      "</span></div>"
    );
  }).join("");

  /* Fixed slot order, not the order the API happens to return them in — the same perk
     type should sit in the same place for every character. */
  const PERK_ORDER = { Defensive: 0, Offensive: 1, Hero: 2 };
  const held = (l.perks || [])
    .slice()
    .sort(
      (a, b) => (PERK_ORDER[a.slot_type] ?? 9) - (PERK_ORDER[b.slot_type] ?? 9),
    );
  const perks = Array.from({ length: PERK_SLOTS }, (_, i) => held[i] || null)
    .map((pk) => {
      const empty = !pk || pk.name === "(empty)";
      return (
        '<div class="perk-tile">' +
        icon("perks", empty ? null : pk.icon, empty ? null : pk.name, "perk") +
        '<div class="perk-body">' +
        (pk && pk.slot_type
          ? '<span class="perk-badge perk-badge-' +
            esc(pk.slot_type) +
            '">' +
            esc(pk.slot_type) +
            "</span>"
          : "") +
        '<div class="perk-name' +
        (empty ? " perk-empty" : "") +
        '">' +
        (empty ? "Empty slot" : esc(pk.name)) +
        "</div>" +
        (pk && pk.upside
          ? '<div class="perk-upside">' + esc(pk.upside) + "</div>"
          : "") +
        (pk && pk.downside
          ? '<div class="perk-downside">' + esc(pk.downside) + "</div>"
          : "") +
        "</div></div>"
      );
    })
    .join("");

  const section = (title, cls, body) =>
    '<div class="loadout-section loadout-section-' +
    cls +
    '">' +
    '<div class="loadout-section-title">' +
    title +
    "</div>" +
    body +
    "</div>";

  detail.innerHTML =
    '<div class="loadout-cols">' +
    section(
      "Block loadout",
      "blocks",
      '<div class="slot-grid">' + blocks + "</div>",
    ) +
    section("Selected perks", "perks", perks) +
    "</div>";
}

/* ---------- role ---------- */

let loadedRoleId = null;

/* A role change is pending until it is applied — same contract as the ladder, so
   "nothing is saved until you say so" holds everywhere in the panel. */
function roleChanged() {
  const sel = document.getElementById("f-role");
  const dirty = sel.value !== loadedRoleId;
  document.getElementById("rolePending").hidden = !dirty;
  if (dirty) {
    const was = [...sel.options].find((o) => o.value === loadedRoleId);
    document.getElementById("roleWas").textContent = was ? was.text : "";
  }
}

function discardRole() {
  document.getElementById("f-role").value = loadedRoleId;
  roleChanged();
}

async function saveRole() {
  const sel = document.getElementById("f-role");
  const id = currentPlayerId;
  const ok = await postPlayer(
    id,
    { role_id: parseInt(sel.value) },
    "Role saved.",
  );
  if (!ok) return;
  loadedRoleId = sel.value;
  roleChanged();
  await loadPlayers();
  if (currentPlayerId === id) loadPlayer(id);
}
