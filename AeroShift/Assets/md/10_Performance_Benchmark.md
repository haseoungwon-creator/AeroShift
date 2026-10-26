# 10. Performance Benchmark System

**상태:** [설계안 / 구현 전] · **첫 시나리오:** Map Streaming, Pool ON, Burst ON, Visibility Culling ON

## 목표

같은 맵과 주행 경로에서 옵션별 CPU, GPU, GC, 메모리, 프레임 지연을 측정한다. **기록되지 않은 개선 수치를 예측값처럼 결과에 쓰지 않는다.**

## 실험 구성

| 항목 | 최초 실험 설정 |
|---|---|
| 시나리오 | Map Streaming |
| Seed | 예: `20261026` (고정 테스트 값, 변경 가능) |
| 카메라·플레이어 | 재현 가능한 자동 입력/일정한 이동 경로 |
| Object Pooling | **ON** |
| Job / Burst | **ON** (장애물 배치 계산 경로) |
| Visibility Culling | **ON** (Unity 기본 컬링 + 추가 정책 명시) |
| LOD | **미사용** |
| 반복 횟수 | 5회 이상(제안) |
| 워밍업 | Pool Prewarm 및 렌더러 초기 상태 안정화 후 측정 |
| 기록 위치 | `Application.persistentDataPath/BenchmarkResults` |

**실험 매트릭스**

| 실험 | Pool | Burst | 추가 Visibility | 목적 |
|---|---|---|---|---|
| A | ON | ON | ON | 최초 구성 확인 |
| B | OFF | ON | ON | Pooling 영향 |
| C | ON | OFF | ON | Burst 경로 영향 |
| D | ON | ON | OFF | 추가 가시성 제어 영향 |

`Burst OFF`는 같은 계산 알고리즘을 일반 C# 또는 Burst 비활성 Job으로 구현해 비교한다. 옵션만 끄고 다른 알고리즘/오브젝트 수까지 바꾸지 않는다. Unity의 **기본 Frustum Culling**은 D에서도 계속 적용될 수 있으며, D의 OFF는 *추가적인 자체 가시성 관리*만 뜻한다.

## 아키텍처

```text
BenchmarkConfig (ScriptableObject)
      ↓
BenchmarkRunner ─────────────→ DeterministicTestDriver
      │                                   │
      │                            WorldProgress / MapManager
      │                                   │
      └─→ BenchmarkRecorder ←─────── 시나리오 실행
                ↓
        BenchmarkAnalyzer
                ↓
        BenchmarkCsvExporter
```

- `BenchmarkConfig`: Seed, 반복 수, 워밍업 시간, 측정 시간, 조건 저장.
- `DeterministicTestDriver`: 매회 같은 입력/경로/패턴 재현.
- `BenchmarkRecorder`: `Unity.Profiling.ProfilerRecorder`로 지원되는 카운터를 수집.
- `BenchmarkAnalyzer`: 회차별 평균, P95/P99, 최소·최대, 카운터 미지원 여부 계산.
- `BenchmarkCsvExporter`: UTF-8 CSV 저장. 소수점은 InvariantCulture로 직렬화.

## 실제 ProfilerRecorder 수집 주의 사항

- 카운터 명칭/지원 여부는 Unity 6 버전, 플랫폼, 빌드 옵션에 따라 다를 수 있다.
- 시작 전에 원하는 카운터의 사용 가능 여부를 검사한다. **지원되지 않으면 0이 아니라 빈 칸/`N/A`로 기록**한다.
- `GC Alloc`의 의미(프레임/스레드별 제공 범위)는 카운터별로 확인한다.
- GPU 시간은 장치/플랫폼에서 이용 가능하지 않을 수 있다.
- `ProfilerRecorder` 자체의 오버헤드도 고려한다. 가능하면 Development Build에서 측정하고 동일 설정을 유지한다.
- 실험 데이터는 Editor가 아닌 PC Player Build에서 우선 확보한다.

## 권장 결과 구조

```text
BenchmarkResults/
  2026-10-26_Seed20261026_MapStreaming_PoolOn_BurstOn_VisibilityOn/
     run_01.csv
     run_02.csv
     run_03.csv
     run_04.csv
     run_05.csv
     summary.csv
     config.json
```

**프레임 CSV 예시 헤더**

```csv
run_id,frame_index,elapsed_s,main_thread_ms,render_thread_ms,gpu_ms,gc_alloc_bytes,total_memory_bytes,draw_calls,active_chunks,pool_active,pool_inactive
```

**Summary CSV 예시 헤더**

```csv
config_id,seed,run_count,frame_avg_ms,frame_p95_ms,frame_p99_ms,main_thread_avg_ms,main_thread_p99_ms,gc_alloc_total_bytes,peak_memory_bytes,notes
```

CSV `NaN`, 비지원 지표, 워밍업 포함 여부, 샘플 수를 명확히 구분한다. 프레임 시간은 실제 표본 정의(델타 시간/Profiler 타임)를 문서화한다. 평균 FPS만으로 끊김을 판단하지 않는다.

## 동일 Seed만으로 부족한 이유

같은 맵 Seed여도 수동 조작, 시작 시각, 프레임 타이밍, 초기 풀 크기, 물리 상태가 바뀌면 실행 부하는 달라진다. **자동 조작 경로, 동일 청크 순서·속도, 동일 워밍업, 동일 실행 빌드·그래픽 설정**도 통제해야 한다.

## 벤치마크 완료 기준

- A/B/C/D의 모든 반복 실행에서 동일 청크 배치가 생성됨.
- 각 Run CSV와 전체 Summary가 저장됨.
- 지원되지 않는 카운터를 `N/A`로 식별함.
- GC 감소 vs CPU 증가처럼 불리한 결과도 기록함.
- 평균뿐 아니라 P99와 최대 프레임 지연을 비교함.

## 면접 질문

- 왜 Seed만 같다고 완전히 같은 성능 조건이 되지는 않나요?
- 평균 프레임과 P99 프레임 시간을 함께 측정하는 이유는 무엇인가요?
- Pool ON에서 CPU가 증가한 경우 어떤 추가 계측이 필요할까요?
