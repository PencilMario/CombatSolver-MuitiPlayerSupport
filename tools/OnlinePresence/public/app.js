const $ = id => document.getElementById(id);
let chart, timer, searchTimer, overviewRequest, playersRequest;
let currentPage = 1;
let pageData;
let resizeTimer;
const verticalGuide = {
  id: 'vertical-guide',
  afterDatasetsDraw(chart) {
    const active = chart.tooltip?.getActiveElements()[0];
    if (!active || chart.data.datasets[active.datasetIndex].data[active.index].y === null) return;
    const x = active.element.x;
    const {top,bottom,left,right} = chart.chartArea;
    if (x < left || x > right) return;
    const ctx = chart.ctx;
    ctx.save();
    ctx.beginPath();
    ctx.rect(left,top,right-left,bottom-top);
    ctx.clip();
    ctx.setLineDash([4,4]);
    ctx.lineWidth = 1;
    ctx.strokeStyle = '#647d73';
    ctx.beginPath();
    ctx.moveTo(x,top);
    ctx.lineTo(x,bottom);
    ctx.stroke();
    ctx.restore();
  },
};

function loggedOut() {
  clearTimeout(timer);
  clearTimeout(searchTimer);
  overviewRequest?.abort();
  playersRequest?.abort();
  $('login').hidden = false;
  $('dashboard').hidden = true;
  $('logout').hidden = true;
  $('connection').textContent = '未登录';
}

async function api(path, options = {}) {
  const response = await fetch(path, {...options, credentials: 'same-origin'});
  if (response.status === 401) { loggedOut(); throw Error('登录已失效'); }
  if (!response.ok) throw Error(response.status === 429 ? '请求过于频繁，请稍后重试' : '服务暂时不可用');
  return response.status === 204 ? null : response.json();
}

function duration(seconds) {
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor(seconds / 3600) % 24;
  const minutes = Math.floor(seconds / 60) % 60;
  if (days) return `${days}天 ${hours}小时 ${minutes}分`;
  if (hours) return `${hours}小时 ${minutes}分`;
  if (minutes) return `${minutes}分 ${seconds % 60}秒`;
  return `${seconds}秒`;
}

function updatePagination(loading = false) {
  $('previous-page').disabled = loading || !pageData || pageData.page <= 1;
  $('next-page').disabled = loading || !pageData || pageData.page >= pageData.totalPages;
}

function renderPlayers(data) {
  pageData = data;
  currentPage = data.page;
  $('rows').replaceChildren();
  $('row-count').textContent = String(data.total);
  $('empty').hidden = data.players.length > 0;
  $('empty').textContent = $('search').value.trim() ? '没有匹配的玩家' : '当前没有在线玩家';
  for (const p of data.players) {
    const row = document.createElement('tr');
    const values = [p.rank, p.name || '未命名玩家', duration(p.onlineSeconds), p.character || '-', p.floor ?? '-', p.encounter || '等待首次计算', p.hpLoss === null ? '-' : `${p.hpLoss} HP`, p.version, `${Math.max(0, Math.floor((data.now - p.lastSeen) / 1000))} 秒前`];
    if (p.battleUpdatedAt !== null) row.title = `战斗数据采集于 ${new Date(p.battleUpdatedAt).toLocaleString()}`;
    for (const [index, value] of values.entries()) {
      const td = document.createElement('td');
      td.textContent = String(value);
      if (index === 6) td.className = p.hpLoss === null ? 'muted' : p.hpLoss === 0 ? 'loss zero' : 'loss';
      if (index === 0) td.className = p.rank <= 3 ? 'rank-leading' : 'muted';
      row.append(td);
    }
    $('rows').append(row);
  }
  const start = data.total ? (data.page - 1) * data.pageSize + 1 : 0;
  const end = data.total ? start + data.players.length - 1 : 0;
  $('page-range').textContent = `${start}–${end} / ${data.total} 位玩家`;
  $('page-number').textContent = `${data.page} / ${data.totalPages}`;
  updatePagination();
}

