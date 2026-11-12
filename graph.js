"use strict";
const BRANCHES = [{ id: "v0.107.1", label: "Live" }, { id: "v0.111.0", label: "Beta" }];
const CHARS = ["ironclad", "silent", "defect", "regent", "necrobinder"];
const $ = (s) => document.querySelector(s);
const store = {
  get(k, d) { try { const v = localStorage.getItem("spirewise." + k); return v ? JSON.parse(v) : d; } catch { return d; } },
  set(k, v) { try { localStorage.setItem("spirewise." + k, JSON.stringify(v)); } catch {} },
};
const S = { branch: store.get("branch", "v0.107.1"), ch: store.get("ch", "ironclad"), sel: null, view: "focus" };
let CARDS = {}, D = null, PAIRS = [], BYCARD = new Map(), PAIRW = new Map();
const pairW = (a, b) => PAIRW.get(a < b ? a + "|" + b : b + "|" + a);
// The deck entered in the Card advisor (same browser storage), minus basics and curses.
const myDeck = () => new Set(Object.keys((store.get("decks", {}) || {})[S.ch] || {}).filter((id) => !skip(id)));
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
  PAIRW = new Map(PAIRS.map((e) => [e.source + "|" + e.target, e.w]));
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
  const zoom = d3.zoom().scaleExtent([0.2, 4]).on("zoom", (ev) => g.attr("transform", ev.transform));
  svg.call(zoom);
  const defs = svg.append("defs");
  nodes.forEach((d, i) => { d.clip = "c" + i; defs.append("clipPath").attr("id", d.clip).append("circle").attr("r", d.r - 2); });

  const link = g.append("g").selectAll("path").data(edges).join("path").attr("class", "edge")
    .attr("stroke", (e) => (e.w > 0 ? "var(--good)" : "var(--bad)"))
    .attr("stroke-width", (e) => 0.8 + 7 * (Math.abs(e.w) / wmax) ** 1.3).attr("stroke-opacity", (e) => 0.32 + 0.68 * (Math.abs(e.w) / wmax) ** 1.3)
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
  node.classed("hidelabel", (d) => d.r < 24);
  node.append("title").text((d) => `${name(d.id)} · own value ${(d.v * 100).toFixed(1)}%`);

  sim?.stop();
  sim = d3.forceSimulation(nodes)
    .force("link", d3.forceLink(edges).id((d) => d.id).distance((e) => 150 - 60 * Math.abs(e.w) / wmax).strength(0.5))
    .force("charge", d3.forceManyBody().strength(-380))
    .force("center", d3.forceCenter(width / 2, height / 2))
    .force("x", d3.forceX(width / 2).strength(0.06)).force("y", d3.forceY(height / 2).strength(0.06))
    .force("collide", d3.forceCollide().radius((d) => d.r + 16))
    .on("tick", () => {
      link.attr("d", (e) => `M${e.source.x},${e.source.y}L${e.target.x},${e.target.y}`);
      node.attr("transform", (d) => `translate(${d.x},${d.y})`);
    })
    .on("end", () => {   // fit everything in view
      const xs = nodes.map((d) => d.x), ys = nodes.map((d) => d.y);
      const x0 = Math.min(...xs) - 50, x1 = Math.max(...xs) + 50, y0 = Math.min(...ys) - 50, y1 = Math.max(...ys) + 60;
      const k = Math.min(width / (x1 - x0), height / (y1 - y0), 1.4);
      svg.transition().duration(500).call(zoom.transform, d3.zoomIdentity.translate(width / 2 - k * (x0 + x1) / 2, height / 2 - k * (y0 + y1) / 2).scale(k));
    });
  window._graph = { node, link };
  if (S.sel) highlight(S.sel);
}

