# Aiming PoC — Sync Server

> **.NET 10 + MagicOnion 기반 실시간 게임 서버 포트폴리오.**
> Unity 없이 100% C# 로 서버 설계 · Zero-Allocation 최적화 · 분산 아키텍처 ·
> Stateful 생명주기 · 관측성의 다섯 역량을 한 레포에서 증명한다.

<p align="center">
  <img src="docs/images/toggle-demo.gif" alt="Zero-Allocation 토글 시연: 버튼 클릭 한 번에 Alloc Rate 와 P99 Latency 그래프가 꺾이는 장면" width="820"
       onerror="this.style.display='none';this.nextElementSibling.style.display='block'">
  <br>
  <sub>⬆ 대시보드 <b>Zero-Alloc OFF → ON</b> 클릭 한 번에 <b>Alloc Rate</b> 와 <b>P99 Latency</b> 가 평행선으로 꺾이는 순간.
  아직 GIF 가 없다면 <a href="#-시연-gif-캡처하기">§ 시연 GIF 캡처하기</a> 참고.</sub>
</p>

---

## 면접 30초 요약

> *"C# 만으로 MagicOnion 서버를 만들고, 헤드리스 봇 수천 개로 부하를 걸면서
> **Zero-Alloc 토글 하나로 GC 그래프를 평행선**으로 만드는 시연을 대시보드 한 화면에서 보여줍니다.
> 그 옆에는 **MagicOnion Redis Backplane** 으로 서버가 2대, 10대로 늘어나도 동일 룸 유저가
> 정상 동기화되고, **UUID v7 PK + Channel 기반 Write-Behind** 로 메인 스레드는 DB I/O 를
> 전혀 타지 않습니다. K8s 는 **Graceful Shutdown** 으로 유저가 튕기지 않게 드레인하고,
> Stateful Hub 옆에 **Stateless Minimal API** 가 같은 프로세스에서 간섭 없이 동작합니다.
> 마지막으로 **AI Ops Assistant** 가 동일 대시보드에서 SSE 스트리밍으로 P99 스파이크 원인을
> 자연어로 답합니다 — LLM Provider 추상화로 OpenAI 장애 시 Mock 으로 즉시 failover.
> 전체 핫패스는 BenchmarkDotNet + xUnit 35 테스트로 회귀 방어됩니다."*

---

## 이 포폴이 증명하는 5가지 질문

*"수만 개 패킷이 쏟아질 때 서버가 어떻게 버티고, 메모리를 얼마나 효율적으로 관리하는가."*
— 이 한 줄의 면접 질문을 **다섯 개의 축**으로 쪼개서 각각 코드/수치/시연으로 답한다.
각 섹션은 **문제 → 설계 결정 → 측정 결과 → 코드 포인터** 순서로 기록되어 있다.

