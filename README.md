# Aiming PoC — Sync Server

.NET 10 + MagicOnion 기반 **Headless 부하 테스트 봇 + 웹 관제 대시보드** PoC.
Unity 클라이언트 없이 100% C# 코드로 서버 아키텍처 설계와 Zero-Allocation 최적화
역량에 집중해 **에이밍(Aiming) 백엔드 엔지니어 포지션**을 저격하기 위한 포트폴리오.

- **프로젝트 합본**: [docs/OVERVIEW.md](docs/OVERVIEW.md) — 한 번에 이해하려면 여기부터
- **용어집**: [docs/GLOSSARY.md](docs/GLOSSARY.md) — 모르는 단어 나오면
- 상세 기획: [docs/PLAN.md](docs/PLAN.md) — 전체 Phase 로드맵
- 채용 공고 정리: [docs/JOB_AIMING.md](docs/JOB_AIMING.md)
- 벤치마크 결과: [docs/BENCHMARK.md](docs/BENCHMARK.md)
- GKE 배포 가이드: [docs/DEPLOY_GKE.md](docs/DEPLOY_GKE.md)

---

## 1. 이 프로젝트의 의도

에이밍은 MagicOnion + ASP.NET Core 로 **대규모 라이브 게임 서버**를 운영하는 회사다.
면접관(기술 임원진)이 보고 싶어하는 것은 한 줄로 요약된다:

> *"수만 개 패킷이 쏟아질 때 서버가 어떻게 버티고, 메모리를 얼마나 효율적으로 관리하는가."*

이 PoC 는 그 질문에 **네 가지 축**으로 답한다.

| 축 | 무엇을 증명하는가 |
|---|---|
| **Zero-Allocation 최적화** | `ArrayPool` + 수동 루프 기반 AOI 필터로 5,000명 기준 **82KB → 0B, 3.8x 속도** (BenchmarkDotNet 계측) |
| **분산 아키텍처** | MagicOnion Redis Backplane 으로 서버 N대에 걸친 Group 브로드캐스트, UUID v7 PK 로 NewSQL(TiDB/Spanner) 친화 |
| **운영/관측성** | Canvas 레이더 + Chart.js 시계열 + **P99 Latency** + KPI Rollup(BackgroundService) — 면접관이 토글을 누르면 GC 그래프가 평행선으로 떨어지는 시연 |
| **Stateful 서버 생명주기** | K8s Graceful Shutdown, Readiness/Liveness 분리, Write-Behind 채널로 메인 스레드 보호 |

MagicOnion StreamingHub 단일 서버를 넘어서,
**라이브 서비스 운영에 필요한 모든 구성 요소를 하나의 솔루션에 녹여두는 것**이 이 프로젝트의 목표다.

---

## 2. Quick Start (infra 없이 바로)

연결 문자열이 비어 있으면 **자동 graceful-degrade**(in-memory leaderboard / no DB) 로 돌아가므로,
MySQL/Redis 컨테이너 없이도 그대로 체험할 수 있다.

```bash
# 1) 서버 기동 — Kestrel(HTTP:5050 / HTTP2:5001) + MagicOnion + 대시보드
dotnet run --project Server -c Release

# 2) 새 터미널에서 봇 부하 주입 (기본: 1000봇, 100ms 틱, 1룸, even 시나리오)
dotnet run --project BotClients -c Release -- 1000 http://localhost:5001 100

# 3) 브라우저 대시보드
open http://localhost:5050
```

`BotClients` 인자:

```
<botCount> <serverUrl(s)> <tickMs> [roomCount] [scenario]
```

- `serverUrl(s)`: 콤마로 구분하면 다중 서버 라운드 로빈 (Phase 10 Scale-Out)
- `scenario`: `even` (기본) / `herd` (동시 접속) / `cluster` (보스 좌표로 수렴해 O(N²) 밀집)

---

## 3. Zero-Allocation 최적화 토글 시연

대시보드 헤더의 **Zero-Alloc: OFF/ON** 버튼이 핵심 시연 포인트.

```bash
# REST 로도 토글 가능
curl -X POST 'http://localhost:5050/api/optimize?on=true'
```

- **OFF**: LINQ 기반 AOI 필터 → `Gen0`/`Allocated MB` 그래프가 가파르게 상승
- **ON**: `ArrayPool<int>` + 수동 루프 → TPS 유지, **할당량 평행선, P99 latency 감소**

BenchmarkDotNet 마이크로벤치로도 검증:

```bash
dotnet run --project Benchmarks -c Release -- --job short --filter '*'
```

**5,000명 기준 3.8× 속도, 할당량 82KB → 0B** — 자세한 수치는 [BENCHMARK.md](docs/BENCHMARK.md).

---

## 4. 부하 시나리오

