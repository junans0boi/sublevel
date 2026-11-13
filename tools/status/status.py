#!/usr/bin/env python3
"""읽기 전용 상황판. 에이전트의 말이 아니라 디스크·프로세스에서 직접 읽는다.

실행: python3 tools/status/status.py [--port 8765] [--lan]
기본은 127.0.0.1 에만 열린다. --lan 이면 0.0.0.0 (같은 Wi-Fi 의 폰에서 접속).
표준 라이브러리만 사용. 프로젝트 파일 수정/에이전트 제어 기능은 없다.
"""
import argparse
import glob
import json
import os
import re
import socket
import subprocess
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TASK_GLOB = "/private/tmp/claude-*/**/tasks/*.output"
TASK_WINDOW = 6 * 3600
GH_TTL = 90
SNAP_TTL = 2
_lock = threading.Lock()
_gh = {"t": 0, "data": None}
_snap = {"t": 0, "data": None}
_hist = {}  # 출력 파일 경로 -> [(시각, 크기)] 증가량 계산용


def run(cmd, cwd=ROOT, timeout=15):
    try:
        r = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, timeout=timeout)
        return r.stdout if r.returncode == 0 else ""
    except Exception:
        return ""


def git(*a):
    return run(["git", "-c", "core.quotepath=false"] + list(a))


def tail_line(path):
    try:
        with open(path, "rb") as f:
            f.seek(0, 2)
            f.seek(max(0, f.tell() - 4096))
            lines = f.read().decode("utf-8", "replace").strip().splitlines()
        return lines[-1][:300] if lines else ""
    except Exception:
        return ""


def agents(now):
    out = []
    for p in glob.glob(TASK_GLOB, recursive=True):
        try:
            st = os.stat(p)
        except OSError:
            continue
        if now - st.st_mtime > TASK_WINDOW:
            continue
        h = _hist.setdefault(p, [])
        h.append((now, st.st_size))
        while len(h) > 1 and now - h[1][0] > 60:  # 약 60초 창
            h.pop(0)
        grew = st.st_size - h[0][1] if now - h[0][0] >= 3 else None
        out.append({"path": p.split("/private/tmp/")[-1], "age": now - st.st_mtime,
                    "size": st.st_size, "grew": grew, "last": tail_line(p)})
    return sorted(out, key=lambda x: x["age"])


UNITY_RE = re.compile(r"/Unity\.app/Contents/MacOS/Unity(\s|$)")


def unity(now):
    procs, projects = [], [ROOT]
    for line in run(["ps", "-axo", "pid=,etime=,command="]).splitlines():
        parts = line.split(None, 2)
        if len(parts) < 3 or not UNITY_RE.search(parts[2]) or "Unity Hub.app" in parts[2]:
            continue
        cmd = parts[2]
        m = re.search(r"-projectPath\s+(.+?)(?=\s-\w|$)", cmd)
        proj = m.group(1) if m else "(알 수 없음)"
        if "-runTests" in cmd:
            kind = "테스트"
        elif "-executeMethod" in cmd:
            kind = "메서드 실행"
        elif "-build" in cmd:
            kind = "빌드"
        else:
            kind = "에디터"
        procs.append({"pid": parts[0], "etime": parts[1], "kind": kind, "project": proj})
        if proj not in projects and os.path.isdir(proj):
            projects.append(proj)
    logs = []
    for proj in projects:
        files = [f for f in glob.glob(os.path.join(proj, "Logs", "*.log")) if "shadercompiler" not in f]
        for f in sorted(files, key=os.path.getmtime, reverse=True)[:4]:
            logs.append({"project": proj, "name": os.path.basename(f),
                         "age": now - os.path.getmtime(f), "last": tail_line(f)})
    return {"procs": procs, "logs": logs}


def gitinfo(now):
    commits = []
    for l in git("log", "-12", "--format=%h%x09%ct%x09%s").splitlines():
        h, ct, s = l.split("\t", 2)
        commits.append({"hash": h, "age": now - int(ct), "subject": s})
    up = git("rev-list", "--count", "@{u}..HEAD").strip()
    files = []
    for l in git("status", "--porcelain").splitlines():
        path = l[3:].split(" -> ")[-1].strip('"')
        try:
            files.append({"st": l[:2].strip() or "?", "path": path,
                          "age": now - os.path.getmtime(os.path.join(ROOT, path))})
        except OSError:
            files.append({"st": l[:2].strip() or "?", "path": path, "age": None})  # 삭제된 파일
    files.sort(key=lambda f: f["age"] if f["age"] is not None else 1e12)
    return {"commits": commits, "unpushed": int(up) if up.isdigit() else None,
            "files": files[:15], "dirty": len(files)}


