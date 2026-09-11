import {test} from 'node:test';
import assert from 'node:assert/strict';
import {comparePeriods} from './comparisons.mjs';
const minute=60000,hour=3600000,week=7*86400000,now=1800000000000;
test('equal elapsed windows, matched minutes and quantified changes',()=>{
  const rows=[];
  for(let i=1;i<=60;i++){const time=now-i*minute;rows.push({time,count:120},{time:time-hour,count:100},{time:time-week,count:80});}
  const result=comparePeriods(rows,now+30000,1);
  assert.equal(result.average,120);assert.equal(result.end,now);
  assert.equal(result.previousPeriod.delta,20);assert.equal(result.previousPeriod.percent,20);
  assert.equal(result.previousWeek.percent,50);assert.equal(result.previousWeek.coverage,1);
});
test('missing minutes excluded symmetrically and current incomplete minute excluded',()=>{
  const result=comparePeriods([{time:now-minute,count:20},{time:now-minute-hour,count:10},{time:now-2*minute,count:1000},{time:now,count:9999}],now,1);
  assert.equal(result.average,510);assert.equal(result.previousPeriod.currentAverage,20);
  assert.equal(result.previousPeriod.delta,10);assert.equal(result.previousPeriod.pairedMinutes,1);
  assert.equal(result.previousPeriod.status,'partial');assert.equal(result.previousWeek.status,'missing');
});
test('real zero stays valid and zero baseline has no percentage',()=>{
  const result=comparePeriods([{time:now-minute,count:0},{time:now-minute-hour,count:0}],now,1);
  assert.equal(result.average,0);assert.equal(result.previousPeriod.delta,0);
  assert.equal(result.previousPeriod.percent,null);assert.equal(result.previousPeriod.status,'zero_baseline');
});
test('30-day comparison uses preceding 30 days and last-week shifted window',()=>{
  const time=now-minute;
  const result=comparePeriods([{time,count:3},{time:time-30*24*hour,count:1},{time:time-week,count:2}],now,720);
  assert.equal(result.previousPeriod.start,result.start-30*24*hour);
  assert.equal(result.previousWeek.start,result.start-week);
  assert.equal(result.previousDay.start,result.start-24*hour);
  assert.equal(result.previousPeriod.delta,2);assert.equal(result.previousWeek.delta,1);
});