```bash
# Thundering Herd — 1000 봇이 1초 안에 동시 Join, 커넥션 병목 시연
dotnet run --project BotClients -c Release -- 1000 http://localhost:5001 100 1 herd

# AOI Cluster — 모든 봇이 (600, 360) 로 수렴, 반경 200px 안에 밀집
# → 브로드캐스트가 O(N²) 로 폭주 → Zero-Alloc 토글 효과가 가장 극적
dotnet run --project BotClients -c Release -- 1000 http://localhost:5001 100 4 cluster
```

로컬 스모크 (40봇 cluster): AOI hits **even 모드 ~1 vs cluster 모드 19** (20배 밀집 스파이크).

---

## 5. Full Stack (docker-compose)

MySQL + Redis + 서버 + 봇을 컨테이너 한 번에.

```bash
# MySQL + Redis + 서버만
docker compose up -d

# + 봇 부하 (1000 봇, 4룸)
docker compose --profile load up -d

# Scale-Out 시연 — 서버 2대(server:5050, server2:5060) + Redis Backplane + 다중 URL 봇
docker compose --profile scale up -d
```

서버 환경변수:
- `ConnectionStrings__Mysql` — 비우면 in-memory 모드 (DbUp/Dapper 비활성)
- `ConnectionStrings__Redis` — Leaderboard 용 (비우면 `InMemoryLeaderboard` 폴백)
- `ConnectionStrings__RedisBackplane` — **MagicOnion Group 브로드캐스트의 Pub/Sub 백플레인**
  (비우면 단일 프로세스 모드)

---

## 6. 대시보드 구성

`http://localhost:5050` 에서 볼 수 있는 것:

| 패널 | 내용 |
|---|---|
| **Live Radar** | 선택 룸의 접속자 좌표 실시간 렌더. View: `Dots` / `Heatmap` 전환 |
| **Time Series** | Packets/sec · Allocated MB · Gen0 · **P99 Latency** 60초 슬라이딩 차트 |
| **Live Metrics** | 현재 접속자, TPS, Avg AOI hits, GC 누적 할당, Gen0/1/2 |
| **KPI Rollup** | Peak Players · Peak TPS · Avg TPS · **P50/P99 Latency** (BackgroundService 가 1초 주기 집계) |
| **Top 10** | Redis ZRANGE 기반 실시간 랭킹 (in-mem 폴백), 15초 스냅샷은 `RankingSnapshotJob` |
| **Rooms** | 현재 활성 룸과 인원수 |

주요 엔드포인트:

```
GET  /api/metrics           Live counters + 최적화 모드
GET  /api/snapshot?room=X   룸별 좌표 flat 배열 (대시보드가 150ms 폴링)
GET  /api/rooms             룸 목록 + 접속자 수
GET  /api/kpi               Peak/Avg/P50/P95/P99
GET  /api/leaderboard?room=X&n=10    Top-N 스코어
GET  /api/ranking?room=X    15초 스냅샷 (RankingSnapshotJob)
POST /api/optimize?on=true  Zero-Alloc 모드 토글

GET  /api/profile/{id}      Phase 14 Hybrid API (Stateless Minimal API)
POST /api/gacha/{id}
GET  /api/mail/{id}

GET  /health/live           K8s Liveness (드레인 중에도 200)
GET  /health/ready          K8s Readiness (SIGTERM 후 503)
```

---

## 7. 테스트 & 벤치마크

```bash
# xUnit 테스트 (AoiFilter 동치성, Snapshot/Leaderboard/Kpi, API E2E, Latency, WriteQueue, Lifecycle)
dotnet test Tests/Server.Tests -c Release
# → 통과: 35/35

# 마이크로 벤치마크 (AOI 필터 Naive vs Optimized, PlayerCount 100/1000/5000)
dotnet run --project Benchmarks -c Release -- --job short --filter '*'
```

---

## 8. K8s / GCP 배포

매니페스트는 [k8s/](k8s/) 에 완성되어 있다 — `namespace`, `redis`, `mysql`(PVC+Secret),
`server`(ConfigMap + Deployment + Service + LoadBalancer), `server-hpa`(CPU 70% / Mem 80%, 1-10 replicas),
`bots-job` (부하 주입 Job).

```bash
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/redis.yaml
kubectl apply -f k8s/mysql.yaml
kubectl apply -f k8s/server.yaml
kubectl apply -f k8s/server-hpa.yaml
kubectl apply -f k8s/bots-job.yaml
```

단계별 GKE Autopilot 가이드: [docs/DEPLOY_GKE.md](docs/DEPLOY_GKE.md).

---

## 9. 프로젝트 구조

