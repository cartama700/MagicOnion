# 용어집 (Glossary)

이 프로젝트를 읽거나 면접 자리에서 설명할 때 등장하는 용어들.
**정의 → 이 프로젝트에서의 등장 위치**를 짝지어 정리한다.

---

## 1. 네트워킹 / RPC

### MagicOnion
Cysharp 가 만든 C# 전용 실시간 네트워크 RPC 라이브러리. gRPC 위에 얹혀
**서버·클라가 같은 인터페이스를 공유**하는 "Unified C#" 모델을 지원.
→ [Shared/IMovementHub.cs](../Shared/IMovementHub.cs), [Server/Hubs/MovementHub.cs](../Server/Hubs/MovementHub.cs)

### StreamingHub
MagicOnion 의 양방향 스트리밍 허브. 한 번 연결되면 커넥션을 유지하며 서버·클라가
서로 메서드를 호출할 수 있다. WebSocket + RPC 의 성격.
→ `IMovementHub : IStreamingHub<IMovementHub, IMovementHubReceiver>` 에서 *Hub* = 서버가 구현하는 메서드 인터페이스, *Receiver* = 클라가 구현하는 알림 인터페이스.

### Unary RPC
요청-응답 1회로 끝나는 전통적 gRPC 호출 모델. Stateful 인 StreamingHub 와 달리
Stateless 라 부하 분산이 쉽다. → Phase 14 Hybrid API 에서 Stateless Minimal API 로 대체됨.

### gRPC / HTTP/2 (h2c)
`h2c` = HTTP/2 cleartext (TLS 없는 평문 HTTP/2). 이 프로젝트는 로컬/컨테이너 내부
통신이라 h2c 로 포트 5001 을 연다.

### MessagePack
JSON 과 호환되는 바이너리 직렬화 포맷. JSON 대비 작고 빠르며,
C# 소스에 `[MessagePackObject] [Key(N)]` 애트리뷰트를 달아 쓴다.
→ [Shared/PlayerState.cs](../Shared/PlayerState.cs) — `PlayerMoveDto`

### Group (MagicOnion Group)
StreamingHub 에서 동일한 `Group.AddAsync("roomId")` 로 묶인 클라들의 집합.
`group.All.OnPlayerMoved(...)` 한 줄이면 그룹 내 모든 클라에게 브로드캐스트된다.
이 프로젝트에서는 Group 이 곧 **룸(Room)** 개념.