def issues(now):
    if now - _gh["t"] > GH_TTL:
        out = run(["gh", "issue", "list", "--limit", "30", "--json", "number,title,labels"], timeout=20)
        try:
            _gh["data"] = [{"n": i["number"], "title": i["title"], "labels": [x["name"] for x in i["labels"]]}
                           for i in json.loads(out)]
        except Exception:
            _gh["data"] = _gh["data"] or []
        _gh["t"] = now
    return {"items": _gh["data"], "cached_age": now - _gh["t"]}


def memory():
    for l in run(["memory_pressure", "-Q"]).splitlines():
        if "free percentage" in l:
            return l.strip()
    return "(읽을 수 없음)"


def notes():
    try:
        with open(os.path.join(ROOT, "docs", "STATUS.json"), encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return None


def build():
    now = time.time()
    a, g = agents(now), gitinfo(now)
    sig = []
    if g["files"] and g["files"][0]["age"] is not None:
        sig.append(("파일 변경", g["files"][0]["age"]))
    if g["commits"]:
        sig.append(("커밋", g["commits"][0]["age"]))
    if a:
        sig.append(("에이전트 출력", a[0]["age"]))
    alive = min(sig, key=lambda s: s[1]) if sig else None
    return {"now": now, "alive": {"source": alive[0], "age": alive[1]} if alive else None,
            "agents": a, "unity": unity(now), "git": g, "issues": issues(now),
            "memory": memory(), "notes": notes()}


def snapshot():
    with _lock:  # 여러 클라이언트가 붙어도 2초에 한 번만 계산
        if time.time() - _snap["t"] > SNAP_TTL:
            _snap["data"] = build()
            _snap["t"] = time.time()
        return _snap["data"]


PAGE = r"""<!doctype html><html lang="ko"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>상황판</title>
<style>
:root{--bg:#f6f7f9;--card:#fff;--fg:#1b1f24;--mut:#67707b;--bd:#dfe3e8;--g:#1a9a4a;--y:#c28a00;--r:#d23b3b}
@media(prefers-color-scheme:dark){:root{--bg:#0f1216;--card:#181c22;--fg:#e6e9ed;--mut:#8b95a1;--bd:#2a313a;--g:#3ccf72;--y:#e8b93a;--r:#f06464}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.45 -apple-system,"Apple SD Gothic Neo",sans-serif}
main{max-width:960px;margin:0 auto;padding:16px}h2{font-size:14px;margin:0 0 8px;color:var(--mut)}
.card{background:var(--card);border:1px solid var(--bd);border-radius:10px;padding:12px 14px;margin-bottom:12px}
.hero{font-size:20px;font-weight:700}.dot{display:inline-block;width:14px;height:14px;border-radius:50%;margin-right:8px;vertical-align:-1px}
.g{background:var(--g)}.y{background:var(--y)}.r{background:var(--r)}.mut{color:var(--mut);font-size:12px;font-weight:400}
.row{padding:6px 0;border-top:1px solid var(--bd);word-break:break-all}.row:first-of-type{border-top:0}
code{font:12px ui-monospace,Menlo,monospace}.tag{font-size:11px;border:1px solid var(--bd);border-radius:4px;padding:0 5px;margin-right:4px}
</style></head><body><main>
<div class="card"><div class="hero" id="hero">불러오는 중…</div>
<div class="mut">가장 최근 신호(파일 변경·커밋·에이전트 출력) 기준. 5분 이내 초록, 20분 이내 노랑, 그 이상 빨강. 20분 넘게 신호가 없으면 멈춘 것으로 봅니다.</div></div>
<div id="out"></div><div class="mut" id="foot"></div></main>
<script>
const E=s=>String(s).replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
const ago=s=>s==null?"-":s<60?Math.round(s)+"초 전":s<3600?Math.floor(s/60)+"분 전":Math.floor(s/3600)+"시간 "+Math.floor(s%3600/60)+"분 전";
const kb=n=>n<1024?n+" B":(n/1024).toFixed(1)+" KB";
const sec=(t,b)=>`<div class="card"><h2>${t}</h2>${b||'<div class="mut">없음</div>'}</div>`;
const row=h=>`<div class="row">${h}</div>`;
async function tick(){try{
const d=await (await fetch("/api",{cache:"no-store"})).json();
const a=d.alive,h=document.getElementById("hero");
if(a){const c=a.age<300?"g":a.age<1200?"y":"r";h.innerHTML=`<span class="dot ${c}"></span>${ago(a.age)} · ${E(a.source)}${c=="r"?' <span class="mut">(멈춘 것으로 의심)</span>':""}`}
else h.innerHTML='<span class="dot r"></span>신호 없음';
let o="";
if(d.notes)o+=sec("작업 메모",d.notes.map(n=>row(`<span class="tag">${E(n.state)}</span><b>${E(n.name)}</b> <span class="mut">${E(n.note||"")}</span>`)).join(""));
o+=sec("서브에이전트 (최근 6시간)",d.agents.map(x=>row(`<code>${E(x.path)}</code><br>${ago(x.age)} · ${kb(x.size)}${x.grew==null?"":x.grew>0?` · <b style="color:var(--g)">+${kb(x.grew)}/60초</b>`:" · 증가 없음"}<br><span class="mut">${E(x.last)}</span>`)).join(""));
o+=sec("Unity 프로세스",d.unity.procs.map(p=>row(`<span class="tag">${E(p.kind)}</span>pid ${E(p.pid)} · 실행 ${E(p.etime)}<br><code>${E(p.project)}</code>`)).join(""));
o+=sec("Unity 로그",d.unity.logs.map(l=>row(`<code>${E(l.project.split("/").slice(-1)[0])}/${E(l.name)}</code> · ${ago(l.age)}<br><span class="mut">${E(l.last)}</span>`)).join(""));
const g=d.git;
o+=sec(`최근 커밋 <span class="mut">푸시 안 된 커밋 ${g.unpushed==null?"?":g.unpushed}개</span>`,g.commits.map(c=>row(`<code>${E(c.hash)}</code> ${E(c.subject)} <span class="mut">${ago(c.age)}</span>`)).join(""));
o+=sec(`방금 바뀐 파일 <span class="mut">커밋 안 된 파일 ${g.dirty}개</span>`,g.files.map(f=>row(`<span class="tag">${E(f.st)}</span><code>${E(f.path)}</code> <span class="mut">${f.age==null?"삭제됨":ago(f.age)}</span>`)).join(""));
o+=sec(`열린 이슈 <span class="mut">(${ago(d.issues.cached_age)} 조회)</span>`,d.issues.items.map(i=>row(`#${i.n} ${E(i.title)} ${i.labels.map(l=>`<span class="tag">${E(l)}</span>`).join("")}`)).join(""));
o+=sec("메모리 압력",`<code>${E(d.memory)}</code>`);
document.getElementById("out").innerHTML=o;
document.getElementById("foot").textContent="갱신 "+new Date().toLocaleTimeString("ko-KR")+" · 5초마다 자동";
}catch(e){document.getElementById("foot").textContent="연결 실패: "+e}}
tick();setInterval(tick,5000);
</script></body></html>"""


class H(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path.split("?")[0] == "/api":
            body, ctype = json.dumps(snapshot(), ensure_ascii=False).encode(), "application/json"
        elif self.path.split("?")[0] == "/":
            body, ctype = PAGE.encode(), "text/html"
        else:
            self.send_error(404)
            return
        self.send_response(200)
        self.send_header("Content-Type", ctype + "; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *a):
        pass


def main():
    ap = argparse.ArgumentParser(description="읽기 전용 상황판")
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--lan", action="store_true", help="0.0.0.0 으로 열어 같은 Wi-Fi 에서 접속 허용")
    a = ap.parse_args()
    host = "0.0.0.0" if a.lan else "127.0.0.1"
    srv = ThreadingHTTPServer((host, a.port), H)
    print("http://127.0.0.1:%d" % a.port)
    if a.lan:
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            s.connect(("10.255.255.255", 1))
            print("LAN: http://%s:%d  (인증 없음, 믿는 Wi-Fi 에서만)" % (s.getsockname()[0], a.port))
        except Exception:
            pass
    srv.serve_forever()


if __name__ == "__main__":
    main()
