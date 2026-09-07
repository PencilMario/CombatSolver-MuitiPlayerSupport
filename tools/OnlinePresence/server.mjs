import http from 'node:http';
import https from 'node:https';
import { readFileSync, mkdirSync } from 'node:fs';
import { randomBytes, scryptSync, timingSafeEqual } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

const root = fileURLToPath(new URL('.', import.meta.url));
export const TTL = 90_000;
export function validate(body) {
  if (!body || typeof body !== 'object' || Array.isArray(body)) return false;
  const fields = ['sessionId','name','character','floor','encounter','hpLoss','version'];
  if (Object.keys(body).some(key => !fields.includes(key)) || Object.keys(body).length !== fields.length) return false;
  if (typeof body.sessionId !== 'string' || !/^[a-f0-9]{32}$/.test(body.sessionId)) return false;
  for (const [key, max] of [['name',128],['character',128],['encounter',512],['version',32]]) {
    if (typeof body[key] !== 'string' || body[key].length > max || /[\u0000-\u001f]/.test(body[key])) return false;
  }
  return (body.floor === null || Number.isInteger(body.floor) && body.floor >= 0 && body.floor <= 10000)
    && (body.hpLoss === null || Number.isInteger(body.hpLoss) && body.hpLoss >= 0 && body.hpLoss <= 10000000);
}

