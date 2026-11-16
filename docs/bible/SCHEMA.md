# 정의서(Bible) 데이터 스키마

**단일 출처 = `docs/bible/data/<id>.json`**. `python3 tools/bible_build.py`가 `docs/bible/bundle.js`(웹용)와 `docs/bible/md/<id>.md`(문서용)를 만든다. 웹: `docs/bible/index.html`을 브라우저로 연다(서버 불필요).

## 파일 하나 = 섹션 하나
```json
{
  "id": "weapons",              // 파일명과 같게(소문자·숫자·_)
  "title": "총기",
  "order": 20,                  // 사이드바 정렬
  "group": "콘텐츠",            // 사이드바 묶음: 개요 | 콘텐츠 | 월드 | 시스템 | 기술 | 제작
  "summary": "한두 문장 요약",
  "status": "proposed",         // confirmed | proposed | open (섹션 전체 상태)
  "parts": [ ... ]
}
```

## part 종류
- **prose**: `{"type":"prose","title":"정의","paragraphs":["..."],"bullets":["..."]}`
- **table**: `{"type":"table","title":"총기 목록","columns":[{"key":"name","label":"이름","kind":"text"}, ...],"rows":[{"id":"ak","status":"proposed","name":"..."}]}`
  - `kind`: `text`(기본) | `num` | `tag`(짧은 라벨) | `ref`(다른 행의 id를 문자열로, 예 `ammo:5.56`) | `image`(저장소 상대경로, `docs/bible/` 기준 또는 `../../Assets/...`) | `audio`(상대경로) | `color`(`#rrggbb`) | `link`(URL)
  - 행의 `status`(선택), `source`(근거: `파일:줄` 또는 문서), `notes`(메모)는 모든 행에서 쓸 수 있다.
- **decisions**: `{"type":"decisions","title":"정해야 할 것","items":[{"id":"d1","q":"질문","options":["A","B"],"recommended":"A","status":"open","answer":"","why":"근거"}]}`
  - `status`: `open`(미정) | `proposed`(제안만) | `confirmed`(준환이 정함). 준환이 정하면 `answer`를 채우고 `confirmed`로.

## 상태 규칙
- `confirmed` = 준환이 확정했거나 **현재 코드/SPEC에 구현돼 있는 사실**(근거 `source` 필수).
- `proposed` = 제안/초안. `open` = 정해지지 않음.
- **추정과 확인을 구분**: 코드에서 읽은 값만 `confirmed`+`source`. 상상한 값은 `proposed`.
- 실존 상표·총기명·차종명 금지(CLAUDE.md 규칙 1). 현재 게임 안 이름(가상)을 쓴다.

## 이미지·소리
- 이미지는 `docs/bible/img/<섹션>/` 아래에 두거나 저장소 안 기존 캡처 경로를 상대경로로 적는다. 없으면 빈 문자열(웹에서 "이미지 없음" 표시).
- 소리 파일이 없는 항목은 `audio`를 비우고 `notes`에 "후보 조사 필요"를 적는다.
