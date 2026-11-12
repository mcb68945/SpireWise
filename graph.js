"use strict";
const BRANCHES = [{ id: "v0.107.1", label: "Live" }, { id: "v0.111.0", label: "Beta" }];
const CHARS = ["ironclad", "silent", "defect", "regent", "necrobinder"];
const $ = (s) => document.querySelector(s);
const store = {
  get(k, d) { try { const v = localStorage.getItem("spirewise." + k); return v ? JSON.parse(v) : d; } catch { return d; } },
  set(k, v) { try { localStorage.setItem("spirewise." + k, JSON.stringify(v)); } catch {} },
};
const S = { branch: store.get("branch", "v0.107.1"), ch: store.get("ch", "ironclad"), sel: null };
let CARDS = {}, D = null, PAIRS = [], BYCARD = new Map();
const name = (id) => CARDS[id]?.name || D?.names?.[id] || id.replace(/^CARD\./, "").replace(/_/g, " ").toLowerCase().replace(/\b\w/g, (c) => c.toUpperCase());
const img = (id) => (CARDS[id]?.img ? "img/cards/" + CARDS[id].img : "");
const odds = (w) => { const x = (Math.exp(w) - 1) * 100; return (x >= 0 ? "+" : "−") + Math.abs(x).toFixed(0) + "% odds"; };
const value = (id) => D.models[id]?.value ?? D.simple[id]?.value ?? 0;
const skip = (id) => !CARDS[id] || CARDS[id].rarity === "Basic" || ["curse", "status"].includes(CARDS[id].color);

async function load() {
  D = await (await fetch(`data/${S.branch}/${S.ch}.json`)).json();
  // Undirected pair weight: average of "pick A with B in deck" and "pick B with A in deck" where both exist.
  const acc = new Map();
  for (const [c, m] of Object.entries(D.models)) {
    if (skip(c)) continue;
    for (const [p, g] of Object.entries(m.gamma)) {
      if (p === c || skip(p)) continue;
      const k = c < p ? c + "|" + p : p + "|" + c;
      const a = acc.get(k) || { a: c < p ? c : p, b: c < p ? p : c, s: 0, n: 0 };
      a.s += g; a.n += 1; acc.set(k, a);
    }
  }
  PAIRS = [...acc.values()].map((x) => ({ source: x.a, target: x.b, w: x.s / x.n, both: x.n === 2 }));
  BYCARD = new Map();
  for (const e of PAIRS) for (const [me, other] of [[e.source, e.target], [e.target, e.source]]) {
    if (!BYCARD.has(me)) BYCARD.set(me, []);
    BYCARD.get(me).push({ id: other, w: e.w });
  }
  const br = BRANCHES.find((b) => b.id === S.branch);
  $("#meta").textContent = `${br.label} ${S.branch} · ${S.ch} · ${D.runs.toLocaleString()} A10 runs · ${PAIRS.length.toLocaleString()} card pairs with enough data`;
}

function controls() {
  $("#branch").innerHTML = BRANCHES.map((b) => `<button data-b="${b.id}" class="${b.id === S.branch ? "on" : ""}">${b.label}</button>`).join("");
  $("#chars").innerHTML = CHARS.map((c) => `<button data-c="${c}" style="--c:var(--${c})" class="${c === S.ch ? "on" : ""}">${c[0].toUpperCase() + c.slice(1)}</button>`).join("");
}

let sim = null;
function draw() {
  const n = +$("#nedges").value; $("#nedges-v").textContent = n;
  const pos = $("#show-pos").checked, neg = $("#show-neg").checked;
  const edges = PAIRS.filter((e) => (e.w > 0 ? pos : neg)).sort((a, b) => Math.abs(b.w) - Math.abs(a.w)).slice(0, n)
    .map((e) => ({ ...e }));
  const ids = [...new Set(edges.flatMap((e) => [e.source, e.target]))];
  const vmax = Math.max(0.02, ...ids.map((id) => Math.abs(value(id))));
  const nodes = ids.map((id) => ({ id, r: 16 + 14 * Math.min(1, Math.abs(value(id)) / vmax), v: value(id) }));
  const wmax = Math.max(...edges.map((e) => Math.abs(e.w)), 0.01);

  const svg = d3.select("#svg"); svg.selectAll("*").remove();
  const { width, height } = $("#stage").getBoundingClientRect();
  const g = svg.append("g");
  svg.call(d3.zoom().scaleExtent([0.2, 4]).on("zoom", (ev) => g.attr("transform", ev.transform)));
  const defs = svg.append("defs");
  nodes.forEach((d, i) => { d.clip = "c" + i; defs.append("clipPath").attr("id", d.clip).append("circle").attr("r", d.r - 2); });

  const link = g.append("g").selectAll("path").data(edges).join("path").attr("class", "edge")
    .attr("stroke", (e) => (e.w > 0 ? "var(--good)" : "var(--bad)"))
    .attr("stroke-width", (e) => 1 + 5 * Math.abs(e.w) / wmax).attr("stroke-opacity", 0.75)
    .attr("stroke-dasharray", (e) => (e.w > 0 ? null : "6 4"));
  link.append("title").text((e) => `${name(e.source)} + ${name(e.target)}: ${odds(e.w)}`);

  const node = g.append("g").selectAll("g").data(nodes).join("g").attr("class", "node")
    .call(d3.drag().on("start", (ev, d) => { if (!ev.active) sim.alphaTarget(0.2).restart(); d.fx = d.x; d.fy = d.y; })
      .on("drag", (ev, d) => { d.fx = ev.x; d.fy = ev.y; })
      .on("end", (ev, d) => { if (!ev.active) sim.alphaTarget(0); d.fx = null; d.fy = null; }))
    .on("click", (ev, d) => select(d.id));
  node.append("circle").attr("class", "ring").attr("r", (d) => d.r).attr("stroke", (d) => (d.v >= 0 ? "var(--good)" : "var(--bad)"));
  node.filter((d) => img(d.id)).append("image").attr("href", (d) => img(d.id))
    .attr("width", (d) => d.r * 2.9).attr("height", (d) => d.r * 4.06).attr("x", (d) => -d.r * 1.45).attr("y", (d) => -d.r * 1.45)
    .attr("clip-path", (d) => `url(#${d.clip})`).attr("preserveAspectRatio", "xMidYMin slice");
  node.append("text").attr("y", (d) => d.r + 13).text((d) => name(d.id));
  node.append("title").text((d) => `${name(d.id)} · own value ${(d.v * 100).toFixed(1)}%`);

  sim?.stop();
  sim = d3.forceSimulation(nodes)
    .force("link", d3.forceLink(edges).id((d) => d.id).distance((e) => 150 - 60 * Math.abs(e.w) / wmax).strength(0.5))
    .force("charge", d3.forceManyBody().strength(-380))
    .force("center", d3.forceCenter(width / 2, height / 2))
    .force("collide", d3.forceCollide().radius((d) => d.r + 16))
    .on("tick", () => {
      link.attr("d", (e) => `M${e.source.x},${e.source.y}L${e.target.x},${e.target.y}`);
      node.attr("transform", (d) => `translate(${d.x},${d.y})`);
    });
  window._graph = { node, link };
  if (S.sel) highlight(S.sel);
}

