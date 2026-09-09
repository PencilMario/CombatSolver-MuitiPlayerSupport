import {test,before,after} from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import {once} from 'node:events';
import {createRequire} from 'node:module';
import {mkdirSync} from 'node:fs';
import {resolve} from 'node:path';
import {createApp} from './server.mjs';
const {chromium}=createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE || 'playwright');
let browser,server,app,origin;
const screenshots=resolve(process.env.SCREENSHOT_DIR || '../../.local/workbench-ui');
before(async()=>{
  mkdirSync(screenshots,{recursive:true});
  browser=await chromium.launch({headless:true,...(process.env.BROWSER_CHANNEL?{channel:process.env.BROWSER_CHANNEL}:{})});
  app=createApp({password:'local-browser-fixture-password-012345'});
  server=http.createServer(app.admin).listen(0,'127.0.0.1');await once(server,'listening');origin=`http://127.0.0.1:${server.address().port}`;
});
after(async()=>{await browser?.close();if(server){server.closeAllConnections();await new Promise(r=>server.close(r));}app?.close();});
const installation=n=>n.toString(16).padStart(32,'0');
const players=Array.from({length:15},(_,i)=>({sessionId:installation(i+1),name:i===0?'长昵称'.repeat(42)+'ab':'玩家 '+(i+1),character:['铁甲战士','静默猎手','故障机器人'][i%3],floor:i+1,encounter:i===0?'超长战斗'.repeat(128):'六火亡魂、邪恶之眼',hpLoss:i===2?null:i===3?99999:i,version:'0.34.7',inCombat:i%3===0,inRun:i%3===1?true:i%3===0?true:false,lastSeen:1800000000000,onlineSeconds:3600*(i+1),rank:i+1,battleUpdatedAt:1800000000000,runStatistics:{profileId:installation(i+101)}}));
function runs(params){const historical=params.get('source')==='historical',target=params.get('profileId');const entries=(target?[players.find(p=>p.runStatistics.profileId===target)].filter(Boolean):players.slice(0,4)).map((p,i)=>({sessionId:p.sessionId,profileId:p.runStatistics.profileId,name:p.name,online:i%2===0,statistics:{wins:20,losses:5,abandoned:historical?null:2,currentStreak:historical&&!params.get('character')?null:i,bestStreak:historical&&!params.get('character')?null:8,completedRuns:25,winRate:.8}}));return {now:1800000000000,source:historical?'historical':'solver',page:1,totalPages:1,pageSize:30,total:entries.length,wins:entries.length*20,losses:entries.length*5,winRate:entries.length?.8:null,entries};}
async function setup(options={}){
  const context=await browser.newContext({viewport:{width:1440,height:900},...options.context});
  const page=await context.newPage(),traffic=[],errors=[];page.on('pageerror',e=>errors.push(e.message));
  const controls={mode:'success',empty:false,failRuns:false,...options};
  await page.route('**/api/**',async route=>{
    const url=new URL(route.request().url());traffic.push(url);
    if(url.pathname==='/api/run-statistics'&&controls.runGate)await controls.runGate(url);
    if(controls.block)await controls.block;
    if(controls.mode==='network'){await route.abort();return;}
    if(controls.mode==='unauthorized'){await route.fulfill({status:401,body:'{}'});return;}
    if(url.pathname==='/api/run-statistics'&&controls.failRuns){await route.fulfill({status:500,body:'{}'});return;}
    let data;
    if(url.pathname==='/api/overview')data={now:1800000000000,onlineCount:15,inRunCount:10,fightingCount:5,runStatusUnknownCount:0,historyPeak:20,history:[{time:1799996400000,count:4,start:1799996400000,end:1799996400000,samples:1},{time:1800000000000,count:15,start:1800000000000,end:1800000000000,samples:1}]};
    else if(url.pathname==='/api/players')data={now:1800000000000,page:1,totalPages:1,pageSize:30,total:controls.empty?0:15,players:controls.empty?[]:players};
    else if(url.pathname==='/api/run-statistics')data=controls.empty?{...runs(url.searchParams),entries:[],total:0,wins:0,losses:0,winRate:null}:runs(url.searchParams);
    else data={ok:true};
    await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(data)});
  });
  await page.goto(origin,{waitUntil:'domcontentloaded'});
  return {context,page,traffic,errors,controls};
}
test('real DOM auth restoration hides login until 401 and offers retry for network failure',async()=>{
  for(const mode of ['success','unauthorized','network']){
    let release;const block=new Promise(r=>release=r);const fixture=await setup({mode,block});
    try{assert.equal(await fixture.page.locator('#login').isVisible(),false);release();
      if(mode==='success')await fixture.page.locator('#rows tr').first().waitFor();
      else if(mode==='unauthorized')await fixture.page.locator('#login').waitFor();
      else await fixture.page.locator('#session-retry').waitFor();
      assert.equal(await fixture.page.locator('#login').isVisible(),mode==='unauthorized');assert.deepEqual(fixture.errors,[]);
    }finally{await fixture.context.close();}
  }
});
test('compact live layout, accurate metrics, stable rows and full-text details',async()=>{
  const f=await setup();try{
    const {page}=f;await page.locator('#rows tr').first().waitFor();
    assert.equal(await page.locator('#in-combat').textContent(),'5');assert.equal(await page.locator('#in-run').textContent(),'10');
    assert.equal(await page.locator('#trend').getAttribute('open'),null);
    assert.ok((await page.locator('#live-table').boundingBox()).y<350);
    const heights=await page.locator('#rows tr').evaluateAll(rows=>rows.map(r=>r.getBoundingClientRect().height));assert.ok(Math.max(...heights)<=52,JSON.stringify(heights));
    assert.ok((await page.locator('#rows').textContent()).includes('主菜单'));
    await page.evaluate(()=>window.savedRow=document.querySelector('#rows tr'));
    await page.locator('#refresh-now').click();await page.waitForFunction(()=>!requests.has('live'));
    assert.ok(await page.evaluate(()=>window.savedRow===document.querySelector('#rows tr')));
    await page.screenshot({path:resolve(screenshots,'live-desktop.png'),fullPage:true});
    await page.locator('#rows tr').first().getByRole('button',{name:'详情',exact:true}).click();
    assert.ok((await page.locator('#details-content').textContent()).includes(players[0].encounter));await page.locator('#close-details').click();
    await page.locator('#trend summary').click();await page.waitForFunction(()=>Boolean(chart));
    await page.locator('#trend summary').click();
    assert.deepEqual(f.errors,[]);
  }finally{await f.context.close();}
});
test('applied filters, explicit sample gate, source switching, scoped lookup and reload',async()=>{
  const f=await setup();try{
    const {page}=f;await page.locator('#rows tr').first().waitFor();await page.locator('#tab-runs').click();await page.locator('#run-rows tr').first().waitFor();
    assert.equal(f.traffic.find(u=>u.pathname==='/api/run-statistics').searchParams.get('runs_min'),'5');
    assert.ok((await page.locator('#active-filters').textContent()).includes('至少完成局数：5'));
    assert.ok((await page.locator('#run-rows').textContent()).includes('样本：25 局'));
    await page.locator('[name=runs_min]').fill('10');assert.equal(await page.locator('#dirty-note').isVisible(),true);
    const before=f.traffic.filter(u=>u.pathname==='/api/run-statistics').length;
    await page.evaluate(()=>{document.activeElement.blur();attempts.set('runs',0);tick();});
    assert.equal(f.traffic.filter(u=>u.pathname==='/api/run-statistics').length,before);
    await page.locator('#apply-filters').click();await page.waitForFunction(()=>!requests.has('runs'));assert.equal(await page.locator('#dirty-note').isVisible(),false);
    await page.getByRole('button',{name:'移除至少完成局数条件'}).click();await page.waitForFunction(()=>!requests.has('runs'));assert.equal(await page.locator('[name=runs_min]').inputValue(),'');
    await page.locator('[name=source]').selectOption('historical');assert.equal(await page.locator('[name=participation]').isVisible(),false);
    await page.locator('#apply-filters').click();await page.waitForFunction(()=>!requests.has('runs'));assert.ok((await page.locator('#run-rows').textContent()).includes('跨角色未计'));
    await page.locator('#run-filters button[type=reset]').click();await page.waitForFunction(()=>!requests.has('runs')&&document.querySelector('[name=source]').value==='solver');assert.equal(await page.locator('[name=participation]').isEnabled(),true);
    await page.locator('#tab-live').click();await page.waitForFunction(()=>!requests.has('live'));await page.locator('#rows tr').first().getByRole('button',{name:'战绩',exact:true}).click();await page.waitForFunction(()=>!requests.has('runs'));
    const last=f.traffic.filter(u=>u.pathname==='/api/run-statistics').at(-1);assert.equal(last.searchParams.get('sessionId'),players[0].sessionId);assert.equal(last.searchParams.get('profileId'),players[0].runStatistics.profileId);assert.equal(last.searchParams.has('runs_min'),false);
    await page.screenshot({path:resolve(screenshots,'runs-desktop.png'),fullPage:true});
    await page.reload();await page.locator('#run-rows tr').first().waitFor();assert.equal(await page.locator('#panel-runs').isVisible(),true);assert.equal(await page.locator('[name=profileId]').inputValue(),players[0].runStatistics.profileId);
    assert.deepEqual(f.errors,[]);
  }finally{await f.context.close();}
});
test('pause, selection, error preservation and empty states',async()=>{
  const f=await setup();try{
    const {page}=f;await page.locator('#rows tr').first().waitFor();await page.locator('#auto-refresh').click();
    let before=f.traffic.length;await page.evaluate(()=>{attempts.clear();tick();});assert.equal(f.traffic.length,before);
    await page.locator('#auto-refresh').click();await page.waitForFunction(()=>!requests.has('live')&&!requests.has('overview'));
    await page.evaluate(()=>{const range=document.createRange();range.selectNodeContents(document.querySelector('#rows tr td:nth-child(2)'));const selection=window.getSelection();selection.removeAllRanges();selection.addRange(range);});
    before=f.traffic.length;await page.evaluate(()=>{attempts.clear();tick();});assert.equal(f.traffic.length,before);
    await page.evaluate(()=>window.getSelection().removeAllRanges());await page.locator('#tab-runs').click();await page.locator('#run-rows tr').first().waitFor();
    const summary=await page.locator('#run-summary').textContent();f.controls.failRuns=true;
    await page.locator('[name=runs_min]').fill('20');await page.locator('#apply-filters').click();await page.locator('#run-error').waitFor();assert.equal(await page.locator('#run-summary').textContent(),summary);assert.ok((await page.locator('#active-filters').textContent()).includes('至少完成局数：5'));
    f.controls.failRuns=false;f.controls.empty=true;await page.locator('#apply-filters').click();await page.locator('#run-empty').waitFor();assert.equal(await page.locator('#run-rows tr').count(),0);
    assert.deepEqual(f.errors,[]);
  }finally{await f.context.close();}
});
test('equivalent 125%, 150% and narrow-window layouts contain horizontal overflow inside tables',async()=>{
  for(const [width,scale] of [[1024,1.25],[854,1.5],[390,1]]){
    const f=await setup({context:{viewport:{width,height:800},deviceScaleFactor:scale}});try{
      await f.page.locator('#rows tr').first().waitFor();await f.page.locator('#tab-runs').click();await f.page.locator('#run-rows tr').first().waitFor();
      assert.ok(await f.page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
      await f.page.screenshot({path:resolve(screenshots,`runs-${width}.png`),fullPage:true});assert.deepEqual(f.errors,[]);
    }finally{await f.context.close();}
  }
});
test('a delayed previous filter response cannot overwrite the newest applied selection',async()=>{
  const f=await setup();try{
    const {page}=f;await page.locator('#rows tr').first().waitFor();await page.locator('#tab-runs').click();await page.locator('#run-rows tr').first().waitFor();
    let release,started;const gate=new Promise(r=>release=r),waiting=new Promise(r=>started=r);
    f.controls.runGate=async url=>{if(url.searchParams.get('runs_min')==='10'){started();await gate;}};
    await page.locator('[name=runs_min]').fill('10');await page.locator('#apply-filters').click();await waiting;
    await page.locator('[name=runs_min]').fill('20');await page.locator('#apply-filters').click();await page.waitForFunction(()=>!requests.has('runs'));
    release();await new Promise(r=>setImmediate(r));
    assert.ok((await page.locator('#active-filters').textContent()).includes('至少完成局数：20'));
    assert.equal(new URL(page.url()).searchParams.get('run_runs_min'),'20');assert.deepEqual(f.errors,[]);
  }finally{await f.context.close();}
});
