/* ---------- trophies ---------- */

/* Admin-awarded trophies. Everyone sees the catalogue; admins create trophies (with their
   PNG art), award them to players and revoke individual awards. Players choose which of
   their trophies to showcase from the game client. */

let trophyCatalogue = [];
let trophyGrants = [];

registerView("trophies", { enter: () => loadTrophies() });

async function loadTrophies() {
  try {
    const res = await fetch("/api/trophies", { cache: "no-store" });
    trophyCatalogue = (await res.json()).trophies || [];
  } catch (e) {
    trophyCatalogue = [];
    showToast("error", "Trophies could not be loaded: " + e.message);
  }
  if (window.controlPanelAdmin) {
    try {
      const res = await fetch("/api/trophies/grants", { cache: "no-store" });
      trophyGrants = res.ok ? (await res.json()).grants || [] : [];
    } catch (e) {
      trophyGrants = [];
    }
  }
  renderTrophies();
}

function trophyName(id) {
  const t = trophyCatalogue.find((x) => x.id === id);
  return t ? t.name : id;
}

function renderTrophies() {
  const rail = document.getElementById("railTrophies");
  if (rail) rail.textContent = trophyCatalogue.length || "";
  document.getElementById("trophyGrid").innerHTML = trophyCatalogue.length
    ? trophyCatalogue
        .map(
          (t) =>
            '<div class="trophy-card' + (t.retired ? " retired" : "") + '">' +
            '<img class="trophy-art" alt="" src="' + esc(t.image) + '" />' +
            '<div class="trophy-info"><strong>' + esc(t.name) + "</strong>" +
            '<code>' + esc(t.id) + "</code>" +
            (t.retired ? '<span class="pen-badge">Retired</span>' : "") +
            "<p>" + esc(t.description || "") + "</p>" +
            (window.controlPanelAdmin
              ? '<div class="trophy-actions">' +
                '<button onclick="editTrophy(\'' + esc(t.id) + "')\">Edit</button>" +
                '<button onclick="retireTrophy(\'' + esc(t.id) + "'," + !t.retired + ')">' +
                (t.retired ? "Restore" : "Retire") + "</button></div>"
              : "") +
            "</div></div>",
        )
        .join("")
    : '<p class="tool-note">No trophies yet.</p>';

  if (!window.controlPanelAdmin) return;
  const select = document.getElementById("f-trophy-grant-id");
  const chosen = select.value;
  select.innerHTML = trophyCatalogue
    .filter((t) => !t.retired)
    .map((t) => '<option value="' + esc(t.id) + '">' + esc(t.name) + "</option>")
    .join("");
  if (chosen) select.value = chosen;
  document.getElementById("trophyPlayerNames").innerHTML = (typeof allPlayers !== "undefined" ? allPlayers : [])
    .map((p) => '<option value="' + esc(p.nickname) + '"></option>')
    .join("");
  document.getElementById("trophyGrantBody").innerHTML = trophyGrants.length
    ? trophyGrants
        .map(
          (g) =>
            "<tr><td>" + esc(trophyName(g.trophy_id)) + "</td>" +
            "<td>" + (typeof playerLink === "function" ? playerLink(g.nickname || String(g.player_id), g.player_id) : esc(g.nickname)) + "</td>" +
            "<td>" + new Date(g.granted_at).toLocaleString() + "</td>" +
            "<td>" + esc(g.granted_by) + "</td>" +
            "<td>" + esc(g.note || "") + "</td>" +
            '<td><button class="ban-btn" onclick="revokeTrophy(' + g.id + ')">Revoke</button></td></tr>',
        )
        .join("")
    : '<tr><td colspan="6">No trophies awarded yet.</td></tr>';
}

async function trophyPost(path, body, successMsg) {
  if (!window.controlPanelAdmin) return false;
  try {
    const res = await fetch("/api/trophies" + path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    const data = await res.json();
    if (!res.ok) {
      showToast("error", data.error || "Trophy update failed");
      return false;
    }
    showToast("success", successMsg);
    await loadTrophies();
    return true;
  } catch (e) {
    showToast("error", e.message);
    return false;
  }
}

function editTrophy(id) {
  const t = trophyCatalogue.find((x) => x.id === id);
  if (!t) return;
  document.getElementById("f-trophy-id").value = t.id;
  document.getElementById("f-trophy-name").value = t.name;
  document.getElementById("f-trophy-description").value = t.description || "";
  document.getElementById("f-trophy-image").value = "";
  document.getElementById("f-trophy-id").scrollIntoView({ behavior: "smooth", block: "center" });
}

function readFileBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(",")[1] || "");
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}

async function saveTrophy() {
  const id = document.getElementById("f-trophy-id").value.trim().toLowerCase();
  const file = document.getElementById("f-trophy-image").files[0];
  if (file && file.type !== "image/png") {
    showToast("error", "Trophy art must be a PNG.");
    return;
  }
  const body = {
    id,
    name: document.getElementById("f-trophy-name").value,
    description: document.getElementById("f-trophy-description").value,
    image_png: file ? await readFileBase64(file) : null,
  };
  const exists = trophyCatalogue.some((t) => t.id === id);
  if (await trophyPost("", body, exists ? "Trophy updated." : "Trophy created.")) {
    document.getElementById("f-trophy-image").value = "";
  }
}

async function retireTrophy(id, retired) {
  await trophyPost("/retire", { trophy_id: id, retired }, retired ? "Trophy retired." : "Trophy restored.");
}

async function grantTrophy() {
  const q = document.getElementById("f-trophy-player").value.trim();
  const players = typeof allPlayers !== "undefined" ? allPlayers : [];
  const p = players.find((x) => x.nickname.toLowerCase() === q.toLowerCase() || String(x.id) === q);
  const playerId = p ? p.id : /^\d+$/.test(q) ? Number(q) : null;
  if (playerId == null) {
    showToast("error", q ? 'No player matches "' + q + '".' : "Name the player to award.");
    return;
  }
  const trophyId = document.getElementById("f-trophy-grant-id").value;
  if (!trophyId) {
    showToast("error", "Create a trophy first.");
    return;
  }
  const note = document.getElementById("f-trophy-note").value;
  if (await trophyPost("/grant", { player_id: playerId, trophy_id: trophyId, note },
      trophyName(trophyId) + " awarded to " + (p ? p.nickname : "#" + playerId) + ".")) {
    document.getElementById("f-trophy-note").value = "";
  }
}

async function revokeTrophy(grantId) {
  const g = trophyGrants.find((x) => x.id === grantId);
  if (g && !confirm("Revoke " + trophyName(g.trophy_id) + " from " + (g.nickname || "#" + g.player_id) + "?")) return;
  await trophyPost("/revoke", { grant_id: grantId }, "Award revoked.");
}