function renderOverview(data) {
  $('login').hidden = true;
  $('dashboard').hidden = false;
  $('logout').hidden = false;
  $('connection').textContent = '已连接';
  $('error').hidden = true;
  $('online').textContent = String(data.onlineCount);
  $('fighting').textContent = String(data.inRunCount);
  $('fighting').title = `旧版客户端未提供跑局状态：${data.runStatusUnknownCount} 人`;
  $('peak').textContent = String(Math.max(data.onlineCount, data.historyPeak));
  $('updated').textContent = new Date(data.now).toLocaleTimeString('zh-CN');
  const points = [];
  let previous;
  for (const p of data.history) {
    if (previous !== undefined && p.breakBefore) points.push({x: Math.floor((previous+p.time)/2), y: null});
    points.push({x: p.time, y: p.count, start:p.start, end:p.end, samples:p.samples});
    previous = p.time;
  }
  $('history-empty').hidden = points.length > 0;
  if (!chart) {
    chart = new Chart($('chart'), {
      type: 'line',
      plugins: [verticalGuide],
      data: {datasets: [{label: '在线人数', data: points, borderColor: '#237c62', backgroundColor: '#237c6218', fill: true, borderWidth: 2, pointRadius: points.length === 1 ? 3 : 0, pointHitRadius: 10, spanGaps: false, cubicInterpolationMode:'monotone', tension:0.25}]},
      options: {
        animation: false, maintainAspectRatio: false, parsing: false,
        interaction: {mode:'index',axis:'x',intersect:false},
        plugins: {legend: {display: false}, tooltip: {callbacks: {
          title: items => {
            const point = items[0].raw;
            const start = new Date(point.start).toLocaleString('zh-CN');
            return point.start === point.end ? start : `${start} 至 ${new Date(point.end).toLocaleString('zh-CN')}`;
          },
          label: item => `${item.raw.samples > 1 ? '平均在线' : '在线人数'}：${item.parsed.y.toLocaleString('zh-CN',{maximumFractionDigits:1})} 人`,
        }}},
        scales: {
          x: {type: 'linear', min: data.now - Number($('range').value) * 3600000, max: data.now, grid: {display: false}, ticks: {maxTicksLimit: 7, callback: value => new Date(value).toLocaleString('zh-CN', {month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit'})}},
          y: {beginAtZero: true, suggestedMax: 5, ticks: {precision: 0}, grid: {color: '#e5eaed'}},
        },
      },
    });
  } else {
    chart.data.datasets[0].data = points;
    chart.data.datasets[0].pointRadius = points.length === 1 ? 3 : 0;
    chart.options.scales.x.min = data.now - Number($('range').value) * 3600000;
    chart.options.scales.x.max = data.now;
    chart.update();
  }
}

async function refreshOverview() {
  overviewRequest?.abort();
  const request = overviewRequest = new AbortController();
  try {
    const width = $('chart').parentElement.clientWidth || Math.max(240,window.innerWidth-64);
    const maxPoints = Math.max(32,Math.min(240,Math.floor(width/6)));
    const data = await api(`/api/overview?hours=${$('range').value}&maxPoints=${maxPoints}`, {signal: request.signal});
    if (!request.signal.aborted) renderOverview(data);
  } catch (error) {
    if (request.signal.aborted || $('dashboard').hidden) return;
    $('connection').textContent = '连接中断';
    $('error').hidden = false;
    $('error').textContent = `${error.message}，当前显示上次收到的数据。`;
  }
}

async function refreshPlayers(page = currentPage, clearRows = false) {
  playersRequest?.abort();
  const request = playersRequest = new AbortController();
  updatePagination(true);
  if (clearRows) { $('rows').replaceChildren(); $('empty').hidden = false; $('empty').textContent = '加载中…'; }
  try {
    const query = new URLSearchParams({page: String(page), q: $('search').value.trim()});
    const data = await api(`/api/players?${query}`, {signal: request.signal});
    if (request.signal.aborted) return;
    $('players-error').hidden = true;
    renderPlayers(data);
  } catch (error) {
    if (request.signal.aborted || $('dashboard').hidden) return;
    $('players-error').hidden = false;
    currentPage = pageData?.page ?? 1;
    $('players-error').textContent = `${error.message}，玩家列表更新失败。`;
    if (clearRows) $('empty').textContent = '暂时无法加载玩家';
  } finally { if (!request.signal.aborted) updatePagination(); }
}

async function refresh() {
  clearTimeout(timer);
  await Promise.all([refreshOverview(), refreshPlayers()]);
  if (!$('dashboard').hidden) timer = setTimeout(refresh, 10000);
}

$('login-form').addEventListener('submit', async event => {
  event.preventDefault();
  const button = event.submitter;
  button.disabled = true;
  $('login-error').textContent = '';
  try {
    await api('/api/login', {method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify({password: $('password').value})});
    $('password').value = '';
    currentPage = 1;
    await refresh();
  } catch (error) { $('login-error').textContent = error.message; }
  finally { button.disabled = false; }
});
$('logout').addEventListener('click', async () => {
  try { await api('/api/logout', {method: 'POST'}); loggedOut(); }
  catch (error) { $('error').hidden = false; $('error').textContent = error.message; }
});
$('range').addEventListener('change', refreshOverview);
window.addEventListener('resize', () => {
  clearTimeout(resizeTimer);
  if (!$('dashboard').hidden) resizeTimer = setTimeout(refreshOverview,250);
});
$('search').addEventListener('input', () => {
  clearTimeout(searchTimer);
  playersRequest?.abort();
  currentPage = 1;
  updatePagination(true);
  searchTimer = setTimeout(() => refreshPlayers(1, true), 250);
});
$('previous-page').addEventListener('click', () => { currentPage -= 1; refreshPlayers(currentPage, true); });
$('next-page').addEventListener('click', () => { currentPage += 1; refreshPlayers(currentPage, true); });
refresh();
