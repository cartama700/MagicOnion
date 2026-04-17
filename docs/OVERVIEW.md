# Aiming PoC — Project Overview

> 이 문서는 **프로젝트 전체를 한 번에 이해할 수 있는 합본**이다.
> Quick start 는 [../README.md](../README.md), 상세 페이즈는 [PLAN.md](PLAN.md),
> 모르는 단어는 [GLOSSARY.md](GLOSSARY.md) 를 참조하라.

---

## 1. 한 줄 정리

**".NET 10 + MagicOnion 기반 헤드리스 부하 테스트 봇과 웹 관제 대시보드를 하나로 묶은,
에이밍(Aiming) 백엔드 엔지니어 포지션 저격용 포트폴리오."**

Unity 같은 실제 게임 클라이언트를 만들지 않고,
**서버 설계 / Zero-Allocation 최적화 / 분산 아키텍처 / Stateful 서버 생명주기 / 관측성**의
다섯 가지 역량을 한 레포에서 증명하는 것이 목적이다.

---

## 2. 아키텍처 (한 장짜리)

```
        [BotClients]  (C# Console, 수천 세션)
              │  gRPC (HTTP/2 h2c, MagicOnion StreamingHub)
              ▼
┌───────────────────────────────────────────────────────────────────┐
│  Server (ASP.NET Core 10 + Kestrel)                               │
│  ┌──────────────────────────────────────────────────┐             │
│  │ HTTP :5050  ──  대시보드 (Canvas + Chart.js)     │             │
│  │                 REST API  (/api/metrics, …)      │             │
│  │                 /health/{live,ready}             │             │
│  └──────────────────────────────────────────────────┘             │
│  ┌──────────────────────────────────────────────────┐             │
│  │ HTTP/2 :5001 ─  MagicOnion StreamingHub          │             │
│  │                 (MovementHub / IMovementHub)     │             │
│  └──────────────────────────────────────────────────┘             │
│                                                                   │
│   Hub 핫패스: IncrementPacket → SnapshotService.Set                │
│             → LatencyHistogram.Record(now-SentAtMs)               │
│             → AoiFilter.{Naive|Optimized}  ← [Zero-Alloc 토글]    │
│             → Group.All.OnPlayerMoved (+ Redis Pub/Sub 전파)       │
│             → MatchWriteQueue.TryEnqueue (Leave 시 1회)           │
│                                                                   │
│  BackgroundServices:                                              │
│   · KpiRollupJob       1s   TPS/P99 집계                          │
│   · RankingSnapshotJob 15s  룸별 Top-10                           │
│   · MatchFlushJob      100건/1s  Dapper Bulk INSERT               │
│                                                                   │
│  Lifecycle:                                                       │
│   · ReadinessGate + GracefulShutdownService (IHostedLifecycle)    │
└───────────────────────────────────────────────────────────────────┘
         │                              │                  │
         ▼                              ▼                  ▼
  [Redis :6379]                  [MySQL :3306]         [Redis :6379]
  Sorted Set 랭킹                 DbUp 마이그레이션       Pub/Sub Backplane
  (lb:{roomId})                   match_record (UUID v7) (다중 서버 Group 공유)
                                  player_profile
```

**graceful-degrade**: 위 세 외부 자원(Redis 랭킹 / MySQL / Redis Backplane) 중
어떤 것이 없어도 서버는 그대로 동작한다. 연결 문자열이 비면 자동으로
`InMemoryLeaderboard` / `NullPlayerRepository` / 단일 프로세스 Group 으로 폴백.

---

## 3. 한 패킷의 여정 (Request Lifecycle)

봇이 `MoveAsync({PlayerId:42, X:300, Y:200, SentAtMs:now})` 를 쏘면:

1. **gRPC 프레이밍** — `MessagePack` 으로 직렬화되어 HTTP/2 frame 으로 서버 5001 도착.
2. **MagicOnion 디스패치** — `MovementHub.MoveAsync(PlayerMoveDto)` 호출.
3. **Metrics** — `MetricsService.IncrementPacket()` (Interlocked).
4. **Snapshot** — `SnapshotService.Set("world", 42, 300, 200)` 로 ConcurrentDictionary 갱신.
5. **Latency** — `now - SentAtMs` 를 `LatencyHistogram.Record(...)` 에 버킷 카운트 증가 (Interlocked).
6. **AOI Filter** — 같은 룸에서 반경 200px 안의 이웃 수를 센다.
   - `OptimizationMode.IsOn == true` → ArrayPool + 수동 루프 (**0 alloc**)
   - `== false` → LINQ `.Where().Select().ToList()` (대량 alloc)
