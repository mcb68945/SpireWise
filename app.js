"use strict";
// Game versions. `data` points a version without its own run data yet at the newest version that has it.
// When a branch moves (e.g. beta becomes live), change only the labels here.
const BRANCHES = [{ id: "v0.107.1", label: "v0.107 · live", note: "live branch v0.107.1" },
                  { id: "v0.111.0", label: "v0.111", note: "v0.111.0, becomes live with the next update" },
                  { id: "v0.113.0", data: "v0.111.0", label: "v0.113 · beta", note: "beta branch v0.113.0, using v0.111.0 data until enough v0.113 runs are available" }];
const dataDir = () => (BRANCHES.find((b) => b.id === S.branch)?.data) || S.branch;
const CHARS = ["ironclad", "silent", "defect", "regent", "necrobinder"];
const MODES = [{ id: "solo", label: "Solo" }, { id: "coop", label: "Co-op" }];
const cap = (s) => s[0].toUpperCase() + s.slice(1);
const POOL_EXTRA = ["colorless", "curse", "status", "event", "token", "quest"];
const $ = (s) => document.querySelector(s);
const sig = (z) => 1 / (1 + Math.exp(-z));
const pct = (x, d = 1) => (x >= 0 ? "+" : "−") + Math.abs(x * 100).toFixed(d) + "%";

const store = {
  get(k, d) { try { const v = localStorage.getItem("spirewise." + k); return v ? JSON.parse(v) : d; } catch { return d; } },
  set(k, v) { try { localStorage.setItem("spirewise." + k, JSON.stringify(v)); } catch {} },
};

let CARDS = {}, STARTERS = {}, DATA = {};
const S = {
  branch: store.get("branch", BRANCHES[0].id),
  ch: store.get("ch", "ironclad"),
  decks: store.get("decks", {}),
  floor: store.get("floor", 5),
  mode: store.get("mode", "solo"),
  mates: store.get("mates", ["silent"]),
  slots: ["", "", ""],
  fixTab: "remove",
  runTab: store.get("runTab", "win"),
  run: store.get("run", { hp: 60, mhp: 80, gold: 99, relics: 1 }),
  relCtx: store.get("relCtx", "shop"),
  relSlots: ["", "", ""],
  open: new Set(),
};

const deck = () => (S.decks[S.ch] ||= {});
const save = () => { store.set("branch", S.branch); store.set("ch", S.ch); store.set("decks", S.decks); store.set("floor", S.floor); store.set("mode", S.mode); store.set("mates", S.mates); store.set("runTab", S.runTab); store.set("run", S.run); store.set("relCtx", S.relCtx); };
const name = (id) => CARDS[id]?.name || DATA[key()]?.names?.[id] || id.replace(/^CARD\./, "").replace(/_/g, " ").toLowerCase().replace(/\b\w/g, (c) => c.toUpperCase());
const img = (id) => (CARDS[id]?.img ? "img/cards/" + CARDS[id].img : "");
const key = () => S.branch + "/" + S.ch + "/" + S.mode;
const LINEUPS = {}, DEC = {};
async function loadDecisions() {
  const k = dataDir() + "/" + S.ch;
  if (!(k in DEC)) { try { DEC[k] = await (await fetch(`data/${dataDir()}/${S.ch}.decisions.json`)).json(); } catch { DEC[k] = null; } }
  return DEC[k];
}

async function loadData() {
  const k = key();
  if (!DATA[k]) {
    const solo = await (await fetch(`data/${dataDir()}/${S.ch}.json`)).json();
    if (S.mode === "coop") {
      const co = await (await fetch(`data/${dataDir()}/${S.ch}.coop.json`)).json();
      // rare cards without a co-op model fall back to the solo estimate, flagged as low data
      const simple = { ...solo.simple };
      for (const [id, m] of Object.entries(solo.models)) if (!co.models[id]) simple[id] = { value: m.value, se: 0.03, picks: m.picks, skips: m.offers - m.picks };
      for (const id of Object.keys(co.models)) delete simple[id];
      DATA[k] = { ...co, runs: co.seats, simple, names: { ...solo.names, ...co.names } };
    } else DATA[k] = solo;
  }
  if (S.mode === "coop" && !LINEUPS[dataDir()]) LINEUPS[dataDir()] = await (await fetch(`data/${dataDir()}/coop_lineups.json`)).json();
  return DATA[k];
}

