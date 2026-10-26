# AeroShift

> **Unity 6 · C# · PC · 3D 비행 / 2D 탑뷰 미사일 회피 · 게임 프로그래밍 포트폴리오**
>
> **문서 버전:** 2026-10-26 / **개발 상태:** 설계 및 개발 진행 중. 문서의 계획을 구현 완료로 해석하지 않는다.

## 게임 소개

**AeroShift**는 플레이어가 3D 비행 구간과 2D 탑뷰 미사일 회피 구간을 `Shift`로 전환하면서 최대한 오래 생존하고 점수를 높이는 아케이드 비행 게임이다. 3D에서는 마우스로 기체를 조종해 바위·벽·좁은 통로를 통과한다. 2D에서는 WASD로 움직이며 **상하좌우 네 방향에서 경고 후 발사되는 미사일**을 피한다. 체력 대신 충돌 시 즉시 종료하는 규칙을 기본으로 한다.

**핵심 경험**
1. 고속으로 움직이는 비행기를 정확히 조작하며 장애물을 회피한다.
2. 3D ↔ 2D 차원 전환 타이밍을 판단하고 서로 다른 회피 방식에 적응한다.
3. Near Miss와 콤보로 위험을 감수해 점수를 높인다.
4. 싱글에서는 개인 최고 기록에 도전하고, 멀티에서는 동일 시드의 맵에서 상대 고스트와 경쟁한다.

## 플레이 흐름

```text
Login → Main Menu ── 싱글 ─→ Ready ─→ Playing (3D ↔ 2D) ─→ GameOver ─→ Result
                └─ 멀티 ─→ Matchmaking → SkillSelect → Ready → Playing → Result
```

- **3D→2D:** Shift → 안전 맵 준비 → 상승 연출 → 2D 탑뷰 / 미사일 회피.
- **2D→3D:** Shift → 일반 미사일 생성 중지 → 네 방향에서 5발씩 총 20발의 전환 패턴 → 하강 연출 → 3D 복귀.
- 전환 도중에는 중복 전환과 액티브 스킬을 차단하고 점수 계산을 일시 정지한다.
- 전환 대기 시간, 연출 길이, 재전환 쿨다운은 플레이 테스트 후 결정한다.

## 기본 조작

| 구간 | 입력 | 역할 |
|---|---|---|
| 3D | 마우스 이동 | 좌우·상하 조향, 기체 기울기 |
| 2D | WASD | 2D 평면 이동 |
| 공통 | Shift | 차원 전환 요청 |
| 공통 | 지정 스킬 키 | 활성 스킬 사용(조건 충족 시) |

## 주요 게임 시스템

| 영역 | 내용 | 문서 |
|---|---|---|
| 게임 규칙 | 차원, 싱글/멀티, 장애물, 점수, 스킨 | [게임 기획](Docs/13_Game_Design.md) |
| 스킬 | 시간 역행·감속, 미사일 억제·감속, 고스트 대전 스킬 | [스킬 명세](Docs/14_Skill_Specification.md) |
| 비행 | 마우스 조향, 보간, 카메라 지연, 프로토타입 검증 | [3D 프로토타입](Docs/15_3D_Prototype.md) |
| 차원 | 3D/2D 입력·물리·표현을 분리해 전환 | [차원 전환](Docs/02_Dimension_Transition.md) |
| 월드 | 안전 구간 / 청크 / 결정적 맵 생성 / 반환 | [맵 스트리밍](Docs/03_Map_Streaming.md) |
| 기록 | 생존 점수, 근접 회피, 콤보, 최고 점수 | [점수와 진행](Docs/17_Score_And_Progression.md) |

## 기술적 중점: 측정 가능한 최적화

AeroShift의 포트폴리오 목표는 기술 이름을 나열하는 것이 아니라 **같은 조건에서 성능 병목을 확인하고 개선 효과 및 부작용을 검증하는 것**이다.

| 기술 | 적용 위치 | 평가할 지표 | 문서 |
|---|---|---|---|
| Object Pooling | 맵·미사일·효과 | GC Alloc, CPU, 메모리 | [04](Docs/04_Object_Pooling.md) |
| Chunk Streaming | 무한 3D 맵 | 프레임 스파이크, 활성 오브젝트 수 | [03](Docs/03_Map_Streaming.md) |
| Job System + Burst | 장애물 배치/거리 계산 | CPU 시간, Job 오버헤드 | [05](Docs/05_Job_System_Burst.md) |
| **Visibility Culling** | 화면 밖 맵·오브젝트 표시 | GPU/렌더링 CPU, Draw Calls | [06](Docs/06_Visibility_Culling.md) |
| Spatial Partitioning | 충돌 후보 및 Near Miss | 검사 횟수, CPU | [07](Docs/07_Collision_Optimization.md) |
| Circular Buffer | 시간 역행 스냅샷 | 할당량, 복원 정확성 | [08](Docs/08_Circular_Buffer.md) |
| ProfilerRecorder + CSV | 성능 측정 | 평균/P95/P99 및 회차별 비교 | [10](Docs/10_Performance_Benchmark.md) |