function highlight(id) {
  const G = window._graph; if (!G) return;
  const nb = new Set([id, ...(BYCARD.get(id) || []).map((x) => x.id)]);
  G.node.classed("dim", (d) => !nb.has(d.id));
  G.link.classed("dim", (e) => e.source.id !== id && e.target.id !== id);
}
function select(id) {
  S.sel = id; highlight(id);
  const list = (BYCARD.get(id) || []).sort((a, b) => b.w - a.w);
  const row = (x) => `<tr><td><a href="#" data-pick="${x.id}">${name(x.id)}</a></td><td class="num ${x.w >= 0 ? "pos" : "neg"}">${odds(x.w)}</td></tr>`;
  const best = list.filter((x) => x.w > 0).slice(0, 8), worst = list.filter((x) => x.w < 0).slice(-6).reverse();
  $("#detail").innerHTML = `${img(id) ? `<img src="${img(id)}" alt="">` : ""}<h2>${name(id)}</h2>
    <p class="muted small">Own pick value: <b class="${value(id) >= 0 ? "pos" : "neg"}">${(value(id) * 100 >= 0 ? "+" : "")}${(value(id) * 100).toFixed(1)}%</b> win chance (picked vs skipped).</p>
    <p class="muted small">${CARDS[id]?.text || ""}</p>
    <h3>Wins more with</h3>${best.length ? `<table>${best.map(row).join("")}</table>` : `<p class="muted small">No clear partners.</p>`}
    <h3>Loses more with</h3>${worst.length ? `<table>${worst.map(row).join("")}</table>` : `<p class="muted small">No clear anti-synergies.</p>`}
    <p class="muted small" style="clear:both;margin-top:10px"><a href="#" id="clear-sel">clear selection</a></p>`;
}

async function render() { controls(); await load(); S.sel = null; $("#detail").innerHTML = `<p class="muted small">Click a card to see its best and worst partners. Drag to rearrange, scroll to zoom.</p>`; draw(); store.set("branch", S.branch); store.set("ch", S.ch); }

(async function init() {
  CARDS = await (await fetch("data/cards.json")).json();
  if (!CHARS.includes(S.ch)) S.ch = "ironclad";
  if (!BRANCHES.some((b) => b.id === S.branch)) S.branch = "v0.107.1";
  $("#branch").addEventListener("click", (e) => { if (e.target.dataset.b) { S.branch = e.target.dataset.b; render(); } });
  $("#chars").addEventListener("click", (e) => { if (e.target.dataset.c) { S.ch = e.target.dataset.c; render(); } });
  for (const id of ["#nedges", "#show-pos", "#show-neg"]) $(id).addEventListener("input", draw);
  $("#detail").addEventListener("click", (e) => {
    if (e.target.dataset.pick) { e.preventDefault(); select(e.target.dataset.pick); }
    if (e.target.id === "clear-sel") { e.preventDefault(); S.sel = null; window._graph.node.classed("dim", false); window._graph.link.classed("dim", false); $("#detail").innerHTML = ""; }
  });
  $("#find").addEventListener("keydown", (e) => {
    if (e.key !== "Enter") return;
    const q = e.target.value.trim().toLowerCase();
    const hit = [...BYCARD.keys()].find((id) => name(id).toLowerCase() === q) || [...BYCARD.keys()].find((id) => name(id).toLowerCase().includes(q));
    if (hit) select(hit);
  });
  render();
})();