// Focus view: chosen card in the centre, best partners fanned out on the right, worst on the left.
function drawFocus() {
  const id = S.sel; if (!id) return;
  const svg = d3.select("#svg"); svg.selectAll("*").remove();
  const { width: W, height: H } = $("#stage").getBoundingClientRect();
  const narrow = W < 600;
  const cx = W / 2, cy = H / 2, R = Math.min(W * (narrow ? 0.38 : 0.36), H * 0.40);
  const rC = narrow ? 36 : 52, rP = narrow ? 21 : 30;
  const list = (BYCARD.get(id) || []).slice().sort((a, b) => b.w - a.w);
  const deck = myDeck();
  const withDeck = (arr, extra, cap) => { const ids = new Set(arr.map((x) => x.id)); return [...arr, ...extra.filter((x) => !ids.has(x.id))].slice(0, cap); };
  const posAll = list.filter((x) => x.w > 0), negAll = list.filter((x) => x.w < 0).reverse();
  const pos = withDeck(posAll.slice(0, narrow ? 5 : 7), posAll.filter((x) => deck.has(x.id) && x.w >= 0.015), narrow ? 6 : 9)
    .sort((a, b) => b.w - a.w);
  const neg = withDeck(negAll.slice(0, narrow ? 3 : 5), negAll.filter((x) => deck.has(x.id) && x.w <= -0.015), narrow ? 4 : 6)
    .sort((a, b) => a.w - b.w);
  const place = (arr, a0, a1) => arr.map((x, i) => {
    const a = arr.length === 1 ? (a0 + a1) / 2 : a0 + (a1 - a0) * i / (arr.length - 1);
    return { ...x, x: cx + R * Math.cos(a), y: cy + R * Math.sin(a) };
  });
  const P = [...place(pos, -Math.PI * 0.42, Math.PI * 0.42), ...place(neg, Math.PI * 1.32, Math.PI * 0.68)];
  const wmax = Math.max(0.01, ...P.map((p) => Math.abs(p.w)));
  const g = svg.append("g");
  svg.call(d3.zoom().scaleExtent([0.5, 3]).on("zoom", (ev) => g.attr("transform", ev.transform)));
  const defs = svg.append("defs");
  const cardNode = (sel, d, r, i) => {
    defs.append("clipPath").attr("id", "f" + i).append("circle").attr("r", r - 2);
    const n = sel.append("g").attr("class", "node").attr("transform", `translate(${d.x},${d.y})`);
    if (deck.has(d.id)) n.append("circle").attr("r", r + 5).attr("fill", "none").attr("stroke", "var(--gold)").attr("stroke-width", 2.5).attr("stroke-dasharray", "4 3");
    n.append("circle").attr("class", "ring").attr("r", r).attr("stroke", value(d.id) >= 0 ? "var(--good)" : "var(--bad)");
    if (img(d.id)) n.append("image").attr("href", img(d.id)).attr("width", r * 2.9).attr("height", r * 4.06)
      .attr("x", -r * 1.45).attr("y", -r * 1.45).attr("clip-path", `url(#f${i})`).attr("preserveAspectRatio", "xMidYMin slice");
    n.append("text").attr("y", r + 15).text((deck.has(d.id) ? "★ " : "") + name(d.id));
    return n;
  };
  const links = g.append("g");
  P.forEach((p) => {
    links.append("path").attr("class", "edge").attr("d", `M${cx},${cy}L${p.x},${p.y}`)
      .attr("stroke", p.w > 0 ? "var(--good)" : "var(--bad)").attr("stroke-opacity", 0.22 + 0.78 * (Math.abs(p.w) / wmax) ** 1.3)
      .attr("stroke-width", 1 + 9 * (Math.abs(p.w) / wmax) ** 1.3).attr("stroke-dasharray", p.w > 0 ? null : "7 5");
    const mx = cx + (p.x - cx) * (narrow ? 0.62 : 0.55), my = cy + (p.y - cy) * (narrow ? 0.62 : 0.55);
    links.append("text").attr("class", "elabel").attr("x", mx).attr("y", my - 6).attr("text-anchor", "middle")
      .attr("fill", p.w > 0 ? "var(--good)" : "var(--bad)").text(odds(p.w).replace(" odds", ""));
  });
  const nodes = g.append("g");
  P.forEach((p, i) => cardNode(nodes, p, rP, i).on("click", () => select(p.id)).append("title").text(`${name(p.id)}: ${odds(p.w)} together`));
  cardNode(nodes, { id, x: cx, y: cy }, rC, "c");
  if (narrow) g.selectAll(".node text").attr("font-size", 10);
  g.append("text").attr("x", cx + R * 0.5).attr("y", 26).attr("text-anchor", "middle").attr("fill", "var(--good)").attr("font-size", 14).text("wins more with →");
  g.append("text").attr("x", cx - R * 0.5).attr("y", 26).attr("text-anchor", "middle").attr("fill", "var(--bad)").attr("font-size", 14).text("← loses more with");
  if (deck.size) g.append("text").attr("x", cx).attr("y", H - 14).attr("text-anchor", "middle").attr("fill", "var(--gold)").attr("font-size", 13).text("★ gold ring = already in your deck");
  if (!P.length) g.append("text").attr("x", cx).attr("y", cy + 90).attr("text-anchor", "middle").attr("fill", "var(--muted)").text("No partners with enough data for this card.");
}
// "Your deck" view: deck cards on the outer ring, best additions inside, links among all of them.
function drawDeck() {
  const svg = d3.select("#svg"); svg.selectAll("*").remove();
  const { width: W, height: H } = $("#stage").getBoundingClientRect();
  const deck = [...myDeck()].filter((id) => BYCARD.has(id));
  if (!deck.length) {
    svg.append("text").attr("x", W / 2).attr("y", H / 2).attr("text-anchor", "middle").attr("fill", "var(--muted)")
      .text("No deck yet. Add cards in the Card advisor, then come back.");
    $("#detail").innerHTML = `<p class="muted small">Build your deck on the <a href="index.html">Card advisor</a> page; this view reads it from your browser.</p>`;
    return;
  }
  const narrow = W < 600, cx = W / 2, cy = H / 2, R = Math.min(W, H) * (narrow ? 0.40 : 0.40);
  const rD = narrow ? 18 : 26, rA = narrow ? 22 : 32;
  // candidates: summed association with the cards you already have
  const cand = [...BYCARD.keys()].filter((id) => !deck.includes(id)).map((id) => {
    const parts = deck.map((p) => ({ p, w: pairW(id, p) || 0 })).filter((x) => x.w);
    return { id, total: parts.reduce((s, x) => s + x.w, 0), parts };
  }).filter((c) => c.total > 0).sort((a, b) => b.total - a.total).slice(0, narrow ? 4 : 6);
  const nodes = [
    ...deck.map((id, i) => ({ id, r: rD, inDeck: true, x: cx + R * Math.cos(2 * Math.PI * i / deck.length - Math.PI / 2), y: cy + R * Math.sin(2 * Math.PI * i / deck.length - Math.PI / 2) })),
    ...cand.map((c, i) => ({ id: c.id, r: rA, inDeck: false, total: c.total, x: cx + R * 0.42 * Math.cos(2 * Math.PI * i / cand.length - Math.PI / 2), y: cy + R * 0.42 * Math.sin(2 * Math.PI * i / cand.length - Math.PI / 2) })),
  ];
  const at = new Map(nodes.map((n) => [n.id, n]));
  // Keep it readable: each addition's 3 strongest links into your deck, plus the 8 strongest pairs within your deck.
  const edges = [];
  for (const c of cand) c.parts.filter((x) => x.w > 0).sort((x, y) => y.w - x.w).slice(0, 3)
    .forEach((x) => edges.push({ a: at.get(c.id), b: at.get(x.p), w: x.w }));
  const inner = [];
  for (let i = 0; i < deck.length; i++) for (let k = i + 1; k < deck.length; k++) {
    const w = pairW(deck[i], deck[k]); if (w && Math.abs(w) >= 0.012) inner.push({ a: at.get(deck[i]), b: at.get(deck[k]), w });
  }
  edges.push(...inner.sort((x, y) => Math.abs(y.w) - Math.abs(x.w)).slice(0, 8));
  const wmax = Math.max(0.01, ...edges.map((e) => Math.abs(e.w)));
  const g = svg.append("g");
  svg.call(d3.zoom().scaleExtent([0.5, 3]).on("zoom", (ev) => g.attr("transform", ev.transform)));
  const defs = svg.append("defs");
  g.append("g").selectAll("path").data(edges).join("path").attr("class", "edge")
    .attr("d", (e) => { const mx = (e.a.x + e.b.x) / 2, my = (e.a.y + e.b.y) / 2; return `M${e.a.x},${e.a.y}Q${mx + (cx - mx) * 0.35},${my + (cy - my) * 0.35} ${e.b.x},${e.b.y}`; })
    .attr("stroke", (e) => (e.w > 0 ? "var(--good)" : "var(--bad)")).attr("stroke-dasharray", (e) => (e.w > 0 ? null : "6 4"))
    .attr("stroke-width", (e) => 0.8 + 7 * (Math.abs(e.w) / wmax) ** 1.3).attr("stroke-opacity", (e) => 0.3 + 0.7 * (Math.abs(e.w) / wmax) ** 1.3)
    .append("title").text((e) => `${name(e.a.id)} + ${name(e.b.id)}: ${odds(e.w)}`);
  const node = g.append("g").selectAll("g").data(nodes).join("g").attr("class", "node").attr("transform", (d) => `translate(${d.x},${d.y})`)
    .on("click", (ev, d) => { S.view = "focus"; redraw(); select(d.id); });
  node.filter((d) => d.inDeck).append("circle").attr("r", (d) => d.r + 5).attr("fill", "none").attr("stroke", "var(--gold)").attr("stroke-width", 2.5).attr("stroke-dasharray", "4 3");
  node.append("circle").attr("class", "ring").attr("r", (d) => d.r).attr("stroke", (d) => (value(d.id) >= 0 ? "var(--good)" : "var(--bad)"));
  node.each(function (d, i) {
    defs.append("clipPath").attr("id", "d" + i).append("circle").attr("r", d.r - 2);
    if (img(d.id)) d3.select(this).append("image").attr("href", img(d.id)).attr("width", d.r * 2.9).attr("height", d.r * 4.06)
      .attr("x", -d.r * 1.45).attr("y", -d.r * 1.45).attr("clip-path", `url(#d${i})`).attr("preserveAspectRatio", "xMidYMin slice");
  });
  node.append("text").attr("y", (d) => d.r + 14).attr("font-size", narrow ? 10 : 11).text((d) => (d.inDeck ? "★ " : "+ ") + name(d.id));
  node.append("title").text((d) => d.inDeck ? `${name(d.id)} (in your deck)` : `${name(d.id)}: ${odds(d.total)} with your deck`);
  g.append("text").attr("x", cx).attr("y", cy + 4).attr("text-anchor", "middle").attr("fill", "var(--muted)").attr("font-size", 12).text(cand.length ? "best additions" : "");

  // side panel
  const links = inner.sort((x, y) => y.w - x.w);
  const row = (a, b, w) => `<tr><td>${name(a)} + ${name(b)}</td><td class="num ${w >= 0 ? "pos" : "neg"}">${odds(w)}</td></tr>`;
  $("#detail").innerHTML = `<h2>Your deck</h2><p class="muted small">${deck.length} non-basic cards from the Card advisor. Click any card to focus on it.</p>
    <h3>Best additions <span class="muted small">(combined with your deck)</span></h3>${cand.length ? `<table>${cand.map((c) => `<tr><td><a href="#" data-pick="${c.id}">${name(c.id)}</a><div class="muted small">with ${c.parts.filter((x) => x.w > 0).sort((a, b) => b.w - a.w).slice(0, 3).map((x) => name(x.p)).join(", ")}</div></td><td class="num pos">${odds(c.total)}</td></tr>`).join("")}</table>` : `<p class="muted small">No strong additions found.</p>`}
    <h3>Strongest pairs already in your deck</h3>${links.filter((e) => e.w > 0).length ? `<table>${links.filter((e) => e.w > 0).slice(0, 5).map((e) => row(e.a.id, e.b.id, e.w)).join("")}</table>` : `<p class="muted small">None above the threshold.</p>`}
    <h3>Pairs working against each other</h3>${links.filter((e) => e.w < 0).length ? `<table>${links.filter((e) => e.w < 0).reverse().slice(0, 4).map((e) => row(e.a.id, e.b.id, e.w)).join("")}</table>` : `<p class="muted small">None above the threshold.</p>`}`;
}
function renderPicks() {
  // quick picks: the most valuable cards that have partners
  const deck = [...myDeck()].filter((id) => BYCARD.has(id)).sort((a, b) => name(a).localeCompare(name(b)));
  const top = [...BYCARD.keys()].filter((id) => (BYCARD.get(id) || []).length >= 4 && !deck.includes(id)).sort((a, b) => value(b) - value(a)).slice(0, deck.length ? 6 : 12);
  const btn = (id) => `<button data-q="${id}" class="${id === S.sel ? "on" : ""}">${name(id)}</button>`;
  $("#picks").innerHTML = (deck.length ? `<span class="muted small">Your deck:</span>${deck.map(btn).join("")}<span class="muted small" style="margin-left:8px">Top cards:</span>` : "") + top.map(btn).join("");
}
function redraw() {
  document.body.classList.toggle("focus", S.view !== "overview");
  [...document.querySelectorAll("#view button")].forEach((b) => b.classList.toggle("on", b.dataset.v === S.view));
  $("#picks").style.display = S.view === "focus" ? "" : "none";
  if (S.view === "focus") { renderPicks(); drawFocus(); if (S.sel) select(S.sel); } else if (S.view === "deck") drawDeck(); else draw();
}