// ---------- scoring ----------
function score(id, D) {
  const dk = deck(), set = Object.keys(dk), n = set.reduce((s, c) => s + dk[c], 0);
  const m = D.models[id];
  if (m) {
    const b = Math.min(Math.floor((Math.max(1, S.floor) - 1) / 9), 5);
    let z0 = m.a + m.bucket[b] + m.k * n / 10, g = 0;
    for (const p of set) { if (m.delta[p]) z0 += m.delta[p]; if (m.gamma[p]) g += m.gamma[p]; }
    if (m.np) {   // co-op: team size and teammates' characters
      const np = Math.min(Math.max(S.mates.length + 1, 2), 4) - 2;
      z0 += m.np[np]; g += m.pnp[np];
      for (const t of S.mates) { z0 += m.mate[t] || 0; g += m.pmate[t] || 0; }
    }
    const base = sig(z0 + m.beta) - sig(z0), gain = sig(z0 + m.beta + g) - sig(z0);
    const contrib = set.filter((p) => m.gamma[p]).map((p) => ({ p, v: sig(z0 + m.beta + m.gamma[p]) - sig(z0 + m.beta) }))
      .sort((x, y) => Math.abs(y.v) - Math.abs(x.v));
    return { id, gain, base, syn: gain - base, contrib, offers: m.offers, picks: m.picks, avg: m.value, low: false };
  }
  const s = D.simple[id];
  if (s) return { id, gain: s.value, base: s.value, syn: 0, contrib: [], offers: s.picks + s.skips, picks: s.picks, se: s.se, avg: s.value,
                  low: true, tiny: s.picks < 50 || s.se > 0.05 };
  return null;
}

// ---------- autocomplete ----------
function pool() {
  const D = DATA[key()];
  const ids = new Set(Object.keys(CARDS).filter((id) => CARDS[id].color === S.ch || POOL_EXTRA.includes(CARDS[id].color)));
  if (D) for (const id of [...Object.keys(D.models), ...Object.keys(D.simple)]) ids.add(id);
  return [...ids];
}
const CARD_SRC = { pool: () => pool(), name: (id) => name(id), img: (id) => img(id), sub: (id) => CARDS[id]?.color ?? "", own: (id) => (CARDS[id]?.color === S.ch ? 0 : 1) };
function autocomplete(input, list, onPick, src = CARD_SRC) {
  let hl = 0, items = [];
  const close = () => list.classList.remove("open");
  const render = () => {
    const q = input.value.trim().toLowerCase();
    if (!q) return close();
    const nm = src.name, own = src.own || (() => 0);
    items = src.pool().filter((id) => nm(id).toLowerCase().includes(q))
      .sort((a, b) => own(a) - own(b) || (nm(a).toLowerCase().startsWith(q) ? 0 : 1) - (nm(b).toLowerCase().startsWith(q) ? 0 : 1) || nm(a).localeCompare(nm(b)))
      .slice(0, 12);
    hl = 0;
    list.innerHTML = items.map((id, i) => `<li data-i="${i}" class="${i === hl ? "hl" : ""}">${src.img(id) ? `<img src="${src.img(id)}" alt="">` : ""}<span>${nm(id)}</span><span class="muted small">${src.sub(id)}</span></li>`).join("");
    list.classList.toggle("open", items.length > 0);
  };
  const pick = (i) => { if (items[i]) { onPick(items[i]); input.value = ""; close(); } };
  input.addEventListener("input", render);
  input.addEventListener("keydown", (e) => {
    if (!list.classList.contains("open")) return;
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault(); hl = (hl + (e.key === "ArrowDown" ? 1 : items.length - 1)) % items.length;
      [...list.children].forEach((li, i) => li.classList.toggle("hl", i === hl));
    } else if (e.key === "Enter") { e.preventDefault(); pick(hl); } else if (e.key === "Escape") close();
  });
  list.addEventListener("mousedown", (e) => { const li = e.target.closest("li"); if (li) { e.preventDefault(); pick(+li.dataset.i); } });
  input.addEventListener("blur", () => setTimeout(close, 100));
}

