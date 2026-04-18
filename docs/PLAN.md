# Aiming PoC — Sync Server Plan

.NET 10.0 + MagicOnion 기반 "Headless 부하 테스트 봇 + Web 관제 대시보드" PoC.
Unity 클라이언트 없이 100% C# 코드로 **서버 아키텍처 설계 + Zero-Allocation 최적화 역량**에
집중해 에이밍 기술 임원진에게 어필하는 것이 목적.

---

## 1. 핵심 전략

- 게임 클라이언트 대신 **압도적인 부하 테스트 관제탑**을 만든다.
- 수천 개의 봇이 실시간 패킷을 퍼붓는 상황에서 서버가 어떻게 버티는지,
  그리고 GC(가비지 컬렉션) 할당을 어떻게 제거하는지를 **웹 대시보드로 시연**한다.
- 면접관이 보고 싶은 것: *"수만 개 패킷이 쏟아질 때 서버가 어떻게 버티고,
  메모리를 얼마나 효율적으로 관리하는가."*

---

## 2. 시스템 구조 (100% C#)

```text
Aiming_PoC_SyncServer.slnx
├── Shared/                    # [Unified C#] 클라-서버 공유 계약
│   ├── IMovementHub.cs        # MagicOnion StreamingHub 인터페이스
│   └── PlayerState.cs         # MessagePack struct (Zero-alloc 대비)
│
├── Server/                    # [핵심] ASP.NET Core + MagicOnion
│   ├── Program.cs             # Kestrel(HTTP1:5050 / HTTP2:5001) + DI
│   ├── Hubs/MovementHub.cs    # 동기화 로직 (최적화 타겟)
│   ├── Services/MetricsService.cs  # TPS, GC 지표 싱글톤
│   └── wwwroot/index.html     # Canvas 레이더 + 메트릭 패널
│
└── BotClients/                # [부하 발생기] C# Console
    └── Program.cs             # 수천 개 가상 유저 세션
```

### 2.1 포트

| 포트 | 프로토콜 | 용도 |
|---|---|---|
| 5050 | HTTP/1.1 | 웹 대시보드 + `/api/metrics` |
| 5001 | HTTP/2 (h2c) | MagicOnion gRPC 스트리밍 허브 |

### 2.2 패키지 버전 (2026-04-15 기준)

- MagicOnion 7.10.0 (Server / Client / Abstractions)
- MessagePack 3.1.4
- Grpc.Net.Client 2.76.0 / Grpc.AspNetCore.Server 2.57.0
- 타겟 프레임워크: `net10.0`

---

## 3. 실행 방법

```bash
# 1) 서버 기동
dotnet run --project Server

# 2) 봇 부하 발생 (1000마리, 100ms 틱)
dotnet run --project BotClients -c Release -- 1000 http://localhost:5001 100

# 3) 대시보드
open http://localhost:5050
```

`BotClients` 인자: `<botCount> <serverUrl> <tickMs> [roomCount]` (roomCount 기본 1)

---

## 4. 로드맵

### Phase 0 — 기본 세팅 (완료)

- [x] 솔루션/프로젝트 생성 (Shared / Server / BotClients, net10.0)
- [x] MagicOnion 7.10.0 + MessagePack 3.1.4 와이어링
- [x] `IMovementHub` + `PlayerMoveDto` 계약 정의
- [x] `MovementHub` — Join / Move / Leave + Group 브로드캐스트
- [x] `MetricsService` — Interlocked 기반 TPS / 접속자 카운터
- [x] `/api/metrics` 엔드포인트 (players, packets/sec, GC 할당, Gen0~2)
- [x] `wwwroot/index.html` — Canvas 레이더 + 메트릭 패널 골격
- [x] `BotClients` — 가상 유저 N개, 랜덤 워킹

### Phase 1 — 실시간 좌표 시각화 (완료)

- [x] 서버에 플레이어 좌표 스냅샷(`ConcurrentDictionary<int, (float,float)>`) 추가 — `Server/Services/SnapshotService.cs`
- [x] `/api/snapshot` — 현재 접속자 좌표를 flat float 배열(`{p:[id,x,y,...]}`)로 반환, 150ms 폴링
- [x] `index.html` Canvas 루프에서 스냅샷을 `players` Map에 반영해 점 렌더
- 결정: 폴링으로 시작, 부드럽지 않으면 SSE 로 승격 (보류)