### Backplane (Pub/Sub 백플레인)
**다중 서버 인스턴스 간에 Group 브로드캐스트를 공유**하기 위한 Redis Pub/Sub 레이어.
`server1` 에 접속한 유저의 Move 가 `server2` 에 접속한 같은 룸 유저에게 전달되려면
백플레인이 필수.
→ Phase 10, `UseRedisGroup(...)` — [Server/Program.cs:30](../Server/Program.cs#L30)

---

## 2. 게임 서버 개념

### AOI (Area of Interest)
"이 플레이어가 지금 **어느 유저를 볼 수 있어야 하는가**" 의 영역.
반경 / 그리드 / 옥트리 등으로 계산. 브로드캐스트 대상을 줄여 O(N²) 를 O(N·k) 로 낮추는 핵심.
→ [Server/Services/AoiFilter.cs](../Server/Services/AoiFilter.cs) — 반경 200px 원형.

### Broadcast
한 유저의 Move 를 Group 의 다른 모든 유저에게 전파하는 행위.
AOI 가 도입되면 "subset broadcast"가 된다.

### Tick
서버/봇의 주기적 업데이트 간격. 이 프로젝트 봇은 기본 100ms (10Hz).
실제 MMO 는 15~60Hz.

### Thundering Herd (천둥 무리)
서버 기동 직후 또는 특정 이벤트 시작 시 클라이언트들이 동시에 몰려오는 현상.
커넥션/인증/DB 가 순간적으로 폭증. → 봇 `herd` 시나리오가 의도적으로 재현.

### Headless Client
GUI 없이 네트워크만 흉내내는 가상 클라이언트. 유닛 박스 N개 = 서버 부하 N배.
→ `BotClients` 프로젝트.

### Room
Group 의 게임 맥락 표현. 이 프로젝트는 문자열 `roomId` ("world", "room-00" 등) 로 구분.

---

## 3. .NET / 성능

### Zero-Allocation
**메서드 호출당 힙 할당이 0 바이트**인 코드 경로. GC 가 개입할 이유가 없어지므로
Gen0 / Gen1 /Gen2 컬렉션이 멈추고, 그 결과 P99 latency 가 평행선이 된다.
→ Phase 2, `AoiFilter.Optimized`.

### ArrayPool<T>
`System.Buffers.ArrayPool<T>.Shared.Rent(n)` / `.Return(arr)` 로 배열을 풀에서 빌려 쓰는 메커니즘.
`new T[n]` 호출이 사라지므로 할당량이 0 이 된다.
→ [Server/Services/AoiFilter.cs](../Server/Services/AoiFilter.cs) 의 Optimized 경로.

### Span<T> / stackalloc
`stackalloc int[256]` 로 스택에 할당한 256칸 버퍼를 `Span<int>` 로 감싸는 패턴.
힙 할당 없이 작은 임시 버퍼가 필요할 때 쓴다. ArrayPool 이 더 안전해서
본 프로젝트는 ArrayPool 을 채택.

### GC (Gen0 / Gen1 / Gen2)
.NET 의 세대 기반 GC. Gen0 = 갓 만든 짧은 객체, Gen2 = 오래 산 객체.
할당이 많으면 Gen0 컬렉션이 폭증한다. `/api/metrics` 의 `gen0/1/2` 값으로 관측.

### Server GC
`<ServerGarbageCollection>true</ServerGarbageCollection>` 또는 `DOTNET_gcServer=1`
환경변수. 코어별 힙을 두는 처리율 우선 GC. 서버 부하 테스트의 기본.

### Interlocked
CPU 원자 명령어를 노출하는 `System.Threading.Interlocked` 클래스.
`Interlocked.Increment`, `CompareExchange` 등. Lock 없이 카운터를 동기화할 때 씀.
→ [Server/Services/MetricsService.cs](../Server/Services/MetricsService.cs), `KpiSnapshot`.

### Channel<T>
`System.Threading.Channels.Channel<T>` — .NET 제공 producer/consumer 큐.
**Bounded 옵션 + DropOldest** 로 폭주 시 오래된 데이터를 흘려버리는 backpressure 처리가 가능.
→ Phase 11 Write-Behind, [Server/Services/MatchWriteQueue.cs](../Server/Services/MatchWriteQueue.cs)

### BackgroundService
ASP.NET Core 가 제공하는 장기 실행 서비스 베이스 클래스. `ExecuteAsync` 하나만 구현하면
호스트가 자동으로 시작/중지를 관리.
→ `KpiRollupJob`, `RankingSnapshotJob`, `MatchFlushJob`.

### IHostedLifecycleService
`IHostedService` 의 확장. **StartingAsync / StartedAsync / StoppingAsync / StoppedAsync** 네 단계의
훅을 제공해 애플리케이션 생명주기 직전·직후 정밀 제어 가능.
→ Phase 13, [GracefulShutdownService.cs](../Server/Lifecycle/GracefulShutdownService.cs).

### Kestrel
ASP.NET Core 의 기본 크로스플랫폼 웹 서버. 이 프로젝트는 Kestrel 이 두 리스너
(HTTP/1.1 포트 5050, HTTP/2 포트 5001) 를 동시에 열도록 설정.

---

## 4. 관측성 (Observability)

### P50 / P95 / P99 Latency
지연 시간의 분위수. 샘플 1,000개 중 **99% 가 X ms 이하**라면 P99 = X ms.
평균(`avg`) 은 튀는 값을 숨기지만 P99 는 튄다. GC 스파이크 직접 관측 지표.
→ [Server/Services/LatencyHistogram.cs](../Server/Services/LatencyHistogram.cs)

### Histogram (버킷 히스토그램)
지연 시간을 몇 개의 범위(bucket) 로 나누어 카운트하는 자료구조. 정확한 정렬 없이도
빠르게 분위수를 추정할 수 있다. 본 프로젝트는 1ms–60s 사이 19개 로그 버킷 사용.

### TPS (Transactions / Packets per Second)
초당 처리량. 이 프로젝트에서는 **서버가 1초 동안 받은 `MoveAsync` 호출 수**.

### KPI Rollup
KPI 지표를 일정 주기(1초)로 집계해 스냅샷을 만드는 배치 루틴.
→ Phase 7, [KpiRollupJob.cs](../Server/Jobs/KpiRollupJob.cs).

### dotnet-counters
`dotnet-counters monitor --process-id <pid>` 로 런타임 카운터를 실시간 관찰하는 CLI 도구.
프로덕션 진단용 보조 수단. 본 프로젝트의 자체 대시보드가 이 역할을 시각화.

---

## 5. DB / Persistence

### Dapper
.NET 용 **마이크로 ORM**. 원시 SQL 쿼리에 파라미터 바인딩/결과 매핑만 얇게 얹어줌.
EF Core 와 달리 쿼리 추상화가 없어 "MySQL 쿼리 최적화" 가 핵심 업무인 팀에 잘 맞음.
→ [Server/Persistence/PlayerRepository.cs](../Server/Persistence/PlayerRepository.cs)

### 마이크로 ORM vs 풀 ORM
- 풀 ORM (EF Core, NHibernate) — LINQ → SQL 자동 변환, 관계 로딩, 변경 추적까지.
- 마이크로 ORM (Dapper, PetaPoco) — SQL 은 직접 작성, 매핑만 도움.

### DbUp
.NET 전용 SQL 마이그레이션 러너. **버전된 `.sql` 파일**을 한 번씩만 실행하고
`SchemaVersions` 테이블에 적용 이력 기록. **다운그레이드 없음**.
자세한 학습 노트: [PLAN.md 4-C 섹션](PLAN.md).

### SchemaVersions
DbUp 이 자동 생성하는 관리 테이블. "어떤 스크립트가 언제 적용됐는지" 의 단일 진실원.

### Flyway 컨벤션
`V001__create_xxx.sql`, `V002__add_index.sql` 처럼 **V + 번호 + 설명** 형식. 알파벳 정렬 순서가 적용 순서가 되도록 zero-pad.

### UUID v7
RFC 9562 (2024). **앞 48비트가 Unix 밀리초 타임스탬프**인 UUID.
→ **time-ordered** 라 BTree 인덱스 locality 유지, 분산 DB 에서도 핫스팟 분산.
.NET 9+ 에서 `Guid.CreateVersion7()` 제공.
→ Phase 11, [V002 SQL](../Server/Persistence/Scripts/V002__create_match_record.sql).

### Snowflake ID
Twitter 가 만든 64-bit ID 체계: `timestamp + machineId + seq`. UUID v7 의 대체재.
분산 시스템에서 정렬 가능한 64비트 ID 가 필요할 때 고려.

### Hotspot (분산 DB 핫스팟)
TiDB / Spanner 같은 NewSQL 에서 **AUTO_INCREMENT PK 는 모든 INSERT 가 같은 Region 에 쏠린다**.
UUID v7 / Snowflake 로 PK 를 분산 랜덤화해야 쓰기 부하가 샤드 전체로 골고루 퍼짐.

### Write-Behind (= Lazy Write)
DB 쓰기를 **즉시 하지 않고** 인메모리 큐에 버퍼링 → 별도 워커가 배치로 flush.
장점: 메인 스레드(게임 틱) 가 DB I/O 지연에 영향받지 않음.
단점: 크래시 시 버퍼 내용 유실 가능.
→ Phase 11, [MatchWriteQueue.cs](../Server/Services/MatchWriteQueue.cs) + [MatchFlushJob.cs](../Server/Jobs/MatchFlushJob.cs).

### Write-Through
반대 개념. 쓰기 즉시 DB 반영 → 일관성 ↑ / 지연 민감.

### Bulk INSERT
여러 행을 한 트랜잭션·한 네트워크 왕복으로 INSERT. `INSERT ... VALUES (...), (...), (...)` 또는
Dapper 의 다중 객체 바인딩. 단건 INSERT 대비 수십 배 빠름.

### N+1 쿼리
리스트 1회 조회 후, 각 원소마다 또 쿼리 → N+1 번. JOIN 또는 IN 절로 1회에 묶는 것이 해법.
DB 튜닝 면접 단골 주제.

### EXPLAIN (MySQL)
쿼리 앞에 `EXPLAIN ` 붙여 실행 계획(인덱스 사용 여부, 조인 방식, 검사 행수) 을 보는 명령.
인덱스 전/후 EXPLAIN 결과를 나란히 보여주면 "튜닝 능력" 증명 가능.

### Sorted Set (ZADD / ZRANGE)
Redis 의 **스코어 기반 정렬 집합**. `ZADD key score member` / `ZRANGE key 0 9 WITHSCORES`.
본 프로젝트의 실시간 리더보드가 이걸 그대로 사용. → [RedisLeaderboard.cs](../Server/Services/RedisLeaderboard.cs).

---

## 6. K8s / 배포

### HPA (Horizontal Pod Autoscaler)
Pod 개수를 CPU / 메모리 / 커스텀 메트릭 기반으로 자동 스케일.
→ [k8s/server-hpa.yaml](../k8s/server-hpa.yaml) — CPU 70% / Mem 80%, 1~10 replicas.

### Readiness Probe / Liveness Probe
- **Liveness** — "프로세스가 살아있는가?" 실패 시 재시작.
- **Readiness** — "트래픽을 받을 준비가 됐는가?" 실패 시 Service 엔드포인트에서만 빠짐(재시작 X).
→ 드레인 시 **Readiness 만 503**, Liveness 는 200 유지가 핵심 패턴.

### preStop Hook
컨테이너 종료 **직전** 실행되는 명령. 보통 `sleep 10` 으로 LB 가 엔드포인트 제거를
인식할 시간을 번다. SIGTERM 이 오기 전에 실행.

### Graceful Shutdown
SIGTERM 후 **기존 세션을 끝까지 처리하고** 정상 종료. 반대는 abrupt termination.
Stateful 서버(StreamingHub) 에 필수.
→ Phase 13, [GracefulShutdownService.cs](../Server/Lifecycle/GracefulShutdownService.cs).

### Drain (드레인)
유저를 새로 받지 않으면서 기존 유저가 자발적으로 빠지길 기다리는 상태.
본 프로젝트는 25초 타임아웃 + 500ms 폴링.

### terminationGracePeriodSeconds
K8s 가 컨테이너에 SIGTERM 주고 SIGKILL 주기까지의 시간 (기본 30초). 드레인 시간을 감안해 늘림.
→ [k8s/server.yaml](../k8s/server.yaml) `60` 설정.

### PVC (PersistentVolumeClaim)
Pod 가 재시작되어도 남는 **영속 볼륨**의 클레임. MySQL 데이터용.
→ [k8s/mysql.yaml](../k8s/mysql.yaml).

### ConfigMap / Secret
K8s 의 비암호 / 암호 설정 저장소. 환경변수로 주입.
→ [k8s/server.yaml](../k8s/server.yaml) `server-config`, [k8s/mysql.yaml](../k8s/mysql.yaml) `mysql-secret`.

### GKE Autopilot
Google Kubernetes Engine 의 완전 관리형 모드. 노드 관리를 Google 이 해주고
사용자는 Pod 만 신경씀. 비용은 약간 더 비싸지만 PoC 용이.

---

## 7. 아키텍처 패턴

### Stateful vs Stateless
- **Stateful** — 서버가 클라별 상태를 메모리에 보유 (StreamingHub 접속 세션, 게임 룸 등).
  스케일아웃 어려움, Graceful Shutdown 필요.
- **Stateless** — 서버는 요청마다 상태를 새로 받음 (Unary RPC, REST API).
  스케일아웃 자유롭고 LB 라운드 로빈 OK.

### Hybrid API
한 프로세스에 Stateful Hub 와 Stateless 엔드포인트가 공존하는 구조.
실 라이브 게임의 전형적 모습.
→ Phase 14, [Endpoints/ProfileEndpoints.cs](../Server/Endpoints/ProfileEndpoints.cs).

### Scale-Out (수평 확장) vs Scale-Up (수직 확장)
- Scale-Out — 인스턴스를 **여러 개** 늘림. 단 Group 동기화가 필요 → Backplane.
- Scale-Up — 한 인스턴스의 CPU/메모리 **사양**을 올림. 단일 서버 한계 존재.

### Graceful Degrade (점진적 성능 저하)
인프라(MySQL/Redis) 가 없으면 **기능을 자동으로 축소**해 돌아가게 하는 설계.
이 프로젝트는 `ConnectionStrings` 가 비면 in-memory 폴백 → 개발 진입 장벽 0.

### Unified C#
Cysharp 진영의 슬로건. **서버·클라·부하기·테스트까지 한 언어/한 타입 시스템**으로 엮는 접근.
이 프로젝트의 `Shared` 프로젝트가 그 구현.

---

## 8. 도구

### BenchmarkDotNet
.NET 마이크로벤치마크 프레임워크. JIT warmup / statistical rigor / 메모리 diagnoser 내장.
→ [Benchmarks/](../Benchmarks/), [BENCHMARK.md](BENCHMARK.md).

### xUnit
.NET 계열에서 가장 많이 쓰이는 테스트 프레임워크. `[Fact]` / `[Theory]` / `[InlineData]`.
→ [Tests/Server.Tests/](../Tests/Server.Tests/).

### FluentAssertions
`actual.Should().Be(expected)` 스타일의 읽기 쉬운 assertion 라이브러리.
실패 메시지가 자연어에 가깝다.

### WebApplicationFactory<T>
`Microsoft.AspNetCore.Mvc.Testing` 제공. **실 Kestrel 없이 TestServer 로** 앱을 띄워 HTTP 테스트.
Program.cs 에 `public partial class Program {}` 를 추가해야 제네릭 인자로 쓸 수 있다.

### Reqnroll
SpecFlow 의 후속 BDD 프레임워크. Gherkin 문법으로 시나리오를 기술.
현재 본 프로젝트는 미도입.

---

## 9. 약어 빠른 참조

| 약어 | 뜻 |
|---|---|
| AOI | Area of Interest |
| DTO | Data Transfer Object |
| DI | Dependency Injection |
| GC | Garbage Collector/Collection |
| HPA | Horizontal Pod Autoscaler |
| h2c | HTTP/2 cleartext |
| KPI | Key Performance Indicator |
| NewSQL | 분산 트랜잭션 지원 RDBMS (TiDB, Spanner 등) |
| N+1 | N+1 쿼리 문제 |
| ORM | Object-Relational Mapping |
| PoC | Proof of Concept |
| PVC | PersistentVolumeClaim |
| RPC | Remote Procedure Call |
| RPS | Requests Per Second |
| TPS | Transactions / Ticks Per Second |
| UUID | Universally Unique Identifier |