// ---------- render ----------
function renderMates() {
  const el = $("#mates");
  if (S.mode !== "coop") { el.innerHTML = ""; return; }
  const opt = (sel) => CHARS.map((c) => `<option value="${c}" ${c === sel ? "selected" : ""}>${cap(c)}</option>`).join("");
  el.innerHTML = `<span class="muted">Teammates</span>` + [0, 1, 2].map((i) => `<select data-mate="${i}">
      <option value="">${i === 0 ? "—" : "none"}</option>${opt(S.mates[i])}</select>`).join("");
}
function renderTeam() {
  const panel = $("#team-panel");
  panel.hidden = S.mode !== "coop";
  if (S.mode !== "coop" || !LINEUPS[dataDir()]) return;
  const n = Math.min(Math.max(S.mates.length + 1, 2), 4), L = LINEUPS[dataDir()].lineups[String(n)];
  const mine = [S.ch, ...S.mates].sort().join("+");
  const row = (l) => `<div class="team-row ${l.lineup.join("+") === mine ? "me" : ""}"><div class="lu">${l.lineup.map((c) => `<span class="chip" style="border-color:var(--${c})">${cap(c)}</span>`).join("")}</div>
      <span class="g ${l.vs_avg >= 0 ? "pos" : "neg"}">${(l.win_rate * 100).toFixed(1)}%</span><span class="muted small">${l.runs.toLocaleString()} runs</span></div>`;
  const me = L.lineups.find((l) => l.lineup.join("+") === mine);
  const sorted = [...L.lineups].sort((a, b) => b.win_rate - a.win_rate);
  $("#team-n").textContent = `${n} players · avg ${(L.avg_win_rate * 100).toFixed(1)}% win`;
  $("#team").innerHTML = (me ? `<h3>Your lineup</h3>${row(me)}<p class="muted small">95% range ${(me.lo * 100).toFixed(1)}–${(me.hi * 100).toFixed(1)}% · ${pct(me.vs_avg)} vs the ${n}-player average</p>`
      : `<p class="muted small">Your exact lineup has fewer than 30 recorded A10 runs.</p>`) +
    `<h3>Best ${n}-player lineups</h3>${sorted.slice(0, 5).map(row).join("")}<h3>Worst</h3>${sorted.slice(-3).reverse().map(row).join("")}
     <p class="muted small">Lineups reflect who plays them as well as the characters; treat small differences as noise.</p>`;
}
function renderControls() {
  $("#mode").innerHTML = MODES.map((m) => `<button data-m="${m.id}" class="${m.id === S.mode ? "on" : ""}">${m.label}</button>`).join("");
  $("#branch").innerHTML = BRANCHES.map((b) => `<button data-b="${b.id}" class="${b.id === S.branch ? "on" : ""}" title="${b.note}">${b.label}</button>`).join("");
  $("#chars").innerHTML = CHARS.map((c) => `<button data-c="${c}" style="--c:var(--${c})" class="${c === S.ch ? "on" : ""}">${c[0].toUpperCase() + c.slice(1)}</button>`).join("");
}
function renderDeck() {
  const dk = deck(), ids = Object.keys(dk).sort((a, b) => name(a).localeCompare(name(b)));
  $("#deck-count").textContent = `· ${ids.reduce((s, c) => s + dk[c], 0)} cards`;
  $("#deck-list").innerHTML = ids.length ? ids.map((id) => `<li><span class="n">${dk[id]}×</span><span class="name">${name(id)}</span>
      <button data-add="${id}" title="Add a copy">+</button><button data-rm="${id}" title="Remove one">−</button></li>`).join("")
    : `<li class="muted">Empty. Load the starter deck or add cards.</li>`;
  $("#floor").value = S.floor;
}
function bar(g, max) {
  const w = Math.min(50, (Math.abs(g) / max) * 50);
  return `<div class="bar"><span style="${g >= 0 ? `left:50%;width:${w}%;background:var(--good)` : `right:50%;width:${w}%;background:var(--bad)`}"></span></div>`;
}
// deck cards that most raise this card's value (strong associations already in your deck)
function withDeck(r) {
  const top = (r.contrib || []).filter((x) => x.v >= 0.004 && CARDS[x.p]?.rarity !== "Basic").sort((a, b) => b.v - a.v).slice(0, 2);
  return top.length ? ` · <span class="pos">with ${top.map((x) => `${name(x.p)} ${pct(x.v)}`).join(", ")}</span>` : "";
}
function synPill(r) {
  if (r.low) return `<span class="pill low">low data</span>`;
  if (Math.abs(r.syn) < 0.003) return "";
  return `<span class="pill ${r.syn > 0 ? "syn-pos" : "syn-neg"}">${S.mode === "coop" ? "team+deck" : "synergy"} ${pct(r.syn)}</span>`;
}
function detail(r) {
  const c = CARDS[r.id] || {};
  const rows = r.contrib.slice(0, 8).map((x) => `<tr><td>${name(x.p)}</td><td class="num ${x.v >= 0 ? "pos" : "neg"}">${pct(x.v)}</td></tr>`).join("");
  return `<div class="detail">${img(r.id) ? `<img src="${img(r.id)}" alt="${name(r.id)}" loading="lazy">` : ""}
    <div><div class="ctext">${c.text || ""}</div>
    <h3>Why</h3><table>
      <tr><td>Pick value for a deck like yours</td><td class="num">${pct(r.base)}</td></tr>
      ${r.low ? "" : `<tr><td>${S.mode === "coop" ? "Synergy with your deck and team" : "Synergy with cards in your deck"}</td><td class="num ${r.syn >= 0 ? "pos" : "neg"}">${pct(r.syn)}</td></tr>`}
      <tr><td><b>Total</b></td><td class="num"><b>${pct(r.gain)}</b></td></tr>
      <tr><td class="muted">Average value across all A10 offers</td><td class="num muted">${pct(r.avg)}</td></tr>
      <tr><td class="muted">Times offered · picked</td><td class="num muted">${r.offers.toLocaleString()} · ${r.picks.toLocaleString()}</td></tr>
      ${r.se ? `<tr><td class="muted">Uncertainty (±1 SE)</td><td class="num muted">±${(r.se * 100).toFixed(1)}%</td></tr>` : ""}
    </table>
    ${rows ? `<h3>Biggest synergy effects from your deck</h3><table>${rows}</table>` : r.low ? `<p class="muted small">Too rare at A10 for a deck-aware model; this is its overall picked-vs-skipped value.</p>` : ""}
    </div></div>`;
}
async function renderRank() {
  const D = await loadData();
  const showLow = $("#show-low").checked, f = $("#filter").value.trim().toLowerCase();
  let rows = [...Object.keys(D.models), ...Object.keys(D.simple)].map((id) => score(id, D)).filter(Boolean)
    .filter((r) => (showLow || !r.tiny) && (!f || name(r.id).toLowerCase().includes(f)))
    .filter((r) => !["curse", "status"].includes(CARDS[r.id]?.color));
  const rankKey = (r) => (r.low ? r.gain - (r.se || 0) : r.gain);   // low-data cards ranked by a cautious estimate
  rows.sort((a, b) => rankKey(b) - rankKey(a));
  const max = Math.max(0.02, ...rows.map((r) => Math.abs(r.gain)));
  const br = BRANCHES.find((b) => b.id === S.branch);
  $("#meta").textContent = S.mode === "coop"
    ? `${br.label} (${br.note}) · co-op · ${S.ch} with ${S.mates.map(cap).join(", ") || "—"} · ${D.runs.toLocaleString()} A10 co-op player-runs · floor ${S.floor}`
    : `${br.label} (${br.note}) · ${S.ch} · ${D.runs.toLocaleString()} A10 runs, ${(D.wins / D.runs * 100).toFixed(1)}% won · floor ${S.floor}`;
  $("#rank").innerHTML = rows.map((r, i) => `<li data-id="${r.id}" class="${S.open.has(r.id) ? "open" : ""}">
      <div class="top"><span class="i">${i + 1}</span>${img(r.id) ? `<img class="th" src="${img(r.id)}" alt="" loading="lazy">` : `<span></span>`}
        <div><div class="nm">${name(r.id)}${synPill(r)}</div><div class="sub">${[CARDS[r.id]?.rarity, CARDS[r.id]?.type, CARDS[r.id]?.cost != null ? CARDS[r.id].cost + " energy" : ""].filter(Boolean).join(" · ")}${withDeck(r)}</div></div>
        ${bar(r.gain, max)}<span class="g ${r.gain >= 0 ? "pos" : "neg"}">${pct(r.gain)}</span></div>${detail(r)}</li>`).join("");
}
async function renderReward() {
  const D = await loadData();
  const ids = S.slots.filter(Boolean);
  if (!ids.length) { $("#reward-result").innerHTML = ""; return; }
  const rs = ids.map((id) => score(id, D) || { id, gain: NaN, low: true });
  const best = Math.max(...rs.filter((r) => !isNaN(r.gain)).map((r) => r.gain));
  $("#reward-result").innerHTML = `<div class="reward-cards">${S.slots.map((id) => {
    if (!id) return `<div></div>`;
    const r = rs.find((x) => x.id === id);
    return `<div class="rc ${r.gain === best && best > 0 ? "best" : ""}">${img(id) ? `<img src="${img(id)}" alt="${name(id)}">` : `<div>${name(id)}</div>`}
      <div class="g ${r.gain >= 0 ? "pos" : "neg"}">${isNaN(r.gain) ? "no data" : pct(r.gain)}</div>${r.low && !isNaN(r.gain) ? `<span class="pill low">low data</span>` : ""}</div>`;
  }).join("")}</div><p class="skip">${best <= 0 ? "<b>Skip</b> looks best: every option scores below skipping." : "Compared with skipping the reward (0%)."} <a href="#" id="clear-slots">clear</a></p>`;
}
function renderSlots() {
  $("#slots").innerHTML = S.slots.map((id, i) => `<div class="slot search"><input data-slot="${i}" placeholder="Card ${i + 1}" value="${id ? name(id) : ""}" autocomplete="off"><ul class="suggest" id="slot-s-${i}"></ul></div>`).join("");
  S.slots.forEach((_, i) => {
    const inp = document.querySelector(`[data-slot="${i}"]`);
    autocomplete(inp, $(`#slot-s-${i}`), (id) => { S.slots[i] = id; renderSlots(); renderReward(); });
    inp.addEventListener("focus", () => inp.select());
  });
}
// Remove / upgrade advice for the current deck: value of choosing each card instead of another card at the same decision.
function decisionScore(m, deckSet, n, floor, extra = 0) {
  const b = Math.min(Math.floor((Math.max(1, floor) - 1) / 9), 5);
  let z0 = m.a + m.bucket[b] + m.k * n / 10 + extra;
  for (const p of deckSet) if (m.delta[p]) z0 += m.delta[p];
  return sig(z0 + m.beta) - sig(z0);
}
async function renderFix() {
  const D = await loadDecisions(), el = $("#fix");
  [...document.querySelectorAll("#fix-tab button")].forEach((b) => b.classList.toggle("on", b.dataset.t === S.fixTab));
  if (!D) { el.innerHTML = `<p class="muted small">No removal/upgrade data for this branch.</p>`; return; }
  const dk = deck(), ids = Object.keys(dk), n = ids.reduce((s, c) => s + dk[c], 0), set = new Set(ids);
  if (!ids.length) { el.innerHTML = `<p class="muted small">Add your deck above first.</p>`; return; }
  const row = (r, i) => `<div class="fix-row ${i === 0 ? "top" : ""}">${img(r.id) ? `<img src="${img(r.id)}" alt="">` : "<span></span>"}<span>${name(r.id)}${dk[r.id] > 1 ? ` <span class="muted small">×${dk[r.id]}</span>` : ""}</span><span class="g ${r.v >= 0 ? "pos" : "neg"}">${pct(r.v)}</span></div>`;
  if (S.fixTab === "doors") {
    if (dataDir() !== S.branch) { el.innerHTML = `<p class="muted small">Doors of Light and Dark changed in ${S.branch}: Light now lets you pick 1 card to upgrade (it used to upgrade 2 at random). The data here is for the old version, so there's no advice for this event until ${S.branch} runs are available.</p>`; return; }
    const d = D.doors; if (!d) { el.innerHTML = `<p class="muted small">Not enough Doors of Light and Dark visits for this character.</p>`; return; }
    const basics = ids.filter((id) => /CARD\.(STRIKE|DEFEND)_/.test(id)).reduce((s, id) => s + dk[id], 0);
    const b = Math.min(Math.floor((Math.max(1, S.floor) - 1) / 9), 5);
    const z = d.a + d.bucket[b] + d.k * n / 10 + d.kb * basics;
    const diff = d.r + d.r_n * n / 10 + d.r_b * basics + d.r_f * S.floor / 10;
    const g = sig(z + diff) - sig(z);
    el.innerHTML = `<p class="muted small">Doors of Light and Dark: remove 1 card, or upgrade 2.</p>
      <div class="verdict ${g >= 0 ? "pos" : "neg"}">${g >= 0 ? "Remove a card" : "Take the upgrades"} <span class="small">(${pct(Math.abs(g))} win chance)</span></div>
      <p class="muted small">For a ${n}-card deck with ${basics} Strikes/Defends at floor ${S.floor}. Bigger decks with more basics favour removing. Based on ${d.n.toLocaleString()} A10 visits (raw: remove ${(d.raw.remove_win * 100).toFixed(1)}% vs upgrade ${(d.raw.upgrade_win * 100).toFixed(1)}% win).</p>`;
    return;
  }
  if (S.fixTab === "transform") {
    const X = await loadExtra(), T = X?.transform || {};
    const tr = (m) => { const b = bucketOf(S.floor), z0 = m.a + m.bucket[b] + m.k * n / 10 + m.kn; return sig(z0 + m.beta) - sig(z0); };
    const rows = ids.filter((id) => T[id]).map((id) => ({ id, v: tr(T[id]) })).sort((a, b) => b.v - a.v);
    el.innerHTML = rows.length ? `<h3>Best cards to transform</h3>${rows.map(row).join("")}
      <p class="muted small">Change in win chance from transforming this card instead of another. Players almost only transform starter cards, so only those have enough data.</p>`
      : `<p class="muted small">None of your cards have enough transform data (mostly only starter cards do).</p>`;
    return;
  }
  const models = S.fixTab === "remove" ? D.removal : D.upgrade;
  const rows = ids.filter((id) => models[id]).map((id) => ({ id, v: decisionScore(models[id], set, n, S.floor, S.fixTab === "remove" ? models[id].kn + models[id].shop : 0) }))
    .sort((a, b) => b.v - a.v);
  const missing = ids.filter((id) => !models[id]).length;
  el.innerHTML = rows.length ? `<h3>${S.fixTab === "remove" ? "Best cards to remove" : "Best cards to upgrade"}</h3>${rows.slice(0, 6).map(row).join("")}
      ${rows.length > 6 ? `<h3>${S.fixTab === "remove" ? "Keep these" : "Least worth upgrading"}</h3>${rows.slice(-3).reverse().map((r) => row(r, 9)).join("")}` : ""}
      <p class="muted small">Change in win chance from ${S.fixTab === "remove" ? "removing" : "upgrading"} this card instead of another card in your deck.${missing ? ` ${missing} card${missing > 1 ? "s" : ""} in your deck ${missing > 1 ? "are" : "is"} rarely ${S.fixTab === "remove" ? "removed" : "upgraded"}, so not scored.` : ""}</p>`
    : `<p class="muted small">None of your cards have enough ${S.fixTab === "remove" ? "removal" : "upgrade"} data.</p>`;
}

