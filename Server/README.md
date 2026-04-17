# Server

**프로젝트의 주 서버 — ASP.NET Core + MagicOnion StreamingHub.**

헤드리스 봇들이 퍼붓는 gRPC 패킷을 받아 Group 브로드캐스트로 재전송하고,
옆에서 웹 대시보드(Canvas 레이더 + Chart.js) 로 관측 지표를 쏟아내는 단일 프로세스.

## 실행

```bash
dotnet run --project Server -c Release
```

- **HTTP 1.1 :5050** — 대시보드, REST API, health probes
- **HTTP/2 h2c :5001** — MagicOnion gRPC StreamingHub

`ConnectionStrings` 가 비어 있으면 **in-memory 모드로 그대로 기동**한다
(MySQL/Redis 없이도 대시보드 + 봇 + 리더보드 전부 돌아감).

## 구성 요소

### Hub
- [Hubs/MovementHub.cs](Hubs/MovementHub.cs) — Join/Move/Leave. 핫패스에서
  `AoiFilter`(Naive vs Optimized 토글), `LatencyHistogram` 기록, `MatchWriteQueue.TryEnqueue`.

### Services (싱글톤)
- [Services/AoiFilter.cs](Services/AoiFilter.cs) — **Phase 2/3 핵심.** LINQ vs ArrayPool 두 경로
- [Services/MetricsService.cs](Services/MetricsService.cs) — Interlocked TPS/접속자 카운터
- [Services/SnapshotService.cs](Services/SnapshotService.cs) — 룸별 좌표 스냅샷 (`ConcurrentDictionary`)
- [Services/OptimizationMode.cs](Services/OptimizationMode.cs) — Zero-Alloc ON/OFF 플래그
- [Services/KpiSnapshot.cs](Services/KpiSnapshot.cs) — Peak/Avg/P50/P95/P99 lock-free 누적
- [Services/LatencyHistogram.cs](Services/LatencyHistogram.cs) — 19-버킷 로그 히스토그램
- [Services/MatchWriteQueue.cs](Services/MatchWriteQueue.cs) — `Channel<MatchRecord>` Write-Behind 버퍼
- [Services/{InMemory,Redis}Leaderboard.cs](Services/) — 실시간 랭킹 (Redis ZSET / 메모리 폴백)

### BackgroundService 들
- [Jobs/KpiRollupJob.cs](Jobs/KpiRollupJob.cs) — 1초마다 `MetricsService` 델타 → `KpiSnapshot` 누적
- [Jobs/RankingSnapshotJob.cs](Jobs/RankingSnapshotJob.cs) — 15초마다 모든 룸 Top-10 스냅샷
- [Jobs/MatchFlushJob.cs](Jobs/MatchFlushJob.cs) — 100건/1초 배치 Dapper Bulk INSERT

### Persistence (MySQL)
- [Persistence/MigrationRunner.cs](Persistence/MigrationRunner.cs) — **DbUp** 임베디드 `.sql` 실행
- [Persistence/Scripts/V00X__*.sql](Persistence/Scripts/) — 버전드 raw SQL DDL (UUID v7 PK)
- [Persistence/PlayerRepository.cs](Persistence/PlayerRepository.cs) — **Dapper** + MySqlConnector, BulkInsert 트랜잭션
- [Persistence/IPlayerRepository.cs](Persistence/IPlayerRepository.cs) — 인터페이스 + `NullPlayerRepository` 폴백

### Lifecycle
- [Lifecycle/ReadinessGate.cs](Lifecycle/ReadinessGate.cs) — SIGTERM 이후 503 플래그
- [Lifecycle/GracefulShutdownService.cs](Lifecycle/GracefulShutdownService.cs) — `IHostedLifecycleService` 드레인

### Endpoints
- [Endpoints/ProfileEndpoints.cs](Endpoints/ProfileEndpoints.cs) — Phase 14 하이브리드 API (`/api/profile`, `/api/gacha`, `/api/mail`)
- REST 엔드포인트 전체는 [Program.cs](Program.cs) 에서 `MapGet`/`MapPost` 로 등록

### Static Dashboard
- [wwwroot/index.html](wwwroot/index.html) — Canvas 레이더 + Chart.js (TPS/Alloc/Gen0/P99) + KPI 카드 + Top-10

## 환경변수 / 설정

| 키 | 기본 | 의미 |
|---|---|---|
| `ConnectionStrings:Mysql` | `""` | 비우면 `NullPlayerRepository` + DbUp 스킵 |
| `ConnectionStrings:Redis` | `""` | 비우면 `InMemoryLeaderboard` 폴백 |
| `ConnectionStrings:RedisBackplane` | `""` 또는 `Redis` 재사용 | 비우면 단일 프로세스 모드 (Group 이 in-memory) |
| `DOTNET_gcServer` | `1` (Dockerfile) | 고부하 대비 Server GC |

## 빌드/배포

- [Dockerfile](Dockerfile) — multi-stage (.NET 10 SDK → aspnet runtime)
- K8s 매니페스트는 프로젝트 루트의 [../k8s/server.yaml](../k8s/server.yaml)

## 언제 이 프로젝트를 손대나

- RPC 핸들링 로직 / 브로드캐스트 규칙 변경
- 새 메트릭 / KPI / BackgroundService 추가
- DB 테이블 스키마 추가 (새 `V0XX__*.sql` 만들기)
- 새 REST 엔드포인트 추가 (`Program.cs` 또는 `Endpoints/`)
- 대시보드 UI 개선 (`wwwroot/index.html`)