### Phase 2 — Zero-Allocation 최적화 (완료)

- [x] `MovementHub.MoveAsync` 에 AOI 필터 두 경로 추가 (Naive LINQ vs ArrayPool 기반)
- [x] `OptimizationMode` 싱글톤 + `POST /api/optimize?on={true|false}` 토글
- [x] 대시보드 헤더에 ON/OFF 토글 버튼 (현재 모드 색상 표시)
- [x] 데모 시나리오 가능 — `/api/metrics` 의 `gcAllocatedBytes`, `gen0` 변화로 확인

### Phase 3 — 벤치마크 & 증거 (완료)

- [x] BenchmarkDotNet 프로젝트 — `Benchmarks/AoiBenchmarks.cs`
- [x] PlayerCount 100 / 1,000 / 5,000 단계별 비교 — Naive vs Optimized
- [x] 결과를 [docs/BENCHMARK.md](BENCHMARK.md) 에 정리 (5,000명 기준 3.8x 빠름, 할당량 82KB → 0B)
- [ ] `dotnet-counters` 로 라이브 부하 스냅샷 (선택, 시연 직전 측정)

### Phase 4 — 관제 대시보드 고도화 (완료)

- [x] Chart.js 로 TPS / Allocated / Gen0 60초 슬라이딩 시계열
- [x] 접속자 Heatmap — 별도 캔버스에 잔상(`globalCompositeOperation:'lighter'`) 누적, View 셀렉터로 Dots/Heatmap 토글
- [x] 멀티룸 — `IMovementHub.JoinAsync(playerId, roomId, x, y)`, `SnapshotService` per-room dict, `/api/rooms`, `BotClients` 4번째 인자 `roomCount`
- [x] 레이아웃 폴리싱 — 좌측 캔버스+차트, 우측 메트릭+룸 목록 그리드, CSS 변수화

### Phase 5 — 배포 / 재현성 (대부분 완료)

- [x] [Server/Dockerfile](../Server/Dockerfile) + [BotClients/Dockerfile](../BotClients/Dockerfile) (multi-stage)
- [x] [docker-compose.yml](../docker-compose.yml) — MySQL + Redis + Server + Bots(`--profile load`)
- [x] [.github/workflows/ci.yml](../.github/workflows/ci.yml) — restore / build / test / BenchmarkDotNet ShortRun / Docker build
- [ ] README 의 시연 GIF / 스크린샷 (Docker 환경 부재로 시연 후 추가)

---

## 4-B. 채용 공고(Aiming) 매핑 강화 로드맵

[docs/JOB_AIMING.md](JOB_AIMING.md) 의 **필수/우대 항목 중 현 PoC 가 아직 증명하지 못한
영역**을 채우기 위한 추가 페이즈. 모두 선택이지만 면접 어필도 순으로 정렬.

### Phase 6 — Persistence & 캐시 (MySQL + Redis) (코드 완료, 튜닝 데모 대기)
> 공고 키워드: *"MySQL 쿼리 최적화", "DB 테이블 설계 및 성능 튜닝", "Redis", "TiDB"*

- [x] [Server/Persistence/Scripts/](../Server/Persistence/Scripts/) — `player_profile`, `match_record`, `room_meta` raw SQL DDL
- [x] **Dapper + MySqlConnector** — [PlayerRepository.cs](../Server/Persistence/PlayerRepository.cs)
- [x] **DbUp 마이그레이션** — [MigrationRunner.cs](../Server/Persistence/MigrationRunner.cs), 임베디드 리소스, `WithTransactionPerScript()`
- [x] **Redis 랭킹(Sorted Set)** — [RedisLeaderboard.cs](../Server/Services/RedisLeaderboard.cs) (`SortedSetIncrement` / `SortedSetRangeByRankWithScores`)
- [x] **그레이스풀 폴백** — Redis 미설정 시 [InMemoryLeaderboard.cs](../Server/Services/InMemoryLeaderboard.cs), MySQL 미설정 시 `NullPlayerRepository` (개발 진입 장벽 0)
- [x] `/api/leaderboard?room=...&n=10` 엔드포인트 + 대시보드 Top-10 패널
- [ ] **튜닝 데모**: 인덱스 전/후 `EXPLAIN` 비교, N+1 → JOIN 리팩터, 슬로우 쿼리 로그 캡처 → [docs/DB_TUNING.md](DB_TUNING.md) (MySQL 컨테이너 가용 시 측정)