// ---------- run: win chance, relics, rest sites (same math as the in-game mod) ----------
const EX = {};
async function loadExtra() {
  const k = dataDir() + "/" + S.ch;
  if (!(k in EX)) { try { EX[k] = await (await fetch(`data/${dataDir()}/${S.ch}.extra.json`)).json(); } catch { EX[k] = null; } }
  return EX[k];
}
const relicName = (id) => id.replace(/^RELIC\./, "").replace(/_/g, " ").toLowerCase().replace(/\b\w/g, (c) => c.toUpperCase());
const bucketOf = (floor) => Math.min(Math.floor((Math.max(1, floor) - 1) / 9), 5);
const deckInfo = () => { const dk = deck(), ids = Object.keys(dk); return { set: new Set(ids), n: ids.reduce((s, c) => s + dk[c], 0) }; };
const REL_CTX = { shop: { label: "Shop", offered: 3 }, ancient: { label: "Ancient", offered: 3 }, other: { label: "Event / other", offered: 2 } };
function relicValue(m, n, floor, ctx) {
  const z0 = m.a + m.bucket[bucketOf(floor)] + m.k * n / 10 + (ctx === "shop" ? m.shop : 0) + (ctx === "ancient" ? m.ancient : 0) + m.kn * REL_CTX[ctx].offered;
  return sig(z0 + m.beta) - sig(z0);
}
function winChance(w, set, n, r, floor) {
  if (!w || !(r.mhp > 0)) return null;
  let z = w.a + w.hp * Math.min(1, Math.max(0, r.hp / r.mhp)) + w.mhp * r.mhp / 100 + w.gold * r.gold / 100 + w.relics * r.relics + w.k * n / 10 + w.fl * floor / 10 + w.bucket[bucketOf(floor)];
  for (const c of set) if (w.deck[c]) z += w.deck[c];
  return sig(z);
}
async function renderRun() {
  const X = await loadExtra(), el = $("#run");
  [...document.querySelectorAll("#run-tab button")].forEach((b) => b.classList.toggle("on", b.dataset.t === S.runTab));
  for (const k of ["hp", "mhp", "gold", "relics"]) { const i = $("#run-" + k); if (document.activeElement !== i) i.value = S.run[k]; }
  $("#run-stats").hidden = S.runTab === "relics";
  if (!X) { el.innerHTML = `<p class="muted small">No run data for this branch and character.</p>`; return; }
  const { set, n } = deckInfo();
  if (S.runTab === "win") {
    const p = winChance(X.win, set, n, S.run, S.floor);
    el.innerHTML = p == null ? `<p class="muted small">Enter your max HP.</p>` : `<div class="win-big">${(p * 100).toFixed(0)}%</div>
      <p class="muted small">Estimated chance this run wins at A10, from your deck, HP, max HP, gold, relic count and floor ${S.floor}.
      A rough guide: the model ranks a winning run above a losing one ${(X.win.auc * 100).toFixed(0)}% of the time (${X.win.n.toLocaleString()} run snapshots). It can't see your relics' names, potions or the next fights.</p>`;
    return;
  }
  if (S.runTab === "rest") {
    const act = S.floor <= 17 ? 0 : S.floor <= 34 ? 1 : 2, hb = Math.min(9, Math.max(0, Math.floor(S.run.hp * 10 / Math.max(1, S.run.mhp))));
    const share = X.rest.winners_smith[act][hb], cnt = X.rest.n[act][hb];
    const cells = X.rest.winners_smith[act].map((v, i) => `<div class="rest-cell ${i === hb ? "on" : ""}" title="${i * 10}-${i * 10 + 9}% HP: ${(v * 100).toFixed(0)}% smithed (${X.rest.n[act][i]} rests)"><span style="height:${Math.round(v * 100)}%"></span><em>${i * 10}</em></div>`).join("");
    el.innerHTML = `<div class="verdict ${share >= 0.5 ? "pos" : "neg"}">${cnt >= 30 ? `Winners smithed ${(share * 100).toFixed(0)}%` : "Too few rests at this HP"} <span class="small">at ${hb * 10}-${hb * 10 + 9}% HP in Act ${act + 1}${cnt >= 30 ? ` (${cnt.toLocaleString()} rests)` : ""}</span></div>
      <div class="rest-chart">${cells}</div><p class="muted small">Share of winning A10 runs that chose Smith over Rest, by HP (%) in Act ${act + 1}.
      This shows what winners did, not a verdict: players only Smith at low HP when they know they're safe, which the data can't see.</p>`;
    return;
  }
  // relics
  const ids = Object.keys(X.relic);
  const ctxBtns = Object.entries(REL_CTX).map(([k, v]) => `<button data-ctx="${k}" class="${S.relCtx === k ? "on" : ""}">${v.label}</button>`).join("");
  const offered = S.relSlots.filter((id) => id && X.relic[id]).map((id) => ({ id, v: relicValue(X.relic[id], n, S.floor, S.relCtx) }));
  const best = offered.length > 1 ? offered.reduce((a, b) => (b.v > a.v ? b : a)).id : null;
  const all = ids.map((id) => ({ id, v: relicValue(X.relic[id], n, S.floor, S.relCtx) })).sort((a, b) => b.v - a.v);
  const row = (r) => `<div class="fix-row rel ${r.id === best ? "top" : ""}"><span></span><span>${relicName(r.id)}</span><span class="g ${r.v >= 0 ? "pos" : "neg"}">${pct(r.v)}</span></div>`;
  el.innerHTML = `<div class="seg small-seg" id="rel-ctx">${ctxBtns}</div>
    <div class="slots rel-slots">${S.relSlots.map((v, i) => `<div class="slot search"><input data-rs="${i}" placeholder="Relic ${i + 1}…" value="${v ? relicName(v) : ""}" autocomplete="off"><ul class="suggest" data-rsl="${i}"></ul></div>`).join("")}</div>
    ${offered.length ? `<h3>Your choice</h3>${offered.map(row).join("")}` : ""}
    <h3>Best ${REL_CTX[S.relCtx].label.toLowerCase()} relics at floor ${S.floor}</h3>${all.slice(0, 6).map(row).join("")}
    <h3>Worst</h3>${all.slice(-3).reverse().map(row).join("")}
    <p class="muted small">Change in win chance from taking the relic instead of passing on it, from ${ids.length} relics offered in A10 shops, Ancient choices and events.
    Treat these as a ranking: the sizes are probably exaggerated, because players take strong relics when their run is already going well.</p>`;
  const src = { pool: () => ids, name: relicName, img: () => "", sub: () => "" };
  el.querySelectorAll("[data-rs]").forEach((inp) => {
    const i = +inp.dataset.rs;
    autocomplete(inp, el.querySelector(`[data-rsl="${i}"]`), (id) => { S.relSlots[i] = id; renderRun(); }, src);
    inp.addEventListener("change", () => { if (!inp.value.trim()) { S.relSlots[i] = ""; renderRun(); } });
  });
}
async function renderAll() {
  renderControls(); renderMates(); renderDeck(); renderSlots();
  await loadData(); renderRank(); renderReward(); renderTeam(); renderFix(); renderRun(); save();
}

