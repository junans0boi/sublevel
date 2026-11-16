(function(){
'use strict';const U=UI;const qs=new URLSearchParams(location.search);
const S={pref:0,d:qs.get('d')||'a',t:qs.get('t')||'dark',screen:qs.get('s')||'splash',hist:[],v:Object.assign({},U.DEFV),mode:'squad',role:'leader',rd:{hm:0,dy:1,sa:0},pv:'3인칭',iload:0,n:12,count:false,acd:30,match:'idle',elapsed:0,ready:{1:1,2:0},set:{},keys:{},listen:null,tab:'gfx',ch:5,col:1,tut:1,modal:qs.get('m')||null,toasts:[],load:0,cd:10,dirty:false};
for(const k of Object.keys(U.DEFV))if(qs.get(k))S.v[k]=qs.get(k);
const host=document.getElementById('host'),view=document.getElementById('view');
const SCREENS=[['splash','1 스플래시'],['loading','1 로딩'],['lobby','3 로비'],['custom','3 커스텀 모드'],['char','4 캐릭터'],['settings','5 설정'],['ingameload','6 인게임 로딩'],['assembly','6 대기 구역'],['deploy','6 엘리베이터 투입'],['ingame','(인게임 자리표시)'],['result','7 결과'],['credits','8 크레딧'],['tutorial','10 훈련장']];
const jump=document.getElementById('jump');SCREENS.forEach(s=>{const o=document.createElement('option');o.value=s[0];o.textContent=s[1];jump.appendChild(o);});
document.querySelectorAll('[data-vk]').forEach(sel=>{const k=sel.dataset.vk;Object.entries(U.VARIANTS[k]).forEach(([v,l])=>{const o=document.createElement('option');o.value=v;o.textContent=l;sel.appendChild(o);});sel.value=S.v[k];sel.onchange=()=>{S.v[k]=sel.value;render();};});
function fitHost(){const w=view.clientWidth,h=view.clientHeight;const sc=Math.min(w/1920,h/1080);host.style.width=Math.floor(1920*sc)+'px';host.style.height=Math.floor(1080*sc)+'px';const st=host.firstElementChild;if(st)st.style.transform='scale('+sc+')';}
function ctx(){return{v:S.v,mode:S.mode,match:S.match,elapsed:S.elapsed,ready:S.ready,role:S.role,rd:S.rd,pv:S.pv,pref:S.pref,iload:S.iload,n:S.n,count:S.count,acd:S.acd,set:S.set,keys:S.keys,listen:S.listen,tab:S.tab,ch:S.ch,col:S.col,tut:S.tut,cd:S.cd,load:S.load,dirty:S.dirty,proto:1};}
let lastScreen=null;
function render(){const a=document.activeElement;const fid=a&&a.dataset&&a.dataset.fid;
 const sc=[...host.querySelectorAll('[role=tabpanel],[style*="overflow-y:auto"]')].map(e=>e.scrollTop);
 S.toasts=S.toasts.filter(x=>x[2]>Date.now());
 host.innerHTML=U.stageHTML(S.d,S.t,S.screen,ctx(),S.modal,S.toasts.map(x=>[x[0],x[1]]));
 [...host.querySelectorAll('[role=tabpanel],[style*="overflow-y:auto"]')].forEach((e,i)=>{if(sc[i])e.scrollTop=sc[i];});
 fitHost();
 document.querySelectorAll('[data-d]').forEach(b=>{if(b.classList.contains('hb'))b.setAttribute('aria-pressed',b.dataset.d===S.d);});
 document.body.dataset.t=S.t;jump.value=S.screen;roleSel.value=S.mode==='solo'?'solo':S.role;document.querySelectorAll('[data-vk]').forEach(s=>s.value=S.v[s.dataset.vk]);
 let f=null;if(S.screen!==lastScreen||S.modal!==lastModal){f=S.modal?host.querySelector('.modal [data-af]'):host.querySelector('.scr [data-af]');}else if(fid){f=host.querySelector('[data-fid="'+CSS.escape(fid)+'"]');}
 if(f)f.focus({preventScroll:true});lastScreen=S.screen;lastModal=S.modal;}
let lastModal=null;
function toast(t,err){S.toasts.push([t,!!err,Date.now()+3200]);setTimeout(render,3300);}
function go(s,replace){if(s==='menu')s='lobby';const from=S.screen;if(!replace&&from!==s)S.hist.push(from);if(['lobby','char','tutorial','settings','credits','custom'].indexOf(s)>=0)S.hist=[];S.screen=s;S.modal=null;S.listen=null;
 if(s==='lobby'&&S.match!=='search'){S.match='idle';}
 if(s==='deploy'){S.cd=5;}
 if(s==='ingameload'){S.iload=0;startIload();}
 if(s==='assembly'){S.n=12;S.count=false;S.acd=30;}
 if(s==='loading'){S.load=0;}
 render();}
function back(){if(S.hist.length){S.screen=S.hist.pop();S.modal=null;S.listen=null;if(S.screen==='lobby')S.match='idle';render();}else if(S.screen==='lobby'){S.modal='quit';render();}else if(['char','tutorial','settings','credits','custom'].indexOf(S.screen)>=0){go('lobby',true);}}
function found(){S.match='idle';S.hist=[];go('ingameload',true);}
function act(a){const [k,p,q]=a.split(':');
 switch(k){
  case 'mode':if(S.role==='member')break;S.mode=p;render();break;
  case 'pref':S.pref=+p;render();break;
  case 'pv':if(S.role==='member')break;S.pv=p;render();break;
  case 'leaveparty':S.mode='solo';S.role='leader';S.match='idle';render();toast('파티에서 나갔습니다. 솔로 로비로 돌아왔습니다.');break;
  case 'fill':S.n=Math.max(S.n,30);S.count=true;S.acd=Math.min(S.acd,8);render();break;
  case 'rdy':S.rd[p]=S.rd[p]?0:1;render();if(S.role==='member'&&p==='hm'&&S.rd.hm){setTimeout(()=>{if(S.screen==='lobby'&&S.role==='member'&&S.rd.hm&&S.match!=='search'){S.match='search';S.elapsed=0;render();toast('파티장이 매칭을 시작했습니다.');}},2000);}break;
  case 'start':S.match='search';S.elapsed=0;render();break;
  case 'cancel':S.match='idle';render();toast('매칭을 취소했습니다.');break;
  case 'found':found();break;
  case 'again':S.match='search';S.elapsed=0;break;
  case 'tab':S.tab=p;render();break;
  case 'tg':if(p==='tutshow'){toast('설정을 바꿨습니다.');break;}S.set[p]=(S.set[p]===undefined?U.DEF[p]:S.set[p])?0:1;S.dirty=true;render();break;
  case 'seg':S.set[p]=a.split(':').slice(2).join(':');S.dirty=true;render();break;
  case 'key':S.listen=p;render();break;
  case 'keysreset':S.keys={};S.dirty=true;render();toast('키를 모두 기본값으로 되돌렸습니다.');break;
  case 'defaults':S.set={};S.keys={};S.dirty=false;render();toast('기본값으로 되돌렸습니다.');break;
  case 'apply':S.dirty=false;render();toast('설정을 저장했습니다.');break;
  case 'ch':S.ch=+p;render();break;
  case 'col':S.col=+p;render();break;
  case 'tut':S.tut=+p;render();break;
  case 'toast':toast(a.slice(6));break;
  case 'modal':S.modal=p||null;render();break;
  case 'quit':S.modal=null;S.hist=[];S.screen='splash';render();toast('실제 게임에서는 여기서 프로그램이 종료됩니다.');startSplash();break;
  case 'leavematch':S.hist=[];go('lobby',true);toast('매치를 나갔습니다.');break;
 }}
host.addEventListener('click',e=>{const b=e.target.closest('button');if(!b||b.disabled)return;
 if(b.dataset.act)act(b.dataset.act);
 if(b.dataset.back!==undefined){back();return;}
 if(b.dataset.go){const g=b.dataset.go;if(g==='!back')back();else if(g==='!quit'){S.modal='quit';render();}else go(g);}});
host.addEventListener('input',e=>{const r=e.target;if(r.type==='range'){const k=r.dataset.k;S.set[k]=+r.value;const p=(r.value-r.min)/(r.max-r.min)*100;r.style.setProperty('--p',p+'%');const v=host.querySelector('[data-v="'+k+'"]');const f=v.dataset.f;const n=+r.value;v.textContent=f==='%'?Math.round(n)+'%':f==='d'?n.toFixed(2):f==='x'?n.toFixed(2)+'x':Math.round(n);S.dirty=true;const dd=host.querySelector('[data-dirty]');if(dd)dd.textContent='● 변경한 내용이 있습니다. 적용을 눌러야 저장됩니다.';}});
host.addEventListener('change',e=>{const s=e.target;if(s.tagName==='SELECT'){S.set[s.dataset.k]=s.value;S.dirty=true;const dd=host.querySelector('[data-dirty]');if(dd)dd.textContent='● 변경한 내용이 있습니다. 적용을 눌러야 저장됩니다.';}});
function desc(e){const r=e.target.closest&&e.target.closest('.row[data-desc]');if(!r)return;const t=host.querySelector('#dtt'),d=host.querySelector('#ddd');if(t)t.textContent=r.dataset.dt;if(d)d.textContent=r.dataset.desc;}
host.addEventListener('focusin',desc);host.addEventListener('mouseover',desc);
const KN={' ':'Space','Control':'Ctrl','Shift':'Shift','Tab':'Tab','Alt':'Alt','Enter':'Enter','ArrowUp':'↑','ArrowDown':'↓','ArrowLeft':'←','ArrowRight':'→'};
document.addEventListener('keydown',e=>{
 if(S.screen==='splash'&&!S.modal){skipSplash();return;}
 if(S.listen){e.preventDefault();if(e.key==='Escape'){S.listen=null;render();toast('키 변경을 취소했습니다.');return;}const lab=KN[e.key]||(e.key.length===1?e.key.toUpperCase():e.key);S.keys[S.listen]=lab;S.listen=null;S.dirty=true;render();toast('키를 '+lab+'(으)로 바꿨습니다.');return;}
 if(S.modal){if(e.key==='Escape'&&(S.modal==='quit'||S.modal==='err'||S.modal==='mode')){S.modal=null;render();}
  if(e.key==='Tab'){const f=[...host.querySelectorAll('.modal button')];if(!f.length)return;const i=f.indexOf(document.activeElement);e.preventDefault();f[(i+(e.shiftKey?-1:1)+f.length)%f.length].focus();}return;}
 const a=document.activeElement;
 if(e.key==='Escape'){if(S.screen==='lobby'&&S.match==='search'){if(S.role==='member')toast('매칭 취소는 파티장만 할 수 있습니다.',1);else act('cancel');}else if(S.screen==='ingameload'){}else if(S.screen==='assembly'){toast('나가려면 나가기 버튼을 누르세요.',1);}else if(S.screen==='ingame'){go('result');}else if(S.screen==='result'){go('lobby');}else if(S.screen==='deploy'){toast('나가려면 왼쪽 아래의 나가기 버튼을 누르세요.',1);}else if(S.screen==='loading'){}else back();return;}
 if(a&&a.classList&&a.classList.contains('mi')){const all=[...host.querySelectorAll('.mi')];const i=all.indexOf(a);const hz=a.closest('.mh');const nx=(hz?'ArrowRight':'ArrowDown'),pv=(hz?'ArrowLeft':'ArrowUp');if(e.key===nx||e.key===pv){e.preventDefault();all[(i+(e.key===nx?1:all.length-1))%all.length].focus();}}
 if(a&&a.classList&&a.classList.contains('tab')&&(e.key==='ArrowRight'||e.key==='ArrowLeft'||e.key==='ArrowDown'||e.key==='ArrowUp')){const all=[...host.querySelectorAll('.tab')];const i=all.indexOf(a);const n=all[(i+((e.key==='ArrowRight'||e.key==='ArrowDown')?1:all.length-1))%all.length];e.preventDefault();n.click();}
 if(a&&a.parentElement&&a.parentElement.classList.contains('seg')&&(e.key==='ArrowRight'||e.key==='ArrowLeft')){const all=[...a.parentElement.children];const i=all.indexOf(a);const n=all[(i+(e.key==='ArrowRight'?1:all.length-1))%all.length];e.preventDefault();n.focus();}
});
/* top bar */
document.querySelectorAll('.hb[data-d]').forEach(b=>b.onclick=()=>{S.d=b.dataset.d;render();});
document.getElementById('tb').onclick=()=>{S.t=S.t==='dark'?'light':'dark';render();};
jump.onchange=()=>{S.hist=[];S.modal=null;S.match='idle';S.screen=jump.value;if(S.screen==='deploy')S.cd=5;if(S.screen==='ingameload'){S.iload=0;startIload();}if(S.screen==='assembly'){S.n=12;S.count=false;S.acd=30;}render();};
const roleSel=document.getElementById('role');roleSel.onchange=()=>{if(roleSel.value==='solo'){S.mode='solo';S.role='leader';}else{S.mode='squad';S.role=roleSel.value;}S.match='idle';S.rd={hm:0,dy:1,sa:0};go('lobby',true);};
document.querySelectorAll('[data-sim]').forEach(b=>b.onclick=()=>{if(b.dataset.sim==='leader'){if(S.mode==='solo'){toast('솔로에는 파티장 이탈이 없습니다.',1);return;}S.role='member';S.rd={hm:0,dy:1,sa:0};toast('파티장이 나갔습니다. 서아 님이 새 파티장이 되었고 매칭은 유지됩니다.');render();return;}S.modal=b.dataset.sim;if(S.screen==='splash'||S.screen==='loading'){S.screen='lobby';}render();});
/* timers */
let sp=null,ld=null;
function skipSplash(){if(sp)clearTimeout(sp);sp=null;S.load=0;go('loading',true);startLoading();}
function startSplash(){if(sp)clearTimeout(sp);sp=setTimeout(skipSplash,2400);}
function startLoading(){if(ld)clearInterval(ld);ld=setInterval(()=>{if(S.screen!=='loading'){clearInterval(ld);return;}S.load=Math.min(100,S.load+2);const l=host.querySelector('[data-load]'),b=host.querySelector('[data-loadbar]');if(l)l.textContent=S.load+'%';if(b)b.style.width=S.load+'%';if(S.load>=100){clearInterval(ld);S.hist=[];go('lobby',true);}},60);}
let il=null;function startIload(){if(il)clearInterval(il);il=setInterval(()=>{if(S.screen!=='ingameload'){clearInterval(il);return;}S.iload=Math.min(100,S.iload+2);const l=host.querySelector('[data-iload]'),b=host.querySelector('[data-iloadbar]');if(l)l.textContent=S.iload+'%';if(b)b.style.width=S.iload+'%';if(S.iload>=100){clearInterval(il);go('assembly',true);}},60);}
setInterval(()=>{if(S.modal&&S.modal!=='dc')return;
 if(S.screen==='assembly'){if(!S.count){S.n=Math.min(50,S.n+3);if(S.n>=30){S.count=true;S.acd=30;}}else{S.acd--;S.n=Math.min(50,S.n+1);if(S.acd<=0){S.hist=[];go('deploy',true);return;}}render();}
 if(S.screen==='lobby'&&S.match==='search'){S.elapsed++;if(S.elapsed>=9){found();}else render();}
 if(S.screen==='deploy'&&!S.modal){S.cd--;const c=host.querySelector('[data-cd]');if(c)c.textContent=String(Math.max(S.cd,0)).padStart(2,'0');if(S.cd<=0){S.hist=[];go('ingame',true);}}},1000);
render();
if(S.screen==='splash')startSplash();else if(S.screen==='loading')startLoading();else if(S.screen==='ingameload')startIload();
addEventListener('resize',fitHost);
})();