### Phase 7 — 배치 & KPI 집계 (완료)
> 공고 키워드: *"배치 처리 추가/수정", "KPI 분석 기반 운영"*

- [x] [Server/Jobs/RankingSnapshotJob.cs](../Server/Jobs/RankingSnapshotJob.cs) — `BackgroundService`, 15초 주기 룸별 Top-10 스냅샷
- [x] [Server/Jobs/KpiRollupJob.cs](../Server/Jobs/KpiRollupJob.cs) — 1초 주기 packets/sec 델타 + peak/avg 누적
- [x] [KpiSnapshot.cs](../Server/Services/KpiSnapshot.cs) — Interlocked 기반 lock-free 누적기 (peak/avg/samples)
- [x] `/api/kpi` + `/api/ranking?room=...` 엔드포인트 + 대시보드 KPI 4-카드 그리드 + Top-10 패널
- [ ] 멱등성 보장 (재실행 중복 집계 X) — 처리 워터마크 테이블 (Phase 6 튜닝 데모와 함께 추가)

### Phase 8 — 테스트 (완료)
> 공고 키워드: *"설계/코드 리뷰, TDD/BDD 경험"* 

- [x] [Tests/Server.Tests/](../Tests/Server.Tests/) — xUnit + FluentAssertions, **23 테스트 통과**
- [x] [AoiFilterTests.cs](../Tests/Server.Tests/AoiFilterTests.cs) — Naive vs Optimized 동치성 (3종 [Theory] 랜덤 시드 + 경계 케이스)
- [x] [SnapshotServiceTests.cs](../Tests/Server.Tests/SnapshotServiceTests.cs) — Set/Remove/RoomList/SerializeFlat
- [x] [LeaderboardTests.cs](../Tests/Server.Tests/LeaderboardTests.cs) — InMemoryLeaderboard + KpiSnapshot lock-free 누적 검증
- [x] [ApiEndpointTests.cs](../Tests/Server.Tests/ApiEndpointTests.cs) — `WebApplicationFactory<Program>` 로 모든 `/api/*` E2E
- [x] **AoiFilter 추출 리팩터** — 테스트 가능성 확보 (Hub private 메서드 → static class)
- [ ] (보류) `MagicOnion.Integration.TestKit` 로 Hub Join/Move 시퀀스 테스트
- [ ] (보류) Reqnroll BDD 시나리오 — "토글 ON 시 할당이 0이 된다"

### Phase 9 — Kubernetes / GCP 배포 (매니페스트 완료, 실배포 대기)
> 공고 키워드: *"Google Cloud 인프라 구축", "Kubernetes"*

- [x] [k8s/](../k8s/) — `namespace` / `redis` / `mysql` (PVC + Secret) / `server` (ConfigMap + Deployment + ClusterIP + LoadBalancer) / `server-hpa` (CPU 70% / Mem 80% / 1-10 replicas) / `bots-job`
- [x] HTTP `/api/metrics` 기반 Readiness + Liveness probe — gRPC 헬스체크 대체
- [x] [docs/DEPLOY_GKE.md](DEPLOY_GKE.md) — Artifact Registry → buildx → Autopilot → kubectl apply → 시연 체크리스트
- [x] BotClients 를 K8s Job 으로 부하 주입 — `k8s/bots-job.yaml`
- [ ] 실제 GKE Autopilot 배포 + 시연 스크린샷 (비용 발생, 시연 직전 1회)

### 우선순위 요약

| 순위 | Phase | 이유 |
|---|---|---|
| 1 | Phase 8 (테스트) | 가장 적은 코드로 "설계 능력" 시그널 강함, 의존성 0 |
| 2 | Phase 6 (DB/캐시) | 공고 1순위 키워드, EXPLAIN 차트로 시각 어필 가능 |
| 3 | Phase 7 (배치/KPI) | Phase 6 위에 자연스럽게 쌓임 |
| 4 | Phase 9 (K8s/GCP) | 인프라 비용 발생, 포트폴리오 사진 한 장이면 충분 |

---

## 4-C. DbUp 학습 노트 (Phase 6 사전지식)

> **한 줄 요약**: "버전된 `.sql` 파일을 순서대로 한 번씩만 실행"해주는 .NET 라이브러리.
> Flyway(JVM) 의 C# 판이라고 보면 정확함. EF Core Migrations 의 C# DSL 과 달리,
> 마이그레이션이 **순수 SQL 파일**이라 DBA / 운영팀과 그대로 공유 가능.