| # | 축 | 한 줄 요약 | 수치 / 핵심 |
|---|---|---|---|
| [①](#-zero-allocation-최적화--대표-시연) | **핫패스 최적화** | LINQ AOI 필터를 ArrayPool 기반으로 교체, 런타임 토글 | **82KB → 0B, 3.8×** |
| [②](#-수평-확장--한-줄로-n대-스케일-아웃) | **분산 아키텍처** | Redis Backplane 한 줄로 Hub 코드 무수정 N대 확장 | `.UseRedisGroup()` 1 LOC |
| [③](#-운영관측성--self-hosted-관제-대시보드) | **운영/관측성** | lock-free 히스토그램 + 1s 롤업 + 60s 슬라이딩 차트 | P50/P95/**P99** 실시간 |
| [④](#-stateful-서버-생명주기--튕기지-않는-드레인) | **생명주기** | K8s HPA 축소 시 Readiness/Liveness 분리 + 25s 드레인 | Write-Behind 65536 큐 |
| [⑤](#-ai-운영-보조자--스파이크를-자연어로) | **AI Ops** | KPI/Latency/GC 텔레메트리 → LLM → 자연어 원인 추정 (SSE) | Provider 1줄 교체 failover |

---

## ① Zero-Allocation 최적화 — 대표 시연

**문제.** MagicOnion StreamingHub 의 `MoveAsync` 핫패스에서 매 틱마다 AOI 필터가 호출된다.
LINQ `.Where().Select().ToList()` 는 호출당 **4종의 힙 객체** (`WhereEnumerator`,
두 번째 박싱, `SelectEnumerator`, `List<int>`) 를 생성한다.
1000 봇 × 10 TPS 환경에서 초당 약 **165 MB 신규 할당** → Gen0 GC 폭증 → 순간적 스파이크 latency.

**설계.** 같은 코드베이스에 **Naive / Optimized 두 구현을 동시에** 둔 뒤,
`OptimizationMode` 싱글턴으로 런타임 스위칭되게 만들었다 — **이것이 대시보드 토글의 정체**.

- `ArrayPool<int>.Shared.Rent(256)` 로 결과 버퍼 풀링
- `ConcurrentDictionary<int, Vec2>.Enumerator` 의 struct enumerator 특성 활용 → `foreach` 박싱 0
- `Math.Sqrt` 생략, squared-distance 비교로 단축

```csharp
// Naive — 호출당 4종 힙 객체
var hits = _world.ToArray()
    .Where(kv => kv.Key != self)
    .Where(kv => InRange(kv.Value))
    .Select(kv => kv.Key)
    .ToList();

// Optimized — 호출당 0 B
var buf = ArrayPool<int>.Shared.Rent(256);
try {
    foreach (var kv in _world) {
        if (kv.Key == self) continue;
        var dx = kv.Value.X - x; var dy = kv.Value.Y - y;
        if (dx*dx + dy*dy > sq) continue;
        buf[count++] = kv.Key;
    }
} finally { ArrayPool<int>.Shared.Return(buf); }
```

**측정.** BenchmarkDotNet 0.14 · Apple M4 Max · .NET 10 RyuJIT · Server GC:

| PlayerCount | Method    |       Mean |  Gen0  | Allocated |
|------------:|-----------|-----------:|-------:|----------:|
|         100 | LINQ      |     508 ns | 0.0124 |   2,112 B |
|         100 | **Pool**  | **177 ns (0.35×)** |  — |  **0 B** |
|       1,000 | LINQ      |   4,319 ns | 0.0992 |  16,928 B |
|       1,000 | **Pool**  | **1,148 ns (0.27×)** |  — | **0 B** |
|       5,000 | LINQ      |  14,583 ns | 0.5035 |  82,840 B |
|       5,000 | **Pool**  | **3,826 ns (0.26×)** |  — | **0 B** |

대시보드에서 토글을 ON 으로 바꾸는 순간 **Alloc Rate · Gen0 Rate 가 0 으로 평행선**,
**P99 Latency 그래프가 한 단계 내려간다** — 포폴의 메인 시연 포인트.

**코드.**
[Server/Services/AoiFilter.cs](Server/Services/AoiFilter.cs) ·
[Server/Hubs/MovementHub.cs:89](Server/Hubs/MovementHub.cs#L89) ·
[Benchmarks/AoiBenchmarks.cs](Benchmarks/AoiBenchmarks.cs) ·
전체 측정치 [docs/BENCHMARK.md](docs/BENCHMARK.md)

---

## ② 수평 확장 — 한 줄로 N대 스케일 아웃

**문제 1 (브로드캐스트).** 단일 MagicOnion 인스턴스의 `Group.All.OnPlayerMoved` 는
**그 프로세스에 붙은 클라에게만** 전파된다. 서버가 2대로 늘면 다른 서버에 붙은
같은 룸 유저는 서로 안 보인다.

**문제 2 (NewSQL PK).** TiDB/Spanner 같은 분산 DB 에서 `BIGINT AUTO_INCREMENT` PK 는
모든 INSERT 가 같은 Region 으로 쏠려 쓰기 병목.

**설계.**

- **Redis Backplane** — `MagicOnion.Server.Redis 7.10` 으로 Group 을 Redis Pub/Sub 채널로 승격.
  코드 변경은 [`Program.cs`](Server/Program.cs) 의 **`.UseRedisGroup()` 한 줄**. Hub 코드는 무수정.
- **UUID v7 PK** — `Guid.CreateVersion7()` 는 앞 48bit 가 Unix 밀리초라 B-Tree locality 를
  유지하면서도 샤드 전체에 고루 분산 ([MovementHub.cs:59](Server/Hubs/MovementHub.cs#L59)).
- **Write-Behind Channel** — Hub 는 `TryEnqueue` (nanoseconds) 만 실행,
  `MatchFlushJob` 이 백그라운드에서 Dapper Bulk INSERT → 메인 스레드 DB I/O = 0
  ([MatchWriteQueue.cs](Server/Services/MatchWriteQueue.cs)).
- **Graceful degrade** — `ConnectionStrings:RedisBackplane` 이 비면 등록 자체를 건너뜀.
  로컬 단일 프로세스 개발은 그대로 동작.

**측정/시연.**

```bash
# 서버 2대 + 공유 Redis Backplane
docker compose --profile scale up -d

# 봇을 두 URL 로 라운드 로빈 주입 → 대시보드에서 동일 룸 인원 동기 확인
dotnet run --project BotClients -c Release -- \
  1000 http://localhost:5050,http://localhost:5060 100 4 even
```

**코드.**
[Server/Program.cs](Server/Program.cs) ·
[Server/Services/MatchWriteQueue.cs](Server/Services/MatchWriteQueue.cs) ·
[Server/Jobs/MatchFlushJob.cs](Server/Jobs/MatchFlushJob.cs) ·
[docker-compose.yml](docker-compose.yml)

---

## ③ 운영/관측성 — self-hosted 관제 대시보드

**문제.** 외부 APM (Datadog/NewRelic) 없이도 **면접 현장에서 그래프를 직접 보여줄 수 있어야**
한다. 게임 서버는 P99 Latency · GC 압박 · TPS 가 한 화면에 있어야 장애를 읽을 수 있다.

**설계.**

- **Lock-free 19-bucket 히스토그램** — 핫패스에서 `Interlocked.Increment(ref _buckets[b])` 만
  실행해 zero-contention. P50/P95/P99 는 1초마다 집계
  ([LatencyHistogram.cs](Server/Services/LatencyHistogram.cs)).
- **KpiRollupJob (1s BackgroundService)** — Peak/Avg TPS 누적, P-percentile 스냅샷
  ([KpiRollupJob.cs](Server/Jobs/KpiRollupJob.cs)).
- **RankingSnapshotJob (15s)** — Redis ZRANGE 기반 Top-N 주기 스냅샷.
- **Canvas + Chart.js 대시보드** — 60s 슬라이딩 윈도우로 **Packets/sec · Alloc Rate · Gen0 Rate ·
  P99 Latency** 4채널 동시 렌더 + Heatmap/Dots 전환 + rate 지표 토글
  ([wwwroot/index.html](Server/wwwroot/index.html)).
- **Zero-Alloc 런타임 토글** — `POST /api/optimize?on=true` 한 번으로 AOI 구현 스위칭 → 그래프에 즉시 반영.

**측정.** 로컬 40봇 cluster 시나리오 스모크:

| 지표 | 값 |
|---|---|
| Packets/sec | ~400 |
| Avg AOI hits (even) | ~1 |
| Avg AOI hits (cluster) | **19 (20× 밀집 스파이크)** |
| P50 Latency (loopback) | 1 ms |
| P99 Latency (loopback) | 2 ms |

**코드.**
[Server/Services/LatencyHistogram.cs](Server/Services/LatencyHistogram.cs) ·
[Server/Services/KpiSnapshot.cs](Server/Services/KpiSnapshot.cs) ·
[Server/Jobs/KpiRollupJob.cs](Server/Jobs/KpiRollupJob.cs) ·
[Server/wwwroot/index.html](Server/wwwroot/index.html)

---

## ④ Stateful 서버 생명주기 — 튕기지 않는 드레인

**문제.** K8s HPA 가 Pod 을 축소할 때 그 Pod 에 붙어있는 StreamingHub 세션은 SIGKILL 로
**즉시 끊긴다**. 유저는 튕기고, Write-Behind 큐에 남은 MatchRecord 는 유실.

**설계 (앱 ↔ K8s 양쪽 협조).**

- `IHostedLifecycleService.StoppingAsync` →
  `ReadinessGate.MarkNotReady()` →
  `/health/ready` 503 응답 →
  K8s Service 가 Pod 을 라우팅에서 제거 →
  `MetricsService.ConnectedPlayers == 0` 이 될 때까지 **최대 25초 대기**.
- **Liveness / Readiness 분리** — 드레인 중 `/health/live` 는 계속 200 → K8s 가 재시작하지 않음.
- **K8s 측** — `lifecycle.preStop: sleep 10` 으로 LB 의 엔드포인트 제거 전파 대기,
  `terminationGracePeriodSeconds: 60` 으로 SIGKILL 지연.
- **Write-Behind 큐** — `Channel<MatchRecord>` Bounded(65536, DropOldest). 폭주해도 메인 스레드는
  영향 없음 — 오래된 쓰기가 먼저 드롭.

**코드.**
[Server/Lifecycle/GracefulShutdownService.cs](Server/Lifecycle/GracefulShutdownService.cs) ·
[Server/Lifecycle/ReadinessGate.cs](Server/Lifecycle/ReadinessGate.cs) ·
[k8s/server.yaml](k8s/server.yaml) ·
[Server/Jobs/MatchFlushJob.cs](Server/Jobs/MatchFlushJob.cs)

---

## ⑤ AI 운영 보조자 — 스파이크를 자연어로

**문제.** P99 Latency 그래프가 튀었다. **왜?** 지금까지는 대시보드를 보고, 로그를 뒤지고,
`allocRate` · `gen0PerSec` · `optimized` 토글 상태를 머릿속에서 상관시키는 게 운영자의 몫이었다.
면접 자리에서도 "이 스파이크는 이래서 저래서 튀었습니다" 를 **즉석에서 증명**할 수단이 없었다.

**설계.** 포폴이 이미 보유한 텔레메트리 (`KpiSnapshot` · `LatencyHistogram` · `MetricsService`
· `OptimizationMode`) 를 LLM 에 주입해 자연어 진단을 스트리밍으로 받는다.
**새 데이터 수집 없이 기존 관측 데이터의 해석 레이어**로 얹는 게 핵심.

- **LLM Provider Registry 추상화** — 본업의 멀티 프로바이더 라우팅 패턴 이식.
  `ILlmProvider` 인터페이스 뒤에 `MockLlmProvider` (결정적 캔드 응답, 면접 안전장치) +
  `OpenAiLlmProvider` (Chat Completions `stream=true` SSE 파싱). OpenAI 장애/과금 시
  `Llm:Provider=mock` 한 줄 변경으로 즉시 failover.
- **SpikeAnalyzer** — 텔레메트리 → 구조화된 프롬프트 직렬화를 분리해 단위 테스트 가능.
  시스템 프롬프트는 *"이 서버에 실제 존재하는 조치(토글/스케일아웃/드레인) 만 제안"* 으로 하드 제약.
- **SSE 스트리밍** — `GET /api/ops/analyze/spike?minutes=5` 가 `text/event-stream` 으로
  토큰 단위 응답. 대시보드는 `EventSource` 한 줄로 수신해 패널에 append. nginx 앞단에선
  `X-Accel-Buffering: no` 로 버퍼링 해제.
- **대시보드 통합** — Time Series 차트 바로 아래 **AI Ops Assistant 패널**.
  Provider 배지 색상으로 Mock/OpenAI 시각 구분 (Mock = 빨강, OpenAI = 초록) —
  면접 데모 중 실수로 과금 내는 참사 방지.

**측정/시연.**

```bash
curl -sN "http://localhost:5050/api/ops/analyze/spike?minutes=5"
# data: [Mock Provider · P99 스파이크 분석]
# data: 최근 관측 구간에서 P99 Latency 상승 신호를 확인했습니다.
# data: - 가설 1: Zero-Allocation 모드가 OFF 인 상태에서 cluster 부하가 유입되면 ...
# event: done
```

Mock Provider 는 입력 프롬프트를 보고 패턴 분기해 "그럴듯한" 진단을 토큰당 25ms 로 흘림 —
**실 LLM 의 스트리밍 UX 를 네트워크 없이 재현**. OpenAI 로 전환하려면
`appsettings.json` 의 `Llm:Provider=openai` + `Llm:ApiKey` 만 채우면 끝.

**코드.**
[Server/Services/Llm/ILlmProvider.cs](Server/Services/Llm/ILlmProvider.cs) ·
[Server/Services/Llm/MockLlmProvider.cs](Server/Services/Llm/MockLlmProvider.cs) ·
[Server/Services/Llm/OpenAiLlmProvider.cs](Server/Services/Llm/OpenAiLlmProvider.cs) ·
[Server/Services/Ops/SpikeAnalyzer.cs](Server/Services/Ops/SpikeAnalyzer.cs) ·
[Server/Endpoints/OpsEndpoints.cs](Server/Endpoints/OpsEndpoints.cs) ·
[Server/wwwroot/index.html](Server/wwwroot/index.html)

**어필.** *"게임 서버 운영의 '알람 → 대시보드 → 로그 뒤짐' 3단계를 AI 스트리밍 한 번으로 압축.
본업에서 운영 중인 멀티 프로바이더 라우팅 패턴을 게임 서버 관측성에 이식 — OpenAI 장애 시
Mock/로컬 모델로 즉시 failover 가능한 프로덕션 구조."*

---

## Quick Start (infra 없이 바로)

연결 문자열이 비어 있으면 **자동 graceful-degrade**
(in-memory leaderboard / no DB / 단일 프로세스 Group) 로 동작한다.

```bash
# 1) 서버 기동 — Kestrel(HTTP:5050 / HTTP2:5001) + MagicOnion + 대시보드
dotnet run --project Server -c Release

# 2) 새 터미널에서 봇 부하 주입 (1000봇, 100ms 틱, 4룸, cluster 시나리오)
dotnet run --project BotClients -c Release -- 1000 http://localhost:5001 100 4 cluster

# 3) 브라우저 대시보드
open http://localhost:5050
```

`BotClients` 인자: `<botCount> <serverUrl(s)> <tickMs> [roomCount] [scenario]`
- `serverUrl(s)`: 콤마 구분 → 다중 서버 라운드 로빈
- `scenario`: `even` (기본) / `herd` (동시 접속) / `cluster` (보스 좌표 수렴, O(N²) 밀집)

---

## 🎬 시연 GIF 캡처하기

대시보드 토글 장면을 15초 GIF 로 만들어 README 상단 플레이스홀더에 꽂는 방법.

```bash
# 1) 서버 + 봇 (cluster 시나리오면 Alloc 차이가 가장 극적)
dotnet run --project Server -c Release &
dotnet run --project BotClients -c Release -- 1000 http://localhost:5001 100 4 cluster &
open http://localhost:5050

# 2) macOS 화면 녹화 — Cmd+Shift+5 → "선택 영역 기록" → 차트 패널 영역 드래그
#    녹화 시작 후:
#      ① 5초 대기  (OFF 상태에서 Alloc/Gen0 그래프 상승 확인)
#      ② Zero-Alloc 버튼 클릭 (OFF → ON)
#      ③ 10초 대기  (그래프 평행선 + P99 하강 관찰)
#    녹화 종료 → ~/Desktop/화면\ 기록*.mov 생성

# 3) MOV → GIF 변환 (gifski 권장, 품질/크기 밸런스 최고)
brew install gifski ffmpeg
ffmpeg -i ~/Desktop/화면\ 기록*.mov -vf "fps=15,scale=1200:-1" -f yuv4mpegpipe - \
  | gifski -o docs/images/toggle-demo.gif -

# 대안: ffmpeg 단독 (gifski 설치 불가 시)
ffmpeg -i ~/Desktop/화면\ 기록*.mov \
  -vf "fps=15,scale=1200:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse" \
  docs/images/toggle-demo.gif
```

권장 가이드라인: **폭 1200px, 15fps, 15초 이내, 5MB 이하.**
깃허브에서 로딩이 느리면 `scale=1000:-1` · `fps=12` 로 더 압축.

---

## 부하 시나리오

```bash
# Thundering Herd — 1000 봇이 1초 안에 동시 Join, 커넥션 병목 시연
dotnet run --project BotClients -c Release -- 1000 http://localhost:5001 100 1 herd

# AOI Cluster — 모든 봇이 (600, 360) 로 수렴, 반경 200px 안에 밀집
# → 브로드캐스트가 O(N²) 로 폭주 → Zero-Alloc 토글 효과가 가장 극적
dotnet run --project BotClients -c Release -- 1000 http://localhost:5001 100 4 cluster
```

---

## Full Stack (docker-compose)

```bash
docker compose up -d                         # MySQL + Redis + 서버
docker compose --profile load up -d          # + 봇 부하
docker compose --profile scale up -d         # 서버 2대(5050,5060) + Redis Backplane
```

서버 환경변수:
- `ConnectionStrings__Mysql` — 비우면 in-memory (DbUp/Dapper 비활성)
- `ConnectionStrings__Redis` — Leaderboard 용 (비우면 `InMemoryLeaderboard` 폴백)
- `ConnectionStrings__RedisBackplane` — **MagicOnion Group Pub/Sub** (비우면 단일 프로세스)

---

## K8s / GKE 배포

매니페스트 6종 ([k8s/](k8s/)): `namespace`, `redis`, `mysql`(PVC+Secret),
`server`(ConfigMap + Deployment + Service + LoadBalancer),
`server-hpa`(CPU 70% / Mem 80%, 1–10 replicas), `bots-job`.

```bash
kubectl apply -f k8s/
```

단계별 GKE Autopilot 가이드: [docs/DEPLOY_GKE.md](docs/DEPLOY_GKE.md).

---

## 테스트 & 벤치마크

```bash
# xUnit 35 tests — AoiFilter 동치성, Snapshot/Leaderboard/Kpi, API E2E,
# Latency, WriteQueue(UUID v7 time-ordering), Lifecycle
dotnet test Tests/Server.Tests -c Release

# BenchmarkDotNet — AOI Naive vs Optimized × 100/1000/5000 명
dotnet run --project Benchmarks -c Release -- --job short --filter '*'
```

---

## 대시보드 엔드포인트 레퍼런스

| 엔드포인트 | 용도 |
|---|---|
| `GET /api/metrics`                     | Live counters + 최적화 모드 |
| `GET /api/snapshot?room=X`             | 룸별 좌표 flat 배열 (150ms 폴링) |
| `GET /api/rooms`                       | 룸 목록 + 접속자 수 |
| `GET /api/kpi`                         | Peak/Avg TPS + P50/P95/P99 |
| `GET /api/leaderboard?room=X&n=10`     | Top-N 스코어 |
| `GET /api/ranking?room=X`              | 15초 스냅샷 (`RankingSnapshotJob`) |
| `POST /api/optimize?on=true`           | **Zero-Alloc 런타임 토글** |
| `GET /api/profile/{id}` · `POST /api/gacha/{id}` · `GET /api/mail/{id}` | Hybrid API (Stateless Minimal API) |
| `GET /api/ops/provider`                | **AI Ops**: 현재 LLM Provider 식별 (`mock`/`openai`) |
| `GET /api/ops/analyze/spike?minutes=5` | **AI Ops**: P99 스파이크 자연어 분석 (SSE 스트리밍) |
| `GET /health/live`                     | K8s Liveness (드레인 중에도 200) |
| `GET /health/ready`                    | K8s Readiness (SIGTERM 후 503) |

---

## 기술 스택

| 분류 | 선택 | 선택 이유 |
|---|---|---|
| Runtime       | **.NET 10.0** (Arm64 RyuJIT / Server GC) | |
| RPC           | **MagicOnion 7.10** StreamingHub (gRPC HTTP/2) | .NET 게임 서버 생태계 주류 |
| 직렬화        | **MessagePack 3.1** | JSON 대비 크기·속도 우위 |
| Backplane     | **MagicOnion.Server.Redis** (Multicaster) | 한 줄 통합, Hub 코드 무수정 |
| DB            | **Dapper + MySqlConnector** | 공고 "MySQL 쿼리 최적화" 와 정렬 (EF Core ✗) |
| Migration     | **DbUp** (v001~v003 raw SQL) | DBA/운영과 `.sql` 공유 가능 |
| 캐시/랭킹     | **StackExchange.Redis** (Sorted Set) | |
| Observability | **자작 lock-free 히스토그램 + Chart.js** | 외부 APM 의존 제거 |
| AI Ops        | **LLM Provider Registry** (ILlmProvider / Mock / OpenAI) + **SSE 스트리밍** | 본업 프로덕션 패턴 이식, 데모 안전 |
| Bench         | **BenchmarkDotNet 0.14** | |
| Test          | **xUnit + FluentAssertions + `WebApplicationFactory<Program>`** | 35 tests |
| Infra         | **Docker multi-stage + compose + K8s** (Deployment/HPA/Service/Secret/PVC) | |

---

## 프로젝트 구조

```
Aiming_PoC_SyncServer.slnx
├── Shared/                     # 클-서 공유 계약 (IMovementHub, PlayerMoveDto)
│
├── Server/                     # ASP.NET Core + MagicOnion
│   ├── Program.cs              # Kestrel(5050 HTTP / 5001 HTTP2) + DI 와이어링
│   ├── Hubs/MovementHub.cs     # Join/Move/Leave + AOI + UUID v7 + WriteQueue
│   ├── Services/
│   │   ├── AoiFilter.cs            # Naive LINQ vs Optimized(ArrayPool)
│   │   ├── LatencyHistogram.cs     # 19-bucket lock-free
│   │   ├── MatchWriteQueue.cs      # Channel<MatchRecord> 65536
│   │   ├── KpiSnapshot.cs          # Peak/Avg + P50/P95/P99
│   │   ├── Llm/                    # Phase 16 — ILlmProvider / Mock / OpenAI (SSE)
│   │   ├── Ops/                    # Phase 16 — SpikeAnalyzer (텔레메트리 → 프롬프트)
│   │   └── {Redis,InMemory}Leaderboard.cs
│   ├── Jobs/
│   │   ├── KpiRollupJob.cs         # 1s BackgroundService
│   │   ├── RankingSnapshotJob.cs   # 15s BackgroundService
│   │   └── MatchFlushJob.cs        # 100건/1s 배치 INSERT
│   ├── Persistence/                # DbUp + Dapper + V00X__*.sql
│   ├── Lifecycle/                  # ReadinessGate + GracefulShutdownService
│   ├── Endpoints/
│   │   ├── ProfileEndpoints.cs     # Phase 14 — Hybrid Minimal API
│   │   └── OpsEndpoints.cs         # Phase 16 — AI Ops (SSE)
│   └── wwwroot/index.html              # Canvas 레이더 + Chart.js + AI Ops 패널
│
├── BotClients/                 # Headless 부하 발생기 (even/herd/cluster)
├── Benchmarks/                 # BenchmarkDotNet (Naive vs Optimized)
├── Tests/Server.Tests/         # xUnit 35 tests
│
├── k8s/                        # namespace/redis/mysql/server/hpa/bots-job
├── docker-compose.yml          # mysql+redis+server(+server2)+bots
├── .github/workflows/ci.yml    # build / test / benchmark regression / docker
└── docs/
    ├── OVERVIEW.md             # 프로젝트 합본 — 한 번에 이해
    ├── PLAN.md                 # 전체 Phase 로드맵 (0~15)
    ├── GLOSSARY.md             # 용어집
    ├── JOB_AIMING.md           # 채용 공고 ↔ PoC 매핑
    ├── BENCHMARK.md            # BenchmarkDotNet 결과
    ├── DEPLOY_GKE.md           # GKE Autopilot 배포
    └── images/                 # ← 시연 GIF/스크린샷을 여기에
```

---

## 포트 레퍼런스

| 포트 | 프로토콜 | 용도 |
|---|---|---|
| 5050 | HTTP/1.1    | 대시보드 + REST API + health probes |
| 5001 | HTTP/2 (h2c)| MagicOnion gRPC 스트리밍 허브 |
| 3306 | MySQL       | 영속화 (선택) |
| 6379 | Redis       | Leaderboard + Backplane (선택) |

> macOS 는 기본 포트 5000 을 AirPlay 가 점유하므로 5050 을 사용.
> 변경: [Server/Program.cs](Server/Program.cs) 의 `ListenAnyIP` +
> [Server/Properties/launchSettings.json](Server/Properties/launchSettings.json).

---

## 더 읽기

- [docs/OVERVIEW.md](docs/OVERVIEW.md) — 프로젝트 합본, 요청 lifecycle, 설계 결정 근거
- [docs/PLAN.md](docs/PLAN.md) — 전체 Phase 로드맵 (0~15)
- [docs/BENCHMARK.md](docs/BENCHMARK.md) — BenchmarkDotNet 상세
- [docs/DEPLOY_GKE.md](docs/DEPLOY_GKE.md) — GKE Autopilot 배포 가이드
- [docs/JOB_AIMING.md](docs/JOB_AIMING.md) — 채용 공고 ↔ 구현 매핑
- [docs/GLOSSARY.md](docs/GLOSSARY.md) — 용어집