7. **Broadcast** — `_room.All.OnPlayerMoved(moveData)` — MagicOnion 이 직렬화 재사용.
   - Backplane 활성화 시 Redis Pub/Sub 으로 `server2` 에도 전파.
8. **응답 없음** — StreamingHub 는 일방향이므로 여기서 끝 (수 μs).

세션이 끊어질 때(Leave 또는 Disconnect) 만:
- `MatchWriteQueue.TryEnqueue(new MatchRecord(UUIDv7, …))` — **Channel 한 줄**. DB I/O 는 메인 스레드에 0.
- 1초 뒤 (또는 100건 차면 즉시) `MatchFlushJob` 이 Dapper Bulk INSERT 로 트랜잭션 반영.

---

## 4. 주요 설계 결정과 근거

### 4.1 Zero-Allocation 토글 (Phase 2)

**문제**: LINQ `.Where().Select().ToList()` 는 호출당 4종의 힙 객체 생성 → Gen0 폭증.

**해법**: `ArrayPool<int>.Shared.Rent(256)` 으로 버퍼 재사용 + struct enumerator `foreach` +
매뉴얼 거리 계산. → **alloc 0B, 3.8× 빠름** ([BENCHMARK.md](BENCHMARK.md)).

**왜 토글로 만들었나**: 면접 시연에서 "같은 부하, 같은 입력, 오직 내부 로직만 바꿨을 때" 의
GC 그래프 차이를 **대시보드에서 클릭 한 번에** 보여주기 위해.

### 4.2 Dapper + DbUp (Phase 6)

**왜 EF Core 를 쓰지 않나**: 공고에 EF Core 언급이 없고, "MySQL 쿼리 최적화" 가 핵심 업무로
명시됨. SQL 을 1차 산출물로 다루는 팀에는 **Dapper + raw SQL migration** 이 톤에 맞음.