### 1. 동작 모델

1. 앱 부팅 시 (또는 별도 콘솔 실행 시) DbUp 이 DB 에 접속
2. `SchemaVersions` 테이블을 자동 생성 (이미 적용한 스크립트 이름을 기록)
3. 임베디드 리소스(또는 디스크 폴더)의 `.sql` 파일을 **이름순(알파벳)** 으로 스캔
4. `SchemaVersions` 에 없는 파일만 **트랜잭션으로 실행**
5. 성공한 파일명을 `SchemaVersions` 에 기록 → 다음 실행 시 스킵

핵심: **선형 적용, 한 번만, 다운그레이드 없음**. EF 의 `Down()` 같은 롤백 개념이 없으므로
"잘못된 마이그레이션은 새 마이그레이션으로 덮는다"가 원칙.

### 2. 최소 통합 코드

```csharp
// Server/Persistence/MigrationRunner.cs
using DbUp;
using DbUp.Engine;

public static class MigrationRunner
{
    public static void EnsureSchema(string connectionString)
    {
        EnsureDatabase.For.MySqlDatabase(connectionString); // DB 자체 생성

        var upgrader = DeployChanges.To
            .MySqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(MigrationRunner).Assembly)
            .WithTransactionPerScript()  // 스크립트별 트랜잭션
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException("DB migration failed", result.Error);
    }
}

// Program.cs
MigrationRunner.EnsureSchema(builder.Configuration.GetConnectionString("Mysql")!);
```

`.csproj` 에 임베디드 리소스 등록:

```xml
<ItemGroup>
  <EmbeddedResource Include="Persistence\Scripts\*.sql" />
  <PackageReference Include="dbup-mysql" Version="6.0.0" />
</ItemGroup>
```

### 3. 마이그레이션 파일 명명 규칙

```
Server/Persistence/Scripts/
  V001__create_player_profile.sql
  V002__create_match_record.sql
  V003__add_index_match_record_room_id.sql
  V004__seed_default_rooms.sql
```

- `V{번호}__{설명}.sql` (Flyway 컨벤션 차용; 알파벳순 정렬되도록 zero-pad)
- 한 파일은 **하나의 논리 변경**만
- DML(INSERT 시드)도 동일 디렉터리에 두되 `seed_` 접두 권장

### 4. 본 PoC 에서의 배치 위치

```
Server/
├── Persistence/
│   ├── MigrationRunner.cs            # DbUp 진입점
│   ├── Scripts/                      # .sql 마이그레이션 (EmbeddedResource)
│   │   ├── V001__create_player_profile.sql
│   │   └── V002__create_match_record.sql
│   ├── PlayerRepository.cs           # Dapper 쿼리 (SELECT/INSERT)
│   └── MatchRepository.cs
└── Program.cs                        # 부팅 시 EnsureSchema 호출
```

### 5. 운영 팁 / 함정

| 항목 | 내용 |
|---|---|
| **트랜잭션 단위** | `WithTransactionPerScript()` 권장. MySQL DDL 은 암시적 커밋이라 부분 실패 시 손으로 정리 필요. |
| **이미 적용된 파일 수정** | DbUp 은 파일명만 본다 → 내용을 바꿔도 재실행 안 함. 잘못 배포 시 **새 V** 로 보정. |
| **롱 마이그레이션** | DDL 락 주의. 대용량 테이블은 `pt-online-schema-change` 를 별도 잡으로 빼고 DbUp 은 메타데이터만 갱신. |
| **CI 사전 검증** | GitHub Actions 에서 `mysql:8` 컨테이너 띄워 빈 DB 에 마이그레이션 적용 → "녹색이면 배포 안전". |
| **다중 환경** | 같은 스크립트가 dev/stage/prod 모두 돌도록 환경별 분기 SQL 금지. 환경 차이는 시드(`seed_*.sql`) 로만. |
| **EF Core 와의 공존** | DbUp 이 스키마를, EF Core 가 쿼리만 — 하이브리드 구성도 가능. 단 EF Core 의 `Migrations` 폴더는 비활성화해야 충돌 없음. |

### 6. 한 줄로 면접에서 설명하기