> **LOD는 사용하지 않는다.** 오브젝트가 작고 모델이 단순한 현재 기획에서는 LOD 메시를 추가로 만드는 비용보다, 카메라 밖 표시를 제한하는 방식의 실효성을 먼저 검증한다. Unity의 기본 Frustum Culling과 자체 활성화/거리 정책은 서로 다른 범위의 처리이므로 혼동하지 않는다.

### 첫 성능 실험

**Map Streaming / Object Pooling ON / Burst ON / Visibility Culling ON / 고정 Seed / 동일한 주행 경로**를 먼저 검증한다. 검증 후 각 항목을 OFF로 바꾼 비교 실험을 수행한다. Burst ON은 계산 Job의 Burst 실행 경로를 사용하는 의미이며, GameObject 생성 자체를 Burst로 처리하는 것은 아니다.

결과는 `Application.persistentDataPath`의 CSV로 저장하도록 설계한다. **지금 수치는 목표 또는 과거 참고값일 뿐, AeroShift의 측정 완료 결과는 아직 없다.**

## 설계 원칙

- 클래스는 하나의 주된 책임만 갖는다. 전역 Singleton은 필요한 경우로 제한하고 현재 기준으로 GameManager만 후보이다.
- `MapManager`는 생성 위치와 시점, `MapPool`은 획득·반환·보관, `DimensionTransition`은 전환 순서를 관리한다.
- InputManager가 공통 입력을 받고 3D/2D 전용 입력과 이동 처리를 분리한다.
- 스폰 결과는 **Seed + Chunk Index + 생성 규칙**으로 재현 가능하게 설계한다.
- 최적화는 Editor 수치만으로 결론 내지 않고 대상 PC 빌드에서 동일 조건 반복 측정한다.
- 아직 구현하지 않은 시스템은 문서에서 **[계획]**, 검증되지 않은 항목은 **[미검증]**으로 표기한다.

## 문서 탐색

### 게임 기획
- [13 · 전체 게임 기획](Docs/13_Game_Design.md)
- [14 · 스킬 시스템 명세](Docs/14_Skill_Specification.md)
- [15 · 3D 프로토타입 기획](Docs/15_3D_Prototype.md)
- [16 · 입력 및 조작 시스템](Docs/16_Input_And_Controls.md)
- [17 · 점수와 난이도](Docs/17_Score_And_Progression.md)

### 기술 설계
- [01 · 아키텍처](Docs/01_Architecture.md)
- [02 · 2D/3D 차원 전환](Docs/02_Dimension_Transition.md)
- [03 · Chunk Map Streaming](Docs/03_Map_Streaming.md)
- [04 · Object Pooling](Docs/04_Object_Pooling.md)
- [05 · Job System / Burst](Docs/05_Job_System_Burst.md)
- [06 · Visibility Culling (LOD 제외)](Docs/06_Visibility_Culling.md)
- [07 · 충돌 / 공간 분할](Docs/07_Collision_Optimization.md)
- [08 · Circular Buffer / 시간 역행](Docs/08_Circular_Buffer.md)
- [09 · 리소스 수명 관리](Docs/09_Resource_Management.md)
- [10 · Performance Benchmark](Docs/10_Performance_Benchmark.md)
- [11 · 멀티플레이와 고스트](Docs/11_Multiplayer_Optimization.md)
- [12 · 성능 결과 기록 양식](Docs/12_Performance_Results.md)

## 진행 상태와 출처

이 자료는 첨부된 `aeroflip_game_design_v2.md`, `aeroflip_skill_specification.md`, `aeroflip_3d_prototype_plan.md`를 **기획 기준 자료**로 사용하고 새 AeroShift 결정 사항을 반영한 개정안이다. 원문은 [References/](References/)에 보존했다.

### 원본과 달라진 점
1. 명칭을 **AeroFlip → AeroShift**로 변경했다.
2. 카메라 시점 변경만 하던 차원 전환에서 2D/3D 물리·입력·표현 분리 설계로 확장했다. **게임 규칙(2D 탑뷰 / 4방향 미사일 / 복귀 20발)은 유지**했다.
3. **LOD는 제거**하고 화면 밖 렌더링 제한을 우선 검증한다.
4. 새 기술 후보(Job System, Burst, Benchmark, Addressables)는 **계획**으로만 기록한다.
5. 기존 문서는 '월드가 뒤로 흐름'을 기본으로 한다. 이후 논의된 '기체 전방 이동'으로의 변경 여부는 맵/물리/스코어에 영향을 미치므로 [미정 및 변경관리](Docs/13_Game_Design.md#변경-관리와-미정-항목)에서 명시해 둔다.

**제작 목적:** 청강문화산업대학교 게임 프로그래밍 포트폴리오를 위한 설계·구현·측정 기록.