```
Aiming_PoC_SyncServer.slnx
├── Shared/                     # 클라-서버 공유 계약 (IMovementHub, PlayerMoveDto)
│
├── Server/                     # ASP.NET Core + MagicOnion
│   ├── Program.cs              # Kestrel(5050 HTTP / 5001 HTTP2) + DI 와이어링
│   ├── Hubs/MovementHub.cs     # Join/Move/Leave + AOI + UUID v7 + WriteQueue
│   ├── Services/
│   │   ├── AoiFilter.cs        # Naive LINQ vs Optimized(ArrayPool)
│   │   ├── LatencyHistogram.cs # 19-버킷 lock-free 히스토그램 (Phase 15)
│   │   ├── MatchWriteQueue.cs  # Channel<MatchRecord> 65536 (Phase 11)
│   │   ├── KpiSnapshot.cs      # Peak/Avg + P50/P95/P99 (Phase 7 + 15)
│   │   └── {Redis,InMemory}Leaderboard.cs
│   ├── Jobs/
│   │   ├── KpiRollupJob.cs         # 1s BackgroundService
│   │   ├── RankingSnapshotJob.cs   # 15s BackgroundService
│   │   └── MatchFlushJob.cs        # 100건/1초 배치 INSERT
│   ├── Persistence/
│   │   ├── MigrationRunner.cs      # DbUp (v001~v003 SQL 스크립트)
│   │   ├── PlayerRepository.cs     # Dapper + MySqlConnector
│   │   └── Scripts/V00X__*.sql
│   ├── Lifecycle/
│   │   ├── ReadinessGate.cs
│   │   └── GracefulShutdownService.cs   # IHostedLifecycleService
│   ├── Endpoints/ProfileEndpoints.cs     # Phase 14 Minimal API
│   └── wwwroot/index.html                # Canvas 레이더 + Chart.js 대시보드
│
├── BotClients/                 # Headless 부하 발생기
│   └── Program.cs              # even/herd/cluster 시나리오, 다중 URL 라운드 로빈
│
├── Benchmarks/                 # BenchmarkDotNet
│   └── AoiBenchmarks.cs        # Naive vs Optimized × 100/1000/5000 명
│
├── Tests/Server.Tests/         # xUnit + FluentAssertions (35 tests)
│
├── k8s/                        # K8s 매니페스트 (namespace/redis/mysql/server/hpa/bots-job)
├── docker-compose.yml          # MySQL + Redis + Server(+server2 scale profile) + Bots
├── .github/workflows/ci.yml    # build / test / benchmark regression / docker build
└── docs/
    ├── PLAN.md                 # 전체 Phase 로드맵 (0~15)
    ├── JOB_AIMING.md           # 채용 공고 매핑
    ├── BENCHMARK.md            # BenchmarkDotNet 결과
    └── DEPLOY_GKE.md           # GKE Autopilot 배포 가이드
```

---

## 10. 기술 스택

| 분류 | 도구 |
|---|---|
| Runtime | .NET 10.0 (Arm64 RyuJIT / Server GC) |
| RPC | **MagicOnion 7.10** StreamingHub (gRPC HTTP/2) + MessagePack 3.1 |
| Backplane | **MagicOnion.Server.Redis** (Multicaster.Distributed.Redis) |
| DB | **Dapper + MySqlConnector** + **DbUp** 마이그레이션 (EF Core 미사용 — 공고 스택 정렬) |
| Cache | **StackExchange.Redis** (Sorted Set 랭킹) |
| Observability | 자작 lock-free 히스토그램 + Chart.js |
| Bench | BenchmarkDotNet 0.14 |
| Test | xUnit + FluentAssertions + `WebApplicationFactory<Program>` |
| Infra | Docker multi-stage + docker-compose + K8s (Deployment/HPA/Service/Secret/PVC) |

---

## 11. 포트 레퍼런스

| 포트 | 프로토콜 | 용도 |
|---|---|---|
| 5050 | HTTP/1.1 | 대시보드 + REST API + health probes |
| 5001 | HTTP/2 (h2c) | MagicOnion gRPC 스트리밍 허브 |
| 3306 | MySQL | 영속화 (선택) |
| 6379 | Redis | Leaderboard + Backplane (선택) |

> macOS 는 기본 포트 5000 을 AirPlay 가 점유하므로 5050 을 사용.
> 변경하려면 [Server/Program.cs](Server/Program.cs) 의 `ListenAnyIP` 와
> [Server/Properties/launchSettings.json](Server/Properties/launchSettings.json) 수정.

---

## 12. 면접 스크립트 (30초 요약)

> *"Unity 없이 C# 만으로 MagicOnion 서버를 만들고, 헤드리스 봇 수천 개로 부하를 걸면서
> Zero-Alloc 토글 하나로 GC 그래프를 평행선으로 만드는 시연을 대시보드 한 화면에서 보여줍니다.
> 그 옆에는 MagicOnion Redis Backplane 으로 서버가 2대, 10대로 늘어나도 동일 룸의 유저가
> 정상 동기화되고, UUID v7 PK + Channel 기반 Write-Behind 로 메인 스레드는 DB I/O 를
> 전혀 타지 않습니다. K8s 는 Graceful Shutdown 으로 유저가 튕기지 않게 드레인하고,
> Stateful Hub 옆에 Stateless Minimal API 가 같은 프로세스에서 간섭 없이 동작합니다.
> 전체 핫패스는 BenchmarkDotNet + xUnit 35개 테스트로 회귀 방어됩니다."*