> *"DbUp 은 EF Migrations 의 C# DSL 대신 순수 SQL 파일을 쓰게 해주는 라이브러리고,
> Flyway 와 같은 'V001__\*.sql' 컨벤션을 따라 SchemaVersions 테이블로 멱등 적용을 보장합니다.
> Dapper 와 짝지으면 ORM 추상화 없이 SQL 을 1차 산출물로 다루면서도
> 마이그레이션 자동화는 잃지 않는 조합이 됩니다."*

---

## 4-D. 외부 피드백 반영 — 에이밍 맞춤형 보강 로드맵

> 라이브 서비스/대규모 분산 환경 관점의 외부 리뷰 + 자체 제안으로 나온 **7가지 보강 포인트**.
> 에이밍의 실제 라이브 게임 운영 톤(Stateful 서버, GKE, NewSQL, 라이브 핫픽스)에 맞춰
> 기존 Phase 6–9 위에 얹는 **"맞춤형 양념" 페이즈**.

### Phase 10 — Redis Backplane 다중 서버 Scale-Out (완료)
> *"서버 인스턴스가 3대, 10대로 늘어나면 유저 간 동기화는?"* — 면접관이 반드시 묻는 질문.

- [x] **`MagicOnion.Server.Redis 7.10.0`** 백플레인 적용 — `UseRedisGroup(configure, registerAsDefault:true)` 로 Group 브로드캐스트가 Redis Pub/Sub 으로 전파 ([Program.cs:30](../Server/Program.cs#L30))
- [x] `ConnectionStrings:RedisBackplane` 가 비어 있으면 자동 비활성 → 로컬 단일 프로세스도 그대로 동작
- [x] [docker-compose.yml](../docker-compose.yml) 에 `server2` 서비스 (`--profile scale`) + `bots-scale` — 동일 Redis 공유
- [x] BotClients 다중 URL 지원 — `"http://server:5001,http://server2:5001"` 콤마 구분, 봇 인덱스 기반 라운드 로빈
- [x] 어필: *"브로드캐스트 in-memory 한계를 Redis Pub/Sub 으로 풀고, 노드 추가 = 선형 확장 가능한 구조"*

### Phase 11 — 분산 DB 친화 스키마 + Write-Behind 파이프라인 (완료)
> *"실시간 메인 스레드에서 DB INSERT 때리면 틱이 밀린다."*

- [x] **PK 전략 변경**: `match_record.id` `BIGINT AUTO_INCREMENT` → `BINARY(16)` (**UUID v7**) — .NET 9+ `Guid.CreateVersion7()` 활용, time-ordered 라 BTree locality 유지
- [x] [V002 마이그레이션](../Server/Persistence/Scripts/V002__create_match_record.sql) 에 주석으로 핫스팟 회피 사유 명시
- [x] **Write-Behind 파이프라인**:
  - [MatchWriteQueue.cs](../Server/Services/MatchWriteQueue.cs) — `Channel<MatchRecord>` (Bounded 65536, `DropOldest` 폭주 방지)
  - [MatchFlushJob.cs](../Server/Jobs/MatchFlushJob.cs) — 100건 차면 즉시 flush, 아니면 1초마다 flush, 종료 시 drain
  - Hub `CleanupAsync` 는 **`TryEnqueue` 한 번**만 호출 → 메인 스레드 DB I/O 0회
  - [PlayerRepository.BulkInsertMatchesAsync](../Server/Persistence/PlayerRepository.cs) 는 트랜잭션 + Dapper 배치 INSERT
- [ ] [docs/DB_TUNING.md](DB_TUNING.md) 에 실 측정치 (p50/p99 INSERT latency, AUTO_INCREMENT 대비 TiDB 핫스팟 비교) 정리 — 실 DB 환경 필요
- [x] 어필: *"NewSQL(Spanner/TiDB) 핫스팟을 UUID v7 로 사전 방지하고, 메인 스레드는 Channel 한 줄로 비동기화 (μs)"*

### Phase 12 — 부하 시나리오 고도화 (악성 부하 토글) (완료)
> *"균등 분포는 O(N), 면접관은 O(N²) 가 보고 싶다."*

- [x] [BotClients](../BotClients/Program.cs) 5번째 인자 `{even|herd|cluster}` 시나리오 모드
- [x] **Thundering Herd** (`herd`): 봇 간 `Task.Delay` 제거 — 1,000 봇이 1초 안에 동시 Join, 커넥션/Group.AddAsync 병목 시연
- [x] **AOI Cluster** (`cluster`): 모든 봇이 `(600, 360)` 로 drift + jitter → 밀집, AOI hits 가 O(N²) 로 폭주
- [x] **스모크 측정**: 40 봇 기준 even 모드 AOI hits ~1 vs **cluster 모드 ~19 (20x 밀집 스파이크)**
- [x] Zero-Alloc 토글 ON/OFF 로 GC 평행선 비교 가능
- [x] 어필: *"평상시 부하가 아닌 보스 레이드 / 던전 입장 같은 게임 특유의 극한 상황을 의도적으로 재현"*

### Phase 13 — K8s Graceful Shutdown (Stateful Hub 생명주기) (완료)
> *"HPA 가 파드 죽이면 유저들이 강제로 튕긴다."*

- [x] [ReadinessGate.cs](../Server/Lifecycle/ReadinessGate.cs) — `Volatile.Read/Write` 기반 lock-free 플래그
- [x] [GracefulShutdownService.cs](../Server/Lifecycle/GracefulShutdownService.cs) — `IHostedLifecycleService.StoppingAsync` 구현
  - SIGTERM → `MarkNotReady()` → `/health/ready` 503 → Service 라우팅 제외
  - `MetricsService.ConnectedPlayers == 0` 또는 25초 타임아웃까지 드레인
- [x] **Liveness/Readiness 분리**: `/health/live` 는 드레인 중에도 200, `/health/ready` 만 503
- [x] [k8s/server.yaml](../k8s/server.yaml) — `lifecycle.preStop: sleep 10` + `terminationGracePeriodSeconds: 60` + 두 probe 분리
- [x] 어필: *"Stateless 웹 API 처럼 죽이면 안 되는 Stateful 서버의 종료 규약을 K8s 와 앱 양쪽에서 협조 설계"*

### Phase 14 — 하이브리드 API (Stateful Hub + Stateless Minimal API) (완료)
> *"실제 게임은 동기화 외에 가챠/인벤토리/우편함 API 도 한 서버에 공존."*

- [x] [ProfileEndpoints.cs](../Server/Endpoints/ProfileEndpoints.cs) — ASP.NET Minimal API 로 3개 엔드포인트
  - `GET  /api/profile/{id}` — 프로필 조회 (첫 요청 시 자동 생성)
  - `POST /api/gacha/{id}` — 랜덤 풀에서 뽑기 → 우편함으로 푸시
  - `GET  /api/mail/{id}` — 우편함 조회
- [x] MagicOnion Hub 와 **동일 프로세스** — gRPC(5001) + HTTP(5050) Kestrel 2 리스너 구성 그대로 활용
- [x] 스모크: 40 봇 동시 접속 + `/api/gacha/77` → `/api/mail/77` 응답 정상, 동기화 트래픽과 간섭 없음
- [x] 어필: *"동기화(스트리밍) 와 일반 API(Unary/Minimal) 를 한 프로세스에 같이 띄우되, Kestrel 이 protocol 별 리스너를 분리 관리해 스레드풀 간섭을 방지"*

### Phase 15 — P95 / P99 Latency 계측 + 대시보드 (완료)
> *"GC 최적화의 진짜 목적은 메모리가 아니라 핑이 안 튀는 것."*

- [x] [LatencyHistogram.cs](../Server/Services/LatencyHistogram.cs) — lock-free 로그 버킷 히스토그램 (1ms–60s, 19 버킷, `Interlocked.Increment` 만)
- [x] `PlayerMoveDto` 에 `SentAtMs` (MessagePack Key 3) 추가 — BotClients 가 송신 직전 `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` 기록
- [x] `MovementHub.MoveAsync` 에서 `now - SentAtMs` 를 히스토그램에 Record
- [x] `KpiRollupJob` 이 1초마다 `SnapshotAndReset()` → `KpiSnapshot.LastP{50,95,99}Ms` 노출
- [x] `/api/kpi` 응답에 `p50Ms`, `p95Ms`, `p99Ms`, `avgLatencyMs` 추가
- [x] 대시보드 Chart.js 에 **P99 Latency 시계열** + KPI 카드 (P50/P99) 추가
- [x] 스모크: 로컬 루프백 40 봇 기준 `p50=1ms, p99=2ms` 정상 측정
- [x] 어필: *"GC 최적화의 진짜 KPI 는 Allocated bytes 가 아니라 P99 latency"*

### Phase 16 — AI 운영 보조자 (LLM Ops Assistant) (예정)
> *"알람은 P99 가 튀었다고만 알려준다. **왜** 튀었는지는 온콜이 로그를 뒤져야 한다."*

게임 서버의 **온콜/운영 페인**을 LLM 으로 축약. 포폴에 이미 존재하는 텔레메트리
(`KpiRollupJob`, `LatencyHistogram`, `MatchRecord`, GC 메트릭) 를 그대로 AI 입력으로 재활용한다.
**새로운 데이터 수집이 아니라 기존 관측 데이터의 해석 레이어**라는 게 핵심.

- [ ] **LLM Provider Registry 추상화** (본업 Personia 프로덕션 패턴 이식)
  - `Server/Services/Llm/ILlmProvider.cs` — `IAsyncEnumerable<string> StreamAsync(systemPrompt, userPrompt, ct)`
  - `Server/Services/Llm/MockProvider.cs` — 결정적 모의 응답. **면접 데모 시 네트워크/과금 실패 대비 안전장치**
  - `Server/Services/Llm/OpenAiProvider.cs` — Chat Completions 스트리밍 (API 키 선택)
  - `appsettings.json` 의 `Llm:Provider` 로 런타임 선택 (`mock` / `openai`), `Llm:ApiKey` / `Llm:Model`
  - 확장 포인트: `ClaudeProvider` / `GeminiProvider` 도 같은 인터페이스로 추가 가능

- [ ] **① P99 스파이크 분석기** — *"14:23 에 왜 튀었나?"*
  - 입력 컨텍스트: 최근 N 분 `KpiSnapshot` (peak/avg/P50/P95/P99), `LatencyHistogram` 버킷 분포, GC Gen0/Allocated 델타, Zero-Alloc 토글 상태, 접속자/TPS 추이
  - `Server/Services/Ops/SpikeAnalyzer.cs` — 텔레메트리 → 구조화된 프롬프트 변환 (분석 대상 시계열을 문자열 차트로 직렬화)
  - `POST /api/ops/analyze/spike?minutes=5` → LLM 스트리밍 응답 (SSE 또는 chunked)

- [ ] **② 어뷰징/치팅 패턴 탐지** — *"이 세션은 사람 맞나?"*
  - 입력: `match_record` 최근 N 건 `(match_id, player_id, room_id, joined_at, left_at, score, score_per_sec)`
  - 탐지 축: 비정상 `score / duration` 비율, 초단시간 재접속 루프, 같은 룸 내 동일 점수 스파이크
  - `Server/Services/Ops/AbuseAnalyzer.cs` — 통계 사전 필터링(상위 N% 이상치) 후 LLM 에 "의심도 + 설명" 질의
  - `POST /api/ops/analyze/abuse?hours=1` → 의심 플레이어 + 근거 리스트

- [ ] **③ Shutdown Drain 요약** — *"정상 드레인 중인가, 스턱인가?"*
  - 입력: `ConnectedPlayers`, `MatchWriteQueue.PendingCount`, 경과 시간, `/health/ready` 상태
  - `GET /api/ops/analyze/drain` → *"15초 경과, 잔여 세션 3, 큐 잔량 12건, 정상 드레인 중 (예상 완료: 5s 내)"*
  - SIGTERM 감지 시 대시보드 상단에 자동 배지

- [ ] **스트리밍 채널** — 응답은 스트리밍 필수 (LLM 토큰 단위 지연)
  - 1차: **Server-Sent Events** (`text/event-stream`) — 구현 단순, JS `EventSource` 로 수신
  - 2차(선택): MagicOnion StreamingHub `IOpsHub.AnalyzeSpikeAsync` + `OnAnalysisChunk(string token)` — 본업의 SignalR TTSHub 경험을 게임 서버 맥락으로 이식

- [ ] **대시보드 통합**
  - P99 Latency 차트 상단에 **"🔍 Ask AI"** 버튼 — 클릭 시 최근 5분 스파이크 분석 스트리밍 출력 패널
  - KPI 카드 영역에 **Drain Summary** 배지 — `/health/ready` 가 503 이 되면 자동 노출
  - 관리자 메뉴에 **Abuse Report** 섹션 — 1시간 주기 수동 실행
  - `Llm:Provider == mock` 일 때 배지 색상으로 시각 구분 (면접 데모 안전)

- [ ] **비용/안전 가드레일**
  - `Llm:MaxTokensPerRequest` / `Llm:MaxRequestsPerMinute` — RateLimit + 예산 방어
  - **PII/시크릿 필터** — 프롬프트 진입 전 플레이어 ID 만 허용, 커넥션 문자열/토큰/이메일 차단
  - `appsettings.Development.json` 기본 `Provider=mock` — 실수로 과금 발생 방지
  - CI 테스트는 `MockProvider` 만 사용 (네트워크 격리)

- [ ] **테스트**
  - `MockProvider` 결정성 — 같은 입력 → 같은 출력 (스냅샷 기반)
  - `SpikeAnalyzer` 프롬프트 직렬화 — 텔레메트리 → 문자열 변환 로직 단위 테스트
  - `/api/ops/analyze/*` E2E — `WebApplicationFactory` + Mock Provider 로 네트워크 없이 전 경로 검증

- [ ] **어필**:
  *"게임 서버 운영의 **'알람 → 대시보드 → 로그 뒤짐'** 3단계를 AI 스트리밍 한 번으로 압축.
  LLM Provider 추상화로 OpenAI 장애 시 로컬 모델/Mock 으로 failover 가능한 프로덕션 구조.
  본업(Personia) 에서 운영 중인 멀티 프로바이더 라우팅 패턴을 게임 서버 관측성에 이식."*

### 4-D 우선순위 (외부 리뷰 반영, 4-B 우선순위 재배치)

| 추천 순위 | Phase | 면접관 시선의 가치 |
|---|---|---|
| **1** | **Phase 10 (Redis Backplane Scale-Out)** | 단일 노드 최적화 → **분산 아키텍처** 능력으로 격상. 가장 강력한 차별화. |
| **2** | **Phase 12 (악성 부하 시나리오)** | Phase 10 효과를 시각적으로 입증. "그래서 어떻게 버틴다고?" 의 답. |
| **3** | **Phase 16 (AI 운영 보조자)** | 타 포폴에 거의 없는 **독자 차별화**. 관측성 + LLM + 프로바이더 추상화가 한 화면에. |
| **4** | **Phase 11 (UUID PK + Write-Behind)** | NewSQL 친화 설계 — Spanner/TiDB 도입 회사가 보고 싶어하는 정확한 그림. |
| **5** | **Phase 15 (P99 Latency)** | Zero-Alloc 토글의 ROI 를 *유저 체감 지표*로 환산해 보여주는 마무리. |
| **6** | **Phase 13 (Graceful Shutdown)** | Stateful 서버 + K8s 운영 이해도 시그널. |
| **7** | **Phase 14 (하이브리드 API)** | 실 라이브 서비스 구조 이해도 시그널. |
| 8 | Phase 8 (테스트) — 기존 4-B 1순위 | **이미 구현 완료**. 위 페이즈들의 안정망으로 후증명 역할. |

> *"기존의 탄탄한 뼈대 위에 위 6가지 양념을 가미하면, 기술 과제를 넘어 '에이밍 백엔드 리드로 당장 투입 가능한 인재'로 평가받을 수 있다."* — 외부 리뷰

---

## 5. 어필 포인트 (면접 스크립트용)

1. **Unified C#** — `Shared` 프로젝트 하나로 서버·봇이 동일 계약 공유.
2. **MagicOnion StreamingHub** — gRPC 위의 양방향 RPC, 브로드캐스트 그룹.
3. **Zero-Allocation 토글** — 같은 부하에서 GC 그래프가 평행선으로 떨어지는 시연.
4. **Headless 부하 발생기** — 단일 프로세스에서 수천 세션, Task 기반 스케줄링.
5. **관측 가능성** — `/api/metrics` + 브라우저 레이더로 현상을 눈으로 증명.

---

## 6. 결정/보류 사항

- 스냅샷 전송 방식: 폴링 vs SSE vs WebSocket → **Phase 1 착수 시 결정**
  - 기본은 폴링(구현 단순), 체감 프레임이 낮으면 SSE 로 승격.
- 좌표 타입: `float` 유지 (네트워크 페이로드 최소화). `double` 필요성 없음.
- 방(Group) 설계: 현재 단일 `"world"` 고정. 멀티룸은 Phase 4 에서.
