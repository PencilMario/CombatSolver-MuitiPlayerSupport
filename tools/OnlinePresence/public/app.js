const $ = id => document.getElementById(id);
let chart, overview, timer, fetching = false;
function loggedOut(){ clearTimeout(timer); $('login').hidden=false; $('dashboard').hidden=true; $('logout').hidden=true; $('connection').textContent='未登录'; }
async function api(path,options={}){const response=await fetch(path,{...options,credentials:'same-origin'});if(response.status===401){loggedOut();throw Error('登录已失效');}if(!response.ok)throw Error(response.status===429?'请求过于频繁，请稍后重试':'服务暂时不可用');return response.status===204?null:response.json();}
function renderPlayers(){
  const term=$('search').value.toLocaleLowerCase();
  const players=overview.players.filter(p=>[p.name,p.character,p.encounter].some(v=>v.toLocaleLowerCase().includes(term)));
  $('rows').replaceChildren(); $('row-count').textContent=String(players.length);
  $('empty').hidden=players.length>0; $('empty').textContent=term?'没有匹配的玩家':'当前没有在线玩家';
  for(const p of players){
    const row=document.createElement('tr');
    for(const [index,value] of [p.name||'未命名玩家',p.character||'主菜单',p.floor??'-',p.encounter||'非战斗',p.hpLoss===null?'未知':`${p.hpLoss} HP`,p.version,`${Math.max(0,Math.floor((overview.now-p.lastSeen)/1000))} 秒前`].entries()){
      const td=document.createElement('td');td.textContent=String(value);if(index===4)td.className=p.hpLoss===null?'muted':p.hpLoss===0?'loss zero':'loss';row.append(td);
    }$('rows').append(row);
  }
}
function render(data){
  overview=data; $('login').hidden=true;$('dashboard').hidden=false;$('logout').hidden=false;
  $('connection').textContent='已连接';$('error').hidden=true;
  $('online').textContent=String(data.players.length);$('fighting').textContent=String(data.players.filter(p=>p.encounter).length);
  $('peak').textContent=String(Math.max(data.players.length,...data.history.map(p=>p.count),0));
  $('updated').textContent=new Date(data.now).toLocaleTimeString('zh-CN');renderPlayers();
  const points=[];let previous;
  for(const p of data.history){if(previous!==undefined&&p.time-previous>90000)points.push({x:previous+60000,y:null});points.push({x:p.time,y:p.count});previous=p.time;}
  $('history-empty').hidden=points.length>0;
  if(!chart){chart=new Chart($('chart'),{type:'line',data:{datasets:[{label:'在线人数',data:points,borderColor:'#237c62',backgroundColor:'#237c6218',fill:true,borderWidth:2,pointRadius:0,pointHitRadius:10,spanGaps:false,tension:0}]},options:{animation:false,maintainAspectRatio:false,parsing:false,plugins:{legend:{display:false},tooltip:{callbacks:{title:items=>new Date(items[0].parsed.x).toLocaleString('zh-CN')}}},scales:{x:{type:'linear',grid:{display:false},ticks:{maxTicksLimit:7,callback:value=>new Date(value).toLocaleString('zh-CN',{month:'numeric',day:'numeric',hour:'2-digit',minute:'2-digit'})}},y:{beginAtZero:true,suggestedMax:5,ticks:{precision:0},grid:{color:'#e5eaed'}}}}});}
  else{chart.data.datasets[0].data=points;chart.update();}
}
async function refresh(){
  if(fetching)return;fetching=true;clearTimeout(timer);
  try{render(await api(`/api/overview?hours=${$('range').value}`));}
  catch(error){if(!$('dashboard').hidden){$('connection').textContent='连接中断';$('error').hidden=false;$('error').textContent=`${error.message}，当前显示上次收到的数据。`;}}
  finally{fetching=false;if(!$('dashboard').hidden)timer=setTimeout(refresh,10000);}
}
$('login-form').addEventListener('submit',async event=>{event.preventDefault();const button=event.submitter;button.disabled=true;$('login-error').textContent='';try{await api('/api/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({password:$('password').value})});$('password').value='';await refresh();}catch(error){$('login-error').textContent=error.message;}finally{button.disabled=false;}});
$('logout').addEventListener('click',async()=>{try{await api('/api/logout',{method:'POST'});loggedOut();}catch(error){$('error').hidden=false;$('error').textContent=error.message;}});
$('range').addEventListener('change',refresh);$('search').addEventListener('input',()=>overview&&renderPlayers());refresh();