**왜 DbUp 인가**: `FluentMigrator`(C# DSL) 대비 **DBA/운영팀과 같은 .sql 파일 공유 가능**,
Evolve 대비 .NET 생태계에서 더 활발. Jenkins + 쉘스크립트 친화. 자세한 비교: [PLAN.md 4-C](PLAN.md).

### 4.3 UUID v7 PK + Write-Behind Channel (Phase 11)

**문제 1 — NewSQL 핫스팟**: TiDB / Spanner 같은 분산 DB 에서 `BIGINT AUTO_INCREMENT` PK 는
모든 신규 INSERT 가 같은 Region 에 쏠려 쓰기 병목. → **UUID v7**: 앞 48비트가
Unix 밀리초라 time-ordered 를 유지하면서도 샤드 전체에 고루 분산.

**문제 2 — 메인 스레드 DB I/O**: 게임 틱에서 `await conn.ExecuteAsync(...)` 는 수 ms 블로킹.
→ **`Channel<MatchRecord>` Bounded 65536 + DropOldest**. Hub 는 `TryEnqueue` (nanoseconds),
`MatchFlushJob` 이 백그라운드에서 배치 INSERT. 폭주해도 메인 스레드는 영향 없음
(오래된 쓰기가 먼저 드롭).

### 4.4 Redis Backplane (Phase 10)

**문제**: 단일 MagicOnion 인스턴스의 `Group.All` 은 **그 프로세스의 클라에게만** 전파됨.
서버 2대 이상이면 다른 서버에 붙은 유저에게 메시지가 안 감.

**해법**: `MagicOnion.Server.Redis 7.10.0` 의 `UseRedisGroup()` 으로 Group 을 Redis Pub/Sub
채널로 승격. **코드 변경은 Program.cs 한 줄**, Hub 코드는 그대로.

**graceful-degrade**: `ConnectionStrings:RedisBackplane` 이 비면 등록 자체를 건너뛰므로
로컬 단일 프로세스 개발은 달라지지 않는다.

### 4.5 Graceful Shutdown (Phase 13)

**문제**: K8s HPA 가 Pod 을 축소하면 그 Pod 에 접속한 StreamingHub 세션이
**즉시 끊김**. 유저는 튕김.

**해법 (양쪽 협조)**:
- 앱: `IHostedLifecycleService.StoppingAsync` → `ReadinessGate.MarkNotReady()` →
  `/health/ready` 503 → K8s Service 가 해당 Pod 을 라우팅에서 제거 →
  `MetricsService.ConnectedPlayers == 0` 될 때까지 25초 드레인.
- K8s: `lifecycle.preStop: sleep 10` 으로 LB 가 엔드포인트 제거를 인식할 시간 확보,
  `terminationGracePeriodSeconds: 60` 로 SIGKILL 을 뒤로 미룸.

**Liveness 와 Readiness 분리**: Liveness 는 드레인 중에도 200 — 재시작 방지.
Readiness 만 503 — 새 트래픽만 차단.

### 4.6 Hybrid API (Phase 14)

**현실**: 실 라이브 게임은 동기화 외에도 가챠/인벤토리/프로필 등 **Stateless Unary API** 가 필요.
이들을 별도 서버로 분리하면 운영/배포 복잡도 2배.

**해법**: Kestrel 이 protocol 별 리스너를 분리 관리하므로 **한 프로세스에서 공존 가능**.
MagicOnion StreamingHub(HTTP/2 :5001) + Minimal API (/api/profile 등, HTTP/1 :5050)
동시 서비스. 대시보드 REST 와 같은 포트를 재사용.

---

## 5. 측정 결과

### AOI 필터 벤치마크 (BenchmarkDotNet, Apple M4 Max)

| PlayerCount | Method | Mean | Gen0 | Allocated |
|---:|---|---:|---:|---:|
| 100   | Naive   | 508 ns    | 0.0124 | 2,112 B |
| 100   | Optim.  | **177 ns (0.35×)** | — | **0 B** |
| 1,000 | Naive   | 4,319 ns  | 0.0992 | 16,928 B |
| 1,000 | Optim.  | **1,148 ns (0.27×)** | — | **0 B** |
| 5,000 | Naive   | 14,583 ns | 0.5035 | 82,840 B |
| 5,000 | Optim.  | **3,826 ns (0.26×)** | — | **0 B** |

→ 풀 소스: [BENCHMARK.md](BENCHMARK.md).

### 로컬 스모크 (40 봇 · cluster 시나리오)

| 지표 | 값 |
|---|---|
| Packets/sec | ~400 |
| Avg AOI hits — `even` | ~1 |
| Avg AOI hits — `cluster` | **19 (20× 밀집 스파이크)** |
| P50 latency | 1 ms |
| P99 latency | 2 ms (loopback) |

### 테스트 커버리지

**35 / 35 xUnit 테스트 통과** — `AoiFilter` 동치성, `SnapshotService`, `InMemoryLeaderboard`,
`KpiSnapshot`, `LatencyHistogram`, `MatchWriteQueue` (UUID v7 time-ordering 포함),
`ReadinessGate`, `ApiEndpointTests` (WebApplicationFactory 로 전체 REST E2E).

---

## 6. 전체 페이즈 체크리스트

| Phase | 이름 | 상태 | 핵심 산출물 |
|---|---|---|---|
| 0 | 기본 세팅 | ✅ | 솔루션, MagicOnion 와이어링, 기본 Hub |
| 1 | 실시간 좌표 시각화 | ✅ | `/api/snapshot` + Canvas 렌더 |
| 2 | Zero-Allocation 최적화 | ✅ | `AoiFilter.Optimized` + 토글 |
| 3 | 벤치마크 | ✅ | BenchmarkDotNet + BENCHMARK.md |
| 4 | 대시보드 고도화 | ✅ | Chart.js + Heatmap + 멀티룸 |
| 5 | 배포 재현성 | 🟡 | Dockerfile + docker-compose + CI. GIF 대기 |
| 6 | Persistence & 캐시 | 🟡 | Dapper + DbUp + Redis. EXPLAIN 데모 대기 |
| 7 | 배치 & KPI | ✅ | `KpiRollupJob`, `RankingSnapshotJob` |
| 8 | 테스트 | ✅ | xUnit 35 tests, AoiFilter 리팩터 |
| 9 | K8s / GCP | 🟡 | 매니페스트 완성. 실 배포 대기 |
| 10 | Redis Backplane | ✅ | `UseRedisGroup()` + `server2` compose |
| 11 | UUID v7 + Write-Behind | ✅ | `Channel<MatchRecord>` + `MatchFlushJob` |
| 12 | 악성 부하 시나리오 | ✅ | `{even, herd, cluster}` |
| 13 | Graceful Shutdown | ✅ | `IHostedLifecycleService` + preStop |
| 14 | Hybrid API | ✅ | Minimal API `/api/{profile,gacha,mail}` |
| 15 | P99 Latency | ✅ | lock-free Histogram + Chart.js |

🟡 = 코드 완료, 시연용 측정/스크린샷 미수행.

---

## 7. 레포 레이아웃

```
MagicOnion/
├── README.md                     ← Quick start, 10분 요약
├── Aiming_PoC_SyncServer.slnx
├── docker-compose.yml            ← mysql + redis + server(+server2) + bots
├── .github/workflows/ci.yml      ← build / test / benchmark / docker
│
├── Shared/                       ← 클·서 공유 계약 (README 有)
├── Server/                       ← 주 서버 (README 有)
├── BotClients/                   ← 부하 발생기 (README 有)
├── Benchmarks/                   ← BenchmarkDotNet (README 有)
├── Tests/Server.Tests/           ← xUnit 35 tests (README 有)
│
├── k8s/                          ← 매니페스트 6종
│   ├── namespace.yaml
│   ├── redis.yaml
│   ├── mysql.yaml                ← Secret + PVC
│   ├── server.yaml               ← ConfigMap + Deployment + Svc + LB
│   ├── server-hpa.yaml           ← CPU/Mem 기반 1~10 replicas
│   └── bots-job.yaml
│
└── docs/
    ├── PLAN.md          ← 전체 Phase 로드맵 (0~15)
    ├── OVERVIEW.md      ← (이 문서) 프로젝트 합본 요약
    ├── GLOSSARY.md      ← 용어집 9 카테고리
    ├── BENCHMARK.md     ← BenchmarkDotNet 결과 표
    ├── JOB_AIMING.md    ← 채용 공고 + PoC 매핑
    └── DEPLOY_GKE.md    ← GKE Autopilot 배포 가이드
```

---

## 8. 기술 스택 요약

| 분류 | 선택 | 비선택 이유 |
|---|---|---|
| RPC | **MagicOnion 7.10** StreamingHub | SignalR: .NET 게임 서버 생태계 주류 아님 |
| 직렬화 | **MessagePack 3.1** | JSON: 크기·속도 열세 |
| Backplane | **MagicOnion.Server.Redis** (Multicaster) | SignalR Redis: 위와 동일 이유 |
| DB 접근 | **Dapper + MySqlConnector** | EF Core: 공고 키워드 "MySQL 쿼리 최적화" 와 정렬 |
| 마이그레이션 | **DbUp** | EF Migrations: 위와 동일. FluentMigrator: DBA 공유성 ↓ |
| 캐시/랭킹 | **StackExchange.Redis** (Sorted Set) | 인메모리만: 멀티 서버 공유 불가 |
| 배치 | **BackgroundService + Channel<T>** | Hangfire/Quartz: 의존성 과다 |
| 벤치 | **BenchmarkDotNet 0.14** | 자체 Stopwatch: JIT warmup·통계 검증 없음 |
| 테스트 | **xUnit + FluentAssertions + WebApplicationFactory** | NUnit: 생태계 정렬 |

---

## 9. 어필 스크립트 (면접 30초)

> *"C# 만으로 MagicOnion 서버를 만들고, 헤드리스 봇 수천 개로 부하를 걸면서
> Zero-Alloc 토글 하나로 GC 그래프를 평행선으로 만드는 시연을 대시보드에서 보여줍니다.
> 그 옆에서 MagicOnion Redis Backplane 으로 서버가 2대, 10대로 늘어나도 동일 룸 유저가
> 동기화되고, UUID v7 PK + Channel 기반 Write-Behind 로 메인 스레드는 DB I/O 를 전혀 타지 않습니다.
> K8s 는 Graceful Shutdown 으로 유저가 튕기지 않게 드레인하고, Stateful Hub 옆에
> Stateless Minimal API 가 같은 프로세스에서 간섭 없이 동작합니다.
> 전체 핫패스는 BenchmarkDotNet + xUnit 35 테스트로 회귀 방어됩니다."*

---

## 10. 다음 액션 (로드맵)

1. **Docker 시연** — docker-compose 으로 전체 스택 기동, `match_record` 실제 쓰기 확인, GIF 캡처
2. **Phase 6 튜닝 데모** — MySQL 컨테이너에서 EXPLAIN 전/후 비교, [DB_TUNING.md](DB_TUNING.md) 작성
3. **Phase 10 시연 영상** — `server` + `server2` + `cluster` 시나리오로 룸 동기화 크로스-Pod 시연
4. **Phase 9 실 배포** — GKE Autopilot 에 1회 배포해 HPA 스케일아웃 스크린샷 확보
5. **README 시연 GIF** — 토글 순간 P99 가 평행선으로 내려가는 15초 영상