function highlight(id) {
  const G = window._graph; if (!G) return;
  const nb = new Set([id, ...(BYCARD.get(id) || []).map((x) => x.id)]);
  G.node.classed("dim", (d) => !nb.has(d.id));
  G.link.classed("dim", (e) => e.source.id !== id && e.target.id !== id);
}
function select(id) {
  S.sel = id;
  if (S.view === "focus") { renderPicks(); drawFocus(); } else highlight(id);
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

async function render() {
  controls(); await load();
  const keep = S.sel && BYCARD.has(S.sel);
  S.sel = keep ? S.sel : [...BYCARD.keys()].filter((id) => BYCARD.get(id).length >= 4).sort((a, b) => value(b) - value(a))[0];
  redraw(); select(S.sel);
  store.set("branch", S.branch); store.set("ch", S.ch);
}

(async function init() {
  CARDS = await (await fetch("data/cards.json")).json();
  if (!CHARS.includes(S.ch)) S.ch = "ironclad";
  if (!BRANCHES.some((b) => b.id === S.branch)) S.branch = "v0.107.1";
  $("#branch").addEventListener("click", (e) => { if (e.target.dataset.b) { S.branch = e.target.dataset.b; render(); } });
  $("#chars").addEventListener("click", (e) => { if (e.target.dataset.c) { S.ch = e.target.dataset.c; render(); } });
  for (const id of ["#nedges", "#show-pos", "#show-neg"]) $(id).addEventListener("input", draw);
  $("#view").addEventListener("click", (e) => { if (e.target.dataset.v) { S.view = e.target.dataset.v; redraw(); if (S.sel && S.view === "overview") highlight(S.sel); } });
  $("#picks").addEventListener("click", (e) => { if (e.target.dataset.q) select(e.target.dataset.q); });
  window.addEventListener("resize", () => { if (S.view === "focus") drawFocus(); else if (S.view === "deck") drawDeck(); });
  $("#detail").addEventListener("click", (e) => {
    if (e.target.dataset.pick) { e.preventDefault(); select(e.target.dataset.pick); }
    if (e.target.id === "clear-sel") { e.preventDefault(); if (window._graph) { window._graph.node.classed("dim", false); window._graph.link.classed("dim", false); } }
  });
  $("#find").addEventListener("keydown", (e) => {
    if (e.key !== "Enter") return;
    const q = e.target.value.trim().toLowerCase();
    const hit = [...BYCARD.keys()].find((id) => name(id).toLowerCase() === q) || [...BYCARD.keys()].find((id) => name(id).toLowerCase().includes(q));
    if (hit) select(hit);
  });
  render();
})();
