/* Direction gallery: token/component specimen + all screens of one direction. */
(function(){
'use strict';const U=window.UI,B=U.B,ico=U.ico;
function hex(c){c=c.trim();let m;if((m=c.match(/^#([0-9a-f]{3})$/i)))return m[1].split('').map(x=>parseInt(x+x,16));if((m=c.match(/^#([0-9a-f]{6})$/i)))return[0,2,4].map(i=>parseInt(m[1].substr(i,2),16));if((m=c.match(/rgba?\(([^)]+)\)/))){const p=m[1].split(',').map(Number);return[p[0],p[1],p[2],p[3]===undefined?1:p[3]];}return[0,0,0];}
const lum=c=>{const f=v=>{v/=255;return v<=.03928?v/12.92:Math.pow((v+.055)/1.055,2.4)};return .2126*f(c[0])+.7152*f(c[1])+.0722*f(c[2]);};
const mix=(a,b)=>{const al=a[3]===undefined?1:a[3];return[0,1,2].map(i=>a[i]*al+b[i]*(1-al));};
const cr=(a,b)=>{const x=lum(a),y=lum(b);return(Math.max(x,y)+.05)/(Math.min(x,y)+.05);};
U.contrastRows=function(st){const g=n=>hex(getComputedStyle(st).getPropertyValue(n));const bg=g('--bg'),sf=g('--sf'),pn=mix(g('--pn'),bg),tx=g('--tx'),t2=g('--t2'),ac=g('--ac'),act=g('--act'),wr=g('--wr'),ok=g('--ok'),fc=g('--fc'),ln=g('--ln');
 const lnm=mix(ln,bg);
 return[['본문 글자 / 패널','--tx on --pn',cr(tx,pn),4.5],['본문 글자 / 배경','--tx on --bg',cr(tx,bg),4.5],['보조 글자 / 패널','--t2 on --pn',cr(t2,pn),4.5],['보조 글자 / 표면','--t2 on --sf',cr(t2,sf),4.5],['강조색 글자 / 표면','--ac on --sf',cr(ac,sf),4.5],['주요 버튼 글자','--tx on --acs2',cr(tx,mix(g('--acs2'),sf)),4.5],['경고색 글자 / 표면','--wr on --sf',cr(wr,sf),4.5],['정상색 글자 / 표면','--ok on --sf',cr(ok,sf),4.5],['포커스 링 / 표면','--fc vs --sf (비텍스트 3:1)',cr(fc,sf),3],['테두리 / 배경 (비텍스트)','--ln vs --bg',cr(lnm,bg),1.5]];};
U.specHTML=function(d,t){
 const sw=[['--bg','배경'],['--sf','표면'],['--pn','패널(반투명)'],['--ln','선'],['--tx','글자'],['--t2','보조 글자'],['--ac','강조'],['--act','강조 위 글자'],['--wr','경고'],['--ok','정상'],['--fc','포커스']];
 const names={a:['시설 관제실','어두운 산업 터미널. 방사능 경고 앰버, 격자·모노스페이스 포인트, 모서리 괄호, 스캔라인.','2px · 모노(숫자·레이블)'],b:['클린 모던','대형 배경 위 반투명 블러 패널, 알약형 컨트롤, 타이포 중심.','12px · 산세리프'],c:['80년대 레트로','인쇄물·공공시설 표지 느낌. 명조 제목, 이중선 테두리, 그림자 판, 약한 CRT 비네트.','3px · 명조(제목)']}[d];
 return `<div class="stage" data-d="${d}" data-t="${t}" id="spec_${d}${t}" style="height:1320px"><div class="art${d==='a'?' scan':''}${d==='c'?' crt':''}">${U.art(d,t,'island','full')}</div>
 <div class="scr" style="padding:40px 56px">
 <div class="rw" style="justify-content:space-between"><div><div class="lbl">디자인 토큰 · 방향 ${d.toUpperCase()} · ${t==='dark'?'다크':'라이트'}</div><h1 style="margin-top:6px">${names[0]}</h1><div class="mut" style="margin-top:4px">${names[1]} 모서리 ${names[2]}</div></div><div class="logo"><svg viewBox="0 0 52 52" style="width:52px;height:52px"><rect x="2" y="2" width="48" height="48" fill="none" stroke="currentColor" stroke-width="2" stroke-dasharray="5 4"/><circle cx="26" cy="26" r="14" fill="var(--ac)"/><rect x="24" y="6" width="4" height="40" fill="var(--bg)"/></svg><div><div class="nm">[게임 이름]</div><div class="sub">로고 자리표시</div></div></div></div>
 <div class="rw g24" style="align-items:flex-start;margin-top:24px">
  <div class="col g16" style="width:560px">
   <div class="pn pad"><div class="lbl" style="margin-bottom:10px">색</div><div style="display:grid;grid-template-columns:repeat(4,1fr);gap:10px">${sw.map(s=>`<div><div style="height:44px;background:var(${s[0]});border:1px solid var(--ln);border-radius:var(--r)"></div><div class="mono" style="font-size:14px;margin-top:4px">${s[0]}</div><div class="mut" style="font-size:14px">${s[1]}</div></div>`).join('')}</div></div>
   <div class="pn pad"><div class="lbl" style="margin-bottom:10px">글자 크기 · 간격</div><div class="col g8"><div style="font:700 52px/1.1 var(--fh)">제목 52</div><div style="font:700 30px/1.2 var(--fh)">소제목 30</div><div style="font-size:22px">본문 22 — 기본 글자 크기입니다.</div><div style="font-size:18px" class="mut">보조 18 — 1080p 최소 본문 크기</div><div class="mono lbl">레이블 18 모노 · 8 / 16 / 24 / 32 간격</div></div></div>
   <div class="pn pad" id="cr_${d}${t}"><div class="lbl" style="margin-bottom:8px">명암비 (자동 계산)</div><div data-cr></div></div>
  </div>
  <div class="col g16" style="width:620px">
   <div class="pn pad col g16"><div class="lbl">버튼 · 탭 · 선택</div>
    <div class="rw g16" style="flex-wrap:wrap">${B('기본',{id:'x1'})}${B('주요 동작',{pri:1,id:'x2'})}${B('위험',{dng:1,id:'x3'})}${B('비활성',{dis:1,id:'x4'})}<button class="btn fx" data-fid="x5">포커스 링</button></div>
    <div class="tabs" role="tablist"><button class="tab" role="tab" aria-selected="true">그래픽</button><button class="tab" role="tab" aria-selected="false">오디오</button><button class="tab" role="tab" aria-selected="false">조작</button></div>
    <div class="rw g16"><div class="seg"><button aria-pressed="true">솔로</button><button aria-pressed="false">스쿼드 (4인)</button></div><button class="tg" role="switch" aria-checked="true" aria-label="켬"><span>켬</span></button><button class="tg" role="switch" aria-checked="false" aria-label="끔"><span>끔</span></button><select class="dd" style="min-width:200px"><option>테두리 없는 창</option></select></div>
    <div class="rw g16"><input type="range" min="0" max="100" value="64" style="--p:64%" aria-label="예시 슬라이더"><span class="val">64%</span></div>
    <div class="rw g16"><span class="mut sm">키 칩</span><kbd>W</kbd><kbd>Shift</kbd><kbd>Space</kbd><kbd class="listen">키를 누르세요</kbd></div></div>
   <div class="pn col"><div class="row"><span class="nm" style="width:220px">전체 음량</span><div class="ct"><input type="range" min="0" max="100" value="80" style="--p:80%" aria-label="x"><span class="val">80%</span></div></div><div class="row" style="background:var(--acs)"><span class="nm" style="width:220px">선택(호버) 행</span><div class="ct mut sm">행 전체가 강조됩니다</div></div><div class="row"><span class="nm" style="width:220px">일반 행</span><div class="ct mut sm">구분선만 있습니다</div></div></div>
   <div class="col g8"><div class="toast">${ico('check')}<span>초대 코드를 복사했습니다.</span></div><div class="toast err">${ico('warn')}<span>매칭 서버 연결이 불안정합니다.</span></div></div>
  </div>
  <div class="col g16" style="width:560px">
   <div class="card col g8"><div class="lbl">카드</div><h3>북령 산악 · 승강장 07</h3><p class="mut sm">카드는 정보 묶음 하나를 담습니다.</p><div class="rw g8"><span class="badge ac">방장</span><span class="badge">나</span><span class="badge bot">봇</span><span class="badge wr">경고</span></div></div>
   <div class="slot me"><div class="av">${U.person('#6f8f7a',48)}</div><div class="grow"><div class="b">해무 <span class="badge">나</span> <span class="badge ac">방장</span></div><div class="sm mut">정비공</div></div><span class="st ready">준비 완료</span></div>
   <div class="slot"><div class="av">${U.person('#5f7686',48)}</div><div class="grow"><div class="b">서아</div><div class="sm mut">통신 기사</div></div><span class="st wait">준비 중</span></div>
   <div class="slot empty"><div class="av" style="border-style:dashed">${ico('plus')}</div><div class="grow"><div class="b">빈자리</div></div>${B('친구 초대',{sm:1,id:'x6'})}</div>
   <div class="pn pad" style="padding:20px"><h3>모달 (미니)</h3><p class="mut sm" style="margin:6px 0 12px">게임을 종료하시겠습니까?</p><div class="rw g16" style="justify-content:flex-end">${B('취소',{sm:1,id:'x7'})}${B('종료',{pri:1,sm:1,id:'x8'})}</div></div>
  </div></div></div></div>`;};
U.initSpec=function(host,d,t){host.innerHTML=U.specHTML(d,t);host.setAttribute('inert','');const st=host.firstElementChild;U.fit(host);
 const rows=U.contrastRows(st);host.querySelector('[data-cr]').innerHTML=rows.map(r=>`<div class="rw sm" style="justify-content:space-between;padding:2px 0"><span>${r[0]}</span><span class="mono ${r[2]>=r[3]?'okc':'wr'}">${r[2].toFixed(1)}:1 ${r[2]>=r[3]?'통과':'미달'} (≥${r[3]})</span></div>`).join('');return rows;};
function ready(){
 const body=document.body,d=body.dataset.d;let t='dark';
 const qs=new URLSearchParams(location.search);if(qs.get('t'))t=qs.get('t');
 body.dataset.t=t;
 const list=document.getElementById('list'),spec=document.getElementById('spec');
 function build(){document.getElementById('tb').textContent=t==='dark'?'라이트로 보기':'다크로 보기';body.dataset.t=t;
  const sf=document.createElement('div');sf.className='fit';spec.innerHTML='';spec.appendChild(sf);U.initSpec(sf,d,t);
  list.innerHTML='';U.ENTRIES.forEach(e=>{const f=document.createElement('figure');f.id=e.id;f.innerHTML=`<figcaption>${e.l}</figcaption><div class="fit"></div>`;list.appendChild(f);U.mountEntry(f.querySelector('.fit'),e,d,t,true);});}
 function refit(){document.querySelectorAll('.fit').forEach(f=>U.fit(f));}
 document.getElementById('tb').onclick=()=>{t=t==='dark'?'light':'dark';build();};
 addEventListener('resize',refit);build();setTimeout(refit,50);
}
if(document.body&&document.body.dataset.d)document.addEventListener('DOMContentLoaded',ready);
})();