export function createApp({ database = ':memory:', password, now = Date.now, secureCookie = false }) {
  if (!password || password.length < 20) throw new Error('ADMIN_PASSWORD must contain at least 20 characters');
  const db = new DatabaseSync(database);
  db.exec('PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; CREATE TABLE IF NOT EXISTS history (time INTEGER PRIMARY KEY, count INTEGER NOT NULL) STRICT;');
  // Personal details are memory-only; the database retains aggregate counts.
  const players = new Map(), sessions = new Map(), buckets = new Map();
  const salt = randomBytes(32), passwordHash = scryptSync(password, salt, 32);
  const allowedHosts = new Set(['127.0.0.1', 'localhost', '[::1]']);
  const historyInsert = db.prepare('INSERT INTO history(time,count) VALUES (?,?) ON CONFLICT(time) DO UPDATE SET count=excluded.count');
  const historyRead = db.prepare('SELECT time,count FROM history WHERE time >= ? ORDER BY time');
  const historyDelete = db.prepare('DELETE FROM history WHERE time < ?');
  function expire() {
    const t = now();
    for (const [key,p] of players) if (p.lastSeen <= t - TTL) players.delete(key);
    for (const [key,s] of sessions) if (s <= t) sessions.delete(key);
    for (const [key,b] of buckets) if (b.until <= t) buckets.delete(key);
  }
  function sample() {
    expire();
    historyInsert.run(Math.floor(now()/60000)*60000, players.size);
    historyDelete.run(now() - 90*86400000);
  }
  function limit(key, max) {
    let b = buckets.get(key);
    if (!b || b.until <= now()) {
      if (buckets.size >= 20000) return false;
      buckets.set(key, b = {until: now()+60000, count: 0});
    }
    return ++b.count <= max;
  }
  function send(res, status, body) {
    res.writeHead(status, {'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});
    res.end(body === undefined ? '' : JSON.stringify(body));
  }
  async function json(req) {
    if (!req.headers['content-type']?.startsWith('application/json')) throw new InputError(415);
    if (Number(req.headers['content-length']) > 4096) throw new InputError(413);
    const chunks = []; let size = 0;
    for await (const chunk of req) {
      size += chunk.length;
      if (size > 4096) throw new InputError(413);
      chunks.push(chunk);
    }
    try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); }
    catch { throw new InputError(400); }
  }
  class InputError extends Error { constructor(status) { super('Invalid request'); this.status = status; } }
  const handle = fn => async (req,res) => {
    try { await fn(req,res); }
    catch (error) {
      if (error instanceof InputError) send(res,error.status,{error:'invalid_request'});
      else { console.error('request_failure',error.code || error.name); send(res,500,{error:'server_error'}); }
    }
  };
  const collector = handle(async (req,res) => {
    if (req.method !== 'POST' || req.url !== '/v1/heartbeat') return send(res,404);
    expire();
    if (!limit('ip:'+req.socket.remoteAddress,240)) return send(res,429);
    const body = await json(req);
    if (!validate(body)) return send(res,400,{error:'invalid_payload'});
    if (!limit('player:'+body.sessionId,6)) return send(res,429);
    if (!players.has(body.sessionId) && players.size >= 10000) return send(res,503);
    players.set(body.sessionId,{...body,lastSeen:now()});
    send(res,204);
  });
  const files = new Map([
    ['/', ['public/index.html','text/html; charset=utf-8']],
    ['/app.js',['public/app.js','text/javascript; charset=utf-8']],
    ['/style.css',['public/style.css','text/css; charset=utf-8']],
    ['/chart.js',['node_modules/chart.js/dist/chart.umd.js','text/javascript; charset=utf-8']],
  ]);
  const admin = handle(async (req,res) => {
    const host = req.headers.host;
    if (!host) return send(res,400);
    let url;
    try { url = new URL(req.url,`http://${host}`); }
    catch { return send(res,400); }
    if (!allowedHosts.has(url.hostname)) return send(res,403);
    if (req.method === 'POST' && req.headers.origin !== `http://${host}`) return send(res,403);
    if (req.method === 'POST' && url.pathname === '/api/login') {
      expire();
      if (!limit('login:'+req.socket.remoteAddress,5)) return send(res,429);
      const body = await json(req);
      if (typeof body?.password !== 'string' || body.password.length > 256) return send(res,400);
      if (!timingSafeEqual(scryptSync(body.password,salt,32),passwordHash)) return send(res,401);
      const token = randomBytes(32).toString('hex');
      sessions.set(token,now()+8*3600000);
      res.setHeader('Set-Cookie',`session=${token}; HttpOnly; SameSite=Strict; Path=/; Max-Age=28800${secureCookie?'; Secure':''}`);
      return send(res,200,{ok:true});
    }
    const token = /(?:^|;\s*)session=([a-f0-9]{64})(?:;|$)/.exec(req.headers.cookie || '')?.[1];
    if (url.pathname.startsWith('/api/')) {
      if (!token || (sessions.get(token) || 0) <= now()) return send(res,401);
      if (req.method === 'POST' && url.pathname === '/api/logout') {
        sessions.delete(token);
        res.setHeader('Set-Cookie','session=; HttpOnly; SameSite=Strict; Path=/; Max-Age=0');
        return send(res,204);
      }
      if (req.method === 'GET' && url.pathname === '/api/overview') {
        expire();
        const hours = Number(url.searchParams.get('hours') || 24);
        if (![1,24,168,720].includes(hours)) return send(res,400);
        return send(res,200,{now:now(),ttl:TTL,players:[...players.values()].sort((a,b)=>b.lastSeen-a.lastSeen),history:historyRead.all(now()-hours*3600000)});
      }
      return send(res,404);
    }
    const file = files.get(url.pathname);
    if (req.method !== 'GET' || !file) return send(res,404);
    res.writeHead(200,{'Content-Type':file[1],'Cache-Control':'no-store','X-Content-Type-Options':'nosniff','Referrer-Policy':'no-referrer','Content-Security-Policy':"default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'"});
    res.end(readFileSync(resolve(root,file[0])));
  });
  return {collector,admin,sample,expire,close:()=>db.close()};
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  mkdirSync(resolve(root,'data'),{recursive:true,mode:0o700});
  const app = createApp({database:process.env.DATABASE_PATH || resolve(root,'data/presence.sqlite'),password:process.env.ADMIN_PASSWORD});
  const admin = http.createServer({requestTimeout:10000,headersTimeout:10000,maxHeaderSize:8192},app.admin);
  admin.maxConnections = 30;
  admin.listen(Number(process.env.ADMIN_PORT || 12889),'127.0.0.1');
  const collector = https.createServer({key:readFileSync(process.env.TLS_KEY),cert:readFileSync(process.env.TLS_CERT),minVersion:'TLSv1.2',requestTimeout:10000,headersTimeout:10000,maxHeaderSize:8192},app.collector);
  collector.maxConnections = 200;
  collector.listen(Number(process.env.COLLECTOR_PORT || 12888),'0.0.0.0');
  app.sample();
  const timer = setInterval(app.sample,60000);
  const expiry = setInterval(app.expire,10000);
  for (const signal of ['SIGINT','SIGTERM']) process.on(signal,()=>{
    clearInterval(timer); clearInterval(expiry);
    admin.close(); collector.close(); admin.closeAllConnections(); collector.closeAllConnections(); app.close();
  });
  console.log('Presence service started');
}