// ---------- events ----------
function wire() {
  $("#mode").addEventListener("click", (e) => { const m = e.target.dataset.m; if (m) { S.mode = m; renderAll(); } });
  $("#mates").addEventListener("change", (e) => {
    const i = e.target.dataset.mate; if (i == null) return;
    const v = [...document.querySelectorAll("[data-mate]")].map((s) => s.value).filter(Boolean);
    S.mates = v.length ? v : [];
    renderMates(); renderRank(); renderReward(); renderTeam(); save();
  });
  $("#branch").addEventListener("click", (e) => { const b = e.target.dataset.b; if (b) { S.branch = b; renderAll(); } });
  $("#chars").addEventListener("click", (e) => { const c = e.target.dataset.c; if (c) { S.ch = c; S.slots = ["", "", ""]; S.relSlots = ["", "", ""]; renderAll(); } });
  autocomplete($("#deck-search"), $("#deck-suggest"), (id) => { deck()[id] = (deck()[id] || 0) + 1; renderDeck(); renderRank(); renderReward(); renderFix(); renderRun(); save(); });
  $("#deck-list").addEventListener("click", (e) => {
    const a = e.target.dataset.add, r = e.target.dataset.rm, dk = deck();
    if (a) dk[a]++;
    if (r && --dk[r] <= 0) delete dk[r];
    if (a || r) { renderDeck(); renderRank(); renderReward(); renderFix(); renderRun(); save(); }
  });
  $("#starter").addEventListener("click", () => { S.decks[S.ch] = { ...(STARTERS[S.ch] || {}) }; S.floor = 1; renderDeck(); renderRank(); renderReward(); renderFix(); renderRun(); save(); });
  $("#clear").addEventListener("click", () => { S.decks[S.ch] = {}; renderDeck(); renderRank(); renderReward(); renderFix(); renderRun(); save(); });
  $("#floor").addEventListener("input", (e) => { S.floor = Math.max(1, +e.target.value || 1); renderRank(); renderReward(); renderFix(); renderRun(); save(); });
  $("#fix-tab").addEventListener("click", (e) => { if (e.target.dataset.t) { S.fixTab = e.target.dataset.t; renderFix(); } });
  $("#run-tab").addEventListener("click", (e) => { if (e.target.dataset.t) { S.runTab = e.target.dataset.t; renderRun(); save(); } });
  for (const k of ["hp", "mhp", "gold", "relics"]) $("#run-" + k).addEventListener("input", (e) => { S.run[k] = Math.max(0, +e.target.value || 0); renderRun(); save(); });
  $("#run").addEventListener("click", (e) => { const c = e.target.dataset.ctx; if (c) { S.relCtx = c; renderRun(); save(); } });
  $("#filter").addEventListener("input", renderRank);
  $("#show-low").addEventListener("change", renderRank);
  $("#rank").addEventListener("click", (e) => {
    const li = e.target.closest("li[data-id]"); if (!li || !e.target.closest(".top")) return;
    const id = li.dataset.id; S.open.has(id) ? S.open.delete(id) : S.open.add(id); li.classList.toggle("open");
  });
  $("#reward-result").addEventListener("click", (e) => { if (e.target.id === "clear-slots") { e.preventDefault(); S.slots = ["", "", ""]; renderSlots(); renderReward(); } });
}

(async function init() {
  [CARDS, STARTERS] = await Promise.all([fetch("data/cards.json").then((r) => r.json()), fetch("data/starters.json").then((r) => r.json())]);
  if (!BRANCHES.some((b) => b.id === S.branch)) S.branch = BRANCHES[0].id;
  if (!CHARS.includes(S.ch)) S.ch = "ironclad";
  if (!MODES.some((m) => m.id === S.mode)) S.mode = "solo";
  S.mates = (S.mates || []).filter((c) => CHARS.includes(c)).slice(0, 3);
  if (!Object.keys(deck()).length) S.decks[S.ch] = { ...(STARTERS[S.ch] || {}) };
  wire(); renderAll();
})();
