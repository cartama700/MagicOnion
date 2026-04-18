# 포트폴리오 테마 이식 가이드
# Aiming PoC (C# / .NET 10 / MagicOnion) → Colorful Palette 저격 Go 버전

> **목적**
> 현재 포트폴리오([Aiming PoC Sync Server](../README.md)) 의 **설계 주제 · 5축 프레임 · 측정 방법**을
> 그대로 유지한 채, 언어만 **Go** 로 바꿔 **株式会社Colorful Palette
> ([プロジェクトセカイ](https://pjsekai.sega.jp) 개발사) サーバサイドエンジニア
> 공고 [BA-09-04a](https://hrmos.co/pages/colorfulpalette/jobs/BA-09-04a)** 에 저격하기 위한 **전환 지침서**.
>
> 이 문서는 _무엇을 옮기고, 무엇을 바꾸고, 무엇을 더해야 하는지_ 를 한 번에 판단하기 위한 레퍼런스다.
> 새 Go 레포 초기화 시 이 문서를 그대로 복사해 출발점으로 삼아라.

---

## 0. TL;DR — 3줄 요약

1. **주제는 그대로**: "수만 개 패킷이 쏟아질 때 서버가 어떻게 버티고 메모리를 어떻게 관리하는가" +
   "AI 가 알람을 자연어로 해석한다" 를 **5축 프레임**으로 증명하는 구조 그대로 이식.
2. **언어는 Go**: C#/.NET 의 관용구를 Go 의 `goroutine · chan · sync · context · interface` 로
   **1:1 대응**. MagicOnion StreamingHub → **gRPC bidi streaming (protobuf)**.
3. **저격 포인트 추가**: 공고 명시 스택(**Aurora MySQL · Spanner · GKE · Terraform · Locust**)
   을 현 포폴의 핵심 축에 얹어서 **1:1 매칭 시그널**을 더 크게 낸다.

---

## 1. 현 포트폴리오의 정체성 (무엇을 어떻게 증명하는가)

**한 줄 정의.**
Unity 없이 100% C# 코드로 **"실시간 게임 서버의 5대 난제"** 를 한 레포에서 증명하는
**부하봇 + 관제 대시보드 + LLM 운영 보조자** 일체형 PoC.

**증명 방식의 핵심.** "구현했다"가 아니라 **"즉석에서 체감할 수 있는 시연"** 을 화면 위에 놓는다.

| 축 | 증명 수단 (시연 가능) |
|---|---|
| ① 핫패스 최적화 | 대시보드 **Zero-Alloc 토글** → Alloc Rate · P99 그래프가 평행선으로 꺾이는 순간 |
| ② 수평 확장 | docker compose `--profile scale` 로 서버 2대 + Redis Backplane + 봇이 양 노드 동기화 |
| ③ 운영/관측성 | Canvas 레이더 + Chart.js 60s 슬라이딩 (TPS/Alloc/Gen0/P99) + KPI 롤업 카드 |
| ④ Stateful 생명주기 | SIGTERM → `/health/ready 503` → ConnectedPlayers=0 드레인 (최대 25s) |
| ⑤ AI Ops | 대시보드 **Analyze Spike** 버튼 → SSE 로 자연어 진단 스트리밍 (Mock/OpenAI 교체 가능) |

**증명 자산.**
- 코드 1800 줄 내외의 **의도적으로 간결한** 프로덕션 패턴
- BenchmarkDotNet 마이크로벤치 (PlayerCount 100/1000/5000)
- xUnit 35 tests (WebApplicationFactory E2E 포함)
- K8s 매니페스트 6종 (`namespace/redis/mysql/server/hpa/bots-job`)
- docker-compose 3-profile (`default/load/scale`)

이 전부를 **Go 로 대칭 이식**하면 "같은 주제를 언어 두 개로 풀 수 있다 =
언어 선택과 설계 결정을 분리해서 사고한다" 는 시그널이 추가로 붙는다.

---

## 2. 5축 프레임의 언어 중립적 본질

**언어가 바뀌어도 살아남는 패턴**만 발라내 놓은 목록. Go 이식은 이것들을 Go idiom 으로 재표현하는 작업이다.

| 패턴 | 정의 | 현 구현 (C#) | Go idiom |
|---|---|---|---|
| **전략 토글** | 런타임에 구현을 스위칭하는 엔드포인트 + UI | `POST /api/optimize?on=true` + `OptimizationMode` | `POST /api/optimize` + `atomic.Bool` + `interface{}` |
| **Provider Registry** | 추상 인터페이스 뒤에 Mock/Prod 구현 교체 | `ILlmProvider`, `ILeaderboard`, `IPlayerRepository` | Go interface + switch on config string |
| **Graceful Degrade** | 외부 의존성 미설정 시 in-memory fallback (개발 진입장벽 0) | ConnectionString 빈 값 → Null/InMemory 등록 | env var 빈 값 → nop/inmem 구현 등록 |
| **Write-Behind** | 핫패스에서 DB I/O 제거, 백그라운드에서 배치 flush | `Channel<MatchRecord>` Bounded + `MatchFlushJob` | buffered `chan` + `flush loop goroutine` |
| **Lock-Free 관측성** | `Interlocked` 만으로 카운터/히스토그램 누적 | `LatencyHistogram` 19-bucket lock-free | `sync/atomic` + prometheus `Histogram` |
| **Structured Prompt** | 텔레메트리 → 프롬프트 직렬화 분리 (테스트 용이성) | `SpikeAnalyzer.BuildPrompts` 메서드 | 순수 함수 `BuildPrompt(ctx, kpi) (sys, usr string)` |
| **SSE 스트리밍** | `text/event-stream` + `EventSource` 한 줄 수신 | `app.MapGet` + `resp.WriteAsync` | `net/http` + `Flusher.Flush()` |
| **시나리오 분리** | even/herd/cluster 세 모드로 부하 특성 변경 | `BotClients` 5번째 CLI 인자 | `cmd/bots` 의 CLI flag |
| **Protocol 분리** | Stateful Hub + Stateless API 를 한 프로세스 / 포트 분리 | Kestrel HTTP1(5050) + HTTP2(5001) | `net/http` + 별도 gRPC 리스너 (`grpc.NewServer`) |
| **Readiness/Liveness 분리** | 드레인 중 ready=503 / live=200 유지 | `/health/live` · `/health/ready` + `ReadinessGate` | 동일 엔드포인트 + `atomic.Bool` gate |

**이 10가지가 살아있으면 포폴이 "그 포폴"이다.** 언어·프레임워크는 자유.

---

## 3. Colorful Palette 공고 요약

> **出典**: [hrmos.co/pages/colorfulpalette/jobs/BA-09-04a](https://hrmos.co/pages/colorfulpalette/jobs/BA-09-04a)

### 3.1 포지션
- **타이틀**: サーバサイドエンジニア (契約社員 · 試用期間3ヶ月 · 正社員登用制度あり)
- **근무**: 東京都渋谷区宇田川町 Abema Towers / 10:00-19:00
- **언어 요건**: 日本語ネイティブ or JLPT N1

### 3.2 팀/프로덕트 톤
- **ファンファースト** 으로 최고의 게임 체험 제공
- 엔지니어가 "**企画や運用にも深く携わる**" 역할 — 단순 구현이 아니라 운영·기획까지
- 신규 개발 + 운영 병행 (주력은 Project Sekai 계열)

### 3.3 주요 업무 (원문)
- スマートフォンゲームの設計／開発／テスト／運用
- 開発環境の構築（サーバ・DB構築、モックアップ作成、プログラミング、単体テスト、バージョン管理）
- **運用負荷、コスト削減等を目的としたアーキテクチャ、及びプログラムの最適化** ← 현 포폴의 ①③ 축과 정확히 일치
- WEB技術のスキルアップ、ノウハウ共有
- お客様からのお問い合わせについての調査対応

### 3.4 필수 요건
- **1-2年以上のサーバサイドのプログラム実務経験**
  언어: **Java / Golang** / PHP / Ruby / Python
  인프라: **AWS / Google Cloud** 등 퍼블릭 클라우드
- 構築~プログラミング~テストまで 전 공정 가능
- 앱~인프라 전 영역 대응 가능
- 日本語ネイティブ or N1

### 3.5 우대 요건 (저격 포인트)
| 우대 항목 | 현 포폴 매칭도 |
|---|---|
| パブリッククラウド 게임 개발·운영 경험 | 🟡 (K8s 매니페스트 있음, 실 배포 시연 대기) |
| **高負荷サービス** 개발·운영·릴리스 경험 | 🟢 Phase 12 부하 시나리오 + Phase 10 scale-out |
| スマートフォン向け Web 사이트 개발 | ⚪ (이 포폴 범위 외) |
| **서버 측 통신/비동기 설계·구현** | 🟢 MagicOnion StreamingHub + Write-Behind Channel |
| **MySQL** DB 설계·구축·운영 | 🟢 Phase 6 (Dapper + DbUp + V00X SQL) |
| **Docker / Kubernetes** | 🟢 Dockerfile multi-stage + k8s/ 6종 |
| **Locust / Gatling / JMeter** 부하 테스트 | 🟡 BotClients 가 역할 수행 (명시적 매핑 필요) |
| **Ansible / Terraform** IaC | 🔴 (현재 없음 — Go 포폴에서 **추가 권장**) |
| Spring Framework 등 컨테이너 | ⚪ (.NET DI 로 대체 가능) |

### 3.6 명시된 기술 스택
- **인프라**: AWS, **Google Cloud**
- **컨테이너**: Docker, **Fargate**, **GKE**
- **개발언어**: Java, **Golang**
- **데이터스토어**: **Aurora MySQL**, **Spanner**, **Redis**
- **소스관리**: GitHub
- **기타**: Slack, Wrike

### 3.7 이 공고에 꽂히는 포폴 키워드
`高負荷` · `最適化` · `アーキテクチャ設計` · `非同期通信` · `MySQL設計` · `K8s運用` ·
`負荷テスト` · `コスト削減` · `Spanner` · `ファンメイク`

---

## 4. Go 이식 결정 표 (패키지·패턴 매핑)

### 4.1 런타임/프레임워크
| C# .NET 10 | Go 대응 | 근거 |
|---|---|---|
| `WebApplication.CreateBuilder` | `net/http` 기본 + `chi` 라우터 | 표준 라이브러리 우선, chi 는 미들웨어 위한 최소 증분 |
| Kestrel HTTP1+HTTP2 리스너 | `http.Server` + 별도 `grpc.Server` | gRPC 는 자체 서버 객체 사용, HTTP는 chi 로 별도 포트 |
| **MagicOnion StreamingHub** | **gRPC bidi streaming (protobuf)** | `google.golang.org/grpc` — MagicOnion 유사물 없음. `.proto` 파일에서 양방향 stream RPC 선언 |
| MessagePack | protobuf | gRPC 쓰면 자연 채택. 별도 MessagePack 원하면 `vmihailenco/msgpack/v5` |
| ASP.NET Minimal API | `chi` 또는 `echo` | Minimal API 의 fluent 매핑과 유사한 간결성 |
| `IHostedService` / BackgroundService | goroutine + `errgroup.Group` + `context.Context` | 생명주기 전파는 ctx, 오류 수집은 errgroup |
| `IHostedLifecycleService.StoppingAsync` | `signal.NotifyContext(ctx, os.Interrupt, syscall.SIGTERM)` + `http.Server.Shutdown` | OS 시그널 → ctx 취소 → http 서버 graceful shutdown |
| `ILogger<T>` | `log/slog` (Go 1.21+) | 표준 구조화 로깅. zerolog 도 OK 하지만 표준 선호 |
| `appsettings.json` + `IConfiguration` | `kelseyhightower/envconfig` 또는 `spf13/viper` | 환경변수 우선. 소규모면 envconfig 가 간결 |

### 4.2 동시성·데이터 구조
| C# | Go | 비고 |
|---|---|---|
| `ArrayPool<T>.Shared` | `sync.Pool` | `Get()` 후 반드시 `Put()`. 초기화 책임은 호출자 |
| `Channel<T>` Bounded + DropOldest | buffered `chan T` + `select default` | 언어 내장. `len(ch) == cap(ch)` 시 뒤로 밀린 것 drop 로직 직접 작성 |
| `ConcurrentDictionary<K,V>` | `sync.Map` (lookup 중심) 또는 `sync.RWMutex + map` (range 중심) | Hub 의 world dict 처럼 range 가 잦으면 RWMutex 가 빠름 |
| `Interlocked.Increment/Exchange` | `sync/atomic` (`AddInt64` / `StoreInt64`) | 같은 시맨틱 |
| `Volatile.Read/Write` | `atomic.LoadInt64` / `StoreInt64` | 동일 |
| `IAsyncEnumerable<string>` (LLM 스트림) | `<-chan string` + goroutine 또는 Go 1.23 `iter.Seq[string]` | 기존 코드 대부분은 chan 으로 이식하는 게 자연스러움 |
| `CancellationToken` | `context.Context` | 취소/타임아웃 1:1 |
| `Guid.CreateVersion7()` | `github.com/google/uuid` (v7 지원) | 동일 time-ordered 보장 |

### 4.3 외부 통합
| C# | Go | 비고 |
|---|---|---|
| Dapper + MySqlConnector | `jmoiron/sqlx` + `go-sql-driver/mysql` | Aurora MySQL 호환. 쿼리 1급 시민 유지 |
| DbUp (V00X SQL) | `pressly/goose` (`*.sql` 순서 적용) | 거의 동일 철학. `goose create -s <name> sql` |
| StackExchange.Redis | `redis/go-redis/v9` | SortedSet/Pub-Sub 모두 지원 |
| MagicOnion.Server.Redis Backplane | **Redis Pub/Sub 자체 구현** | 게임 룸 단위 채널로 broadcast 재전파. 유사 OSS 로 `centrifugal/centrifuge` 대안 |
| `IHttpClientFactory` | `net/http` + `*http.Client` 재사용 + `http.Transport` 튜닝 | HTTP/2, connection pool, timeouts 명시 |
| OpenAI Chat Completions stream | `sashabaranov/go-openai` 또는 수동 SSE 파싱 | 현 포폴의 OpenAiLlmProvider 로직 그대로 Go 로 |
| Kestrel SSE (`text/event-stream`) | `http.Flusher` 캐스팅 → `fmt.Fprintf + f.Flush()` | nginx 앞단: `X-Accel-Buffering: no` 동일 |

### 4.4 관측·테스트·벤치
| C# | Go | 비고 |
|---|---|---|
| BenchmarkDotNet | `go test -bench -benchmem` + `benchstat` | 마이크로벤치 표준. Allocated bytes/op 지표 동일 |
| xUnit + FluentAssertions | `testing` + `stretchr/testify/require` | 표준 + 최소 증분 |
| `WebApplicationFactory<Program>` | `httptest.NewServer(handler)` | In-memory E2E 동일 |
| Chart.js 대시보드 | 그대로 재사용 | 정적 파일이라 서버 언어 무관 |
| **자작 lock-free 히스토그램** | `prometheus/client_golang` `Histogram` + `/metrics` | 업계 표준으로 격상. 기존 핵심 패턴은 유지하되 export 형식을 Prometheus 로 |
| `dotnet-counters` | `net/http/pprof` + `runtime/metrics` + `/debug/pprof/*` | Go 고유 강점. CPU/alloc 프로파일링을 엔드포인트로 노출 |

### 4.5 인프라
| C# | Go | 비고 |
|---|---|---|
| Dockerfile multi-stage | Dockerfile multi-stage (distroless 권장) | `gcr.io/distroless/static-debian12` — Go static binary |
| docker-compose `--profile scale` | 동일 | 변경 없음 |
| K8s 매니페스트 (`.yaml`) | 동일 | 변경 없음 |
| ❌ IaC 없음 | **Terraform 신규 추가** | 공고 우대 키워드. GKE 클러스터 + Artifact Registry + VPC + Workload Identity |

---

## 5. 5축별 Go 재설계 전략

### ① 핫패스 최적화 — **유지 + Go 특화 각도 추가**
**보존**: AOI 필터 Naive vs Optimized 토글 + 벤치마크 테이블.
**재표현**:
- `ArrayPool<int>` → `sync.Pool{New: func() any { return make([]int32, 0, 256) }}`
- LINQ `.Where().Select().ToList()` → **range + append** (Go 에선 naive 가 이미 할당 적음)
- **Go 특화 어필**: **escape analysis** 로 stack 할당 유도 → `go build -gcflags="-m"` 결과 스크린샷

**차별점**: Go 는 C# LINQ 만큼 극적인 alloc 격차가 안 나올 수 있음. 대신 `sync.Pool`
미사용 시의 **GC pause p99** 변화가 벤치 포인트. `GODEBUG=gctrace=1` 로그 수집해서
대시보드 차트에 반영하면 생생함.

**측정**: `go test -bench=AOI -benchmem -count=10 ./internal/aoi/ | benchstat`

### ② 수평 확장 — **유지 + Spanner 축 추가 (공고 저격)**
**보존**: 서버 2대 + Redis Pub/Sub Backplane + UUID v7 + Write-Behind.
**재표현**:
- `UseRedisGroup()` 한 줄 → **자작 `RedisBroker`** (게임 룸 채널 구독/발행)
- `Channel<MatchRecord>` 65536 bounded → `chan MatchRecord` cap=65536 + drop-on-full
- UUID v7 → `github.com/google/uuid.NewV7()`

**공고 저격 추가**:
- **Aurora MySQL** + **Spanner 듀얼 레포지토리 구현**. 환경변수로 전환.
  `sqlx` 용 `MatchRepository` + `spanner-go` 용 `SpannerMatchRepository`.
- 두 구현의 쓰기 레이턴시 비교 표를 README 에 박기 — "같은 Write-Behind 파이프라인이
  MySQL 과 Spanner 양쪽에서 동등하게 동작" 시그널.

### ③ 운영/관측성 — **Prometheus 네이티브로 격상**
**보존**: Canvas 레이더 + Chart.js 60s 슬라이딩 + KPI 롤업 카드.
**재표현**:
- 자작 `LatencyHistogram` → **`prometheus.Histogram` + `/metrics` 엔드포인트**
- `KpiRollupJob` 1s → `time.NewTicker(time.Second)` + goroutine
- `/api/kpi` 는 유지하되 **Prometheus scrape + Grafana 스크린샷** 을 README 에 추가

**Go 특화 추가**:
- **`/debug/pprof/*`** 엔드포인트 기본 노출 (CPU/heap/goroutine/block)
- 대시보드 상단에 **"Open pprof"** 버튼 — 새 탭으로 profile 뷰 오픈

### ④ Stateful 생명주기 — **ctx 전파 문화 + errgroup 강조**
**보존**: K8s `preStop: sleep 10` + `terminationGracePeriodSeconds: 60` + Readiness/Liveness 분리.
**재표현**:
```go
ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
defer cancel()

g, gctx := errgroup.WithContext(ctx)
g.Go(func() error { return httpSrv.ListenAndServe() })
g.Go(func() error { return grpcSrv.Serve(lis) })

<-gctx.Done()                    // ← SIGTERM 또는 첫 에러
ready.Store(false)               // /health/ready → 503
drainCtx, drainCancel := context.WithTimeout(context.Background(), 25*time.Second)
defer drainCancel()
waitDrain(drainCtx, metrics)     // ConnectedPlayers==0 까지
_ = httpSrv.Shutdown(drainCtx)
grpcSrv.GracefulStop()
```
**차별점**: Go 의 **ctx 전파 문화**가 이 축에 가장 잘 드러난다. 모든 goroutine 이
`gctx` 를 받아 취소 신호를 공유하는 걸 **코드로 증명** 가능.

### ⑤ AI Ops — **거의 변화 없음 (Go 가 자연스러움)**
**보존**: `ILlmProvider` 인터페이스 + Mock/OpenAI 구현 + SpikeAnalyzer + SSE.
**재표현**:
- `ILlmProvider` → Go `interface { StreamAnalyze(ctx, sys, usr string) (<-chan string, error) }`
- `MockLlmProvider` → 동일 로직, `time.Sleep(25 * time.Millisecond)` 로 토큰 지연 재현
- SSE → `http.ResponseWriter` 의 `Flusher` 인터페이스 캐스팅

```go
func spikeHandler(w http.ResponseWriter, r *http.Request) {
    w.Header().Set("Content-Type", "text/event-stream")
    w.Header().Set("Cache-Control", "no-cache")
    w.Header().Set("X-Accel-Buffering", "no")
    flusher := w.(http.Flusher)
    ch, _ := analyzer.Analyze(r.Context(), 5)
    for tok := range ch {
        safe := strings.ReplaceAll(tok, "\n", "\\n")
        fmt.Fprintf(w, "data: %s\n\n", safe)
        flusher.Flush()
    }
    fmt.Fprint(w, "event: done\ndata: end\n\n")
    flusher.Flush()
}
```

---

## 6. 리포지토리 구조 제안

현 포폴의 `Shared/Server/BotClients/Benchmarks/Tests/k8s/docs` 를 Go 관용 레이아웃으로 재배치:

```
project-sekai-poc/                  # 리포 이름 (또는 colorful-palette-poc)
├── cmd/
│   ├── server/main.go              # Server/Program.cs
│   └── bots/main.go                # BotClients/Program.cs
├── api/proto/                      # Shared/IMovementHub.cs + PlayerMoveDto
│   ├── movement.proto              # service Movement { rpc Move (stream ...) }
│   └── ops.proto                   # (선택) gRPC 로 Ops 스트리밍도 가능
├── internal/                       # Go 관용: 외부 노출 불가
│   ├── hub/
│   │   └── movement.go             # Hubs/MovementHub.cs — bidi stream handler
│   ├── service/
│   │   ├── metrics/metrics.go      # MetricsService.cs
│   │   ├── snapshot/snapshot.go    # SnapshotService.cs
│   │   ├── aoi/
│   │   │   ├── naive.go            # AoiFilter.Naive
│   │   │   └── pooled.go           # AoiFilter.Optimized (sync.Pool)
│   │   ├── latency/histogram.go    # LatencyHistogram.cs
│   │   ├── kpi/snapshot.go         # KpiSnapshot.cs
│   │   ├── matchqueue/queue.go     # MatchWriteQueue.cs (chan)
│   │   ├── leaderboard/
│   │   │   ├── redis.go            # RedisLeaderboard
│   │   │   └── inmem.go            # InMemoryLeaderboard
│   │   ├── llm/
│   │   │   ├── provider.go         # ILlmProvider
│   │   │   ├── mock.go             # MockLlmProvider
│   │   │   └── openai.go           # OpenAiLlmProvider
│   │   └── ops/
│   │       └── spike.go            # SpikeAnalyzer
│   ├── job/
│   │   ├── kpirollup/              # KpiRollupJob — ticker goroutine
│   │   ├── rankingsnapshot/        # RankingSnapshotJob
│   │   └── matchflush/             # MatchFlushJob
│   ├── lifecycle/
│   │   ├── readiness.go            # ReadinessGate (atomic.Bool)
│   │   └── drain.go                # GracefulShutdownService 로직
│   ├── persistence/
│   │   ├── mysql/                  # PlayerRepository + sqlx
│   │   ├── spanner/                # (추가) SpannerMatchRepository
│   │   └── migrations/             # goose *.sql (V001~)
│   ├── endpoint/                   # HTTP handlers
│   │   ├── metrics.go              # /api/metrics
│   │   ├── kpi.go                  # /api/kpi
│   │   ├── optimize.go             # /api/optimize (토글)
│   │   ├── profile.go              # ProfileEndpoints.cs (가챠/우편)
│   │   ├── ops.go                  # /api/ops/* (SSE)
│   │   └── health.go               # /health/{live,ready}
│   └── broker/
│       └── redis.go                # MagicOnion.Server.Redis 대체
├── bench/
│   ├── aoi_bench_test.go           # Benchmarks/AoiBenchmarks.cs
│   └── README.md
├── test/                           # 통합 테스트 (in-memory server)
│   └── api_test.go                 # Tests/Server.Tests/ApiEndpointTests.cs
├── web/                            # Server/wwwroot
│   ├── index.html
│   └── dashboard.js
├── deploy/
│   ├── docker/
│   │   ├── server.Dockerfile
│   │   └── bots.Dockerfile
│   ├── compose/docker-compose.yml  # --profile load / --profile scale
│   ├── k8s/                        # namespace/redis/mysql/server/hpa/bots-job
│   └── terraform/                  # ★ 신규 — GKE + Artifact Registry + VPC
│       ├── main.tf
│       ├── gke.tf
│       └── variables.tf
├── docs/
│   ├── README.md (KO/JP bilingual 권장)
│   ├── BENCHMARK.md
│   ├── DEPLOY_GKE.md
│   ├── DB_TUNING.md                # ★ 신규 — Aurora MySQL EXPLAIN 전/후
│   ├── SPANNER.md                  # ★ 신규 — Spanner 핫스팟 회피 설계
│   └── JOB_COLORFUL_PALETTE.md     # 공고 ↔ PoC 매핑
├── Makefile                        # build / test / bench / docker / up / down
├── go.mod
└── .github/workflows/ci.yml        # build / test / bench regression / docker
```

---

## 7. 보존 vs 재발명 — Go 에서 어느 축이 더/덜 빛나는가

### 🟢 Go 에서 **더 빛나는** 축
- **④ Stateful 생명주기**: ctx 전파 + errgroup + signal.NotifyContext 조합이
  C# 의 `IHostedLifecycleService` 보다 **코드 밀도로** 더 강력해 보임.
- **③ 운영/관측성**: Prometheus + pprof 네이티브 통합으로 **업계 표준 툴체인** 시그널.
- **② Write-Behind**: `chan` 이 언어 내장이라 "왜 이렇게 해야 하나"의 보일러플레이트가 사라짐.
- **🆕 IaC (Terraform)**: 공고 우대 키워드 + Go 프로젝트와 문화적 친화.

### 🟡 **동등한** 축
- **⑤ AI Ops**: 인터페이스/채널/SSE 모두 대칭. 차별화는 안 됨.
- **② 수평 확장 (Redis Backplane)**: MagicOnion 만큼 한 줄은 아니지만 `redis/go-redis` 로 간결.

### 🔴 Go 에서 **덜 극적인** 축
- **① 핫패스 Zero-Alloc 토글**: C# LINQ 의 박싱 지옥 같은 극적 차이가 Go 에선 덜함.
  **대안 프레이밍**:
  - sync.Pool 미사용 시 GC pause p99 변화
  - escape analysis 로 stack 할당 유도 (`-gcflags="-m"`)
  - `runtime/metrics` 의 `/gc/pauses:seconds` 히스토그램 변화
  → "C# 에선 alloc 의 극적 격차, Go 에선 **GC pause 의 극적 격차**" 로 포장.

### 🆕 Go 만이 가능한 축 (공고 저격 추가)
- **부하 테스트 프레임워크 통합**: 자체 `cmd/bots` 외에 **`k6` 스크립트** (`.js`) 또는
  **Gatling Simulation** (Scala, 하지만 공고가 명시) 병렬 제공.
  README 에 "바이너리 부하봇과 외부 부하툴 두 방식 모두 제공" 시그널.
- **Spanner 듀얼 스토어**: Aurora MySQL + Spanner 동시 타겟. 공고 스택 완전 정렬.

---

## 8. 공고 저격 어필 각도

### 면접 30초 스크립트 (Go 포폴용 초안)

> *"Golang 으로 プロジェクトセカイ 과 같은 리듬게임 서버의 5대 난제를 증명하는
> PoC 를 만들었습니다. **gRPC 양방향 스트리밍**으로 실시간 동기화, **sync.Pool 토글**로
> GC pause 그래프를 평행선으로 떨어뜨리는 시연, **Redis Pub/Sub Backplane**
> 으로 GKE 노드 N대에 걸친 룸 동기화, **Aurora MySQL / Spanner 듀얼 레포지토리**로
> NewSQL 전환을 대비한 Write-Behind 파이프라인, K8s **Graceful drain + ctx 전파**로
> HPA 축소 시 유저 무단절. 대시보드 상단의 **AI Ops Assistant** 는 P99 스파이크를
> 자연어로 해석하고, Terraform 으로 GKE 클러스터 + Artifact Registry 를 코드로 관리합니다.
> 全핫패스는 go test -bench 회귀 방어."*

### 공고 bullet → 포폴 시연 매핑

| 공고 bullet | 포폴에서의 답 |
|---|---|
| "運用負荷、コスト削減を目的としたプログラム最適化" | **① Zero-Alloc 토글** — GC pause 평행선 시연 |
| "高負荷サービスの開発・運用" | **② scale-out** + `cmd/bots cluster` 시나리오 |
| "サーバー側のデータ通信/非同期通信 設計・実装" | gRPC bidi stream + Write-Behind Channel |
| "MySQL 설계·구축·운용" | Aurora MySQL (Phase 6 DbUp → goose) + **EXPLAIN 전/후** |
| "Docker、Kubernetes" | K8s 매니페스트 6종 + HPA + preStop + Terraform |
| "Locust, Gatling, JMeter 부하 테스트" | `cmd/bots` 자체 + k6 스크립트 | 
| "Ansible、Terraform IaC" | deploy/terraform/ (GKE 프로비저닝) |
| "퍼블릭 클라우드 게임 개발·운용" | docs/DEPLOY_GKE.md + 실 배포 스크린샷 |

### 프로덕트 톤 매칭 (プロジェクトセカイ 맥락)
- **리듬게임 = 타이밍이 곧 UX**: P99 Latency 축이 가장 중요. ⑤번째 대시보드 차트로 격상.
- **라이브 이벤트 = 스파이크**: "cluster 시나리오" 가 "새 이벤트 시작 1분간 동시 접속 폭주"
  의 축소판. README 에 **"이벤트 개시 폭주 시뮬"** 로 프레이밍.
- **ランキング/イベント**: Redis Sorted Set 랭킹 + 15s 스냅샷 잡은 그대로 유지.
- **ファンメイク**: AI Ops 가 "유저가 튀는 지점을 사람에게 빠르게 전달" 의 은유로 포장 가능.

---

## 9. 이식 로드맵 (제안 Phase 순서)

Go 레포를 0→완성까지 끌고 가는 추천 순서. 각 Phase 끝에 **체감 가능한 시연 자산 1개** 남기는 것이 원칙.

| # | Phase | 목표 | 완료 기준 (시연 자산) |
|---|---|---|---|
| 0 | 뼈대 | `cmd/server`, `chi`, gRPC 더미, `go.mod` | `curl /api/metrics` → JSON |
| 1 | gRPC Hub | `movement.proto` + bidi stream + in-mem snapshot | `cmd/bots` 1개가 좌표 쏘고 로그 찍힘 |
| 2 | AOI + 토글 | `aoi.Naive` / `aoi.Pooled` + `POST /api/optimize` | Bench 결과 표 생성 (README 에 테이블) |
| 3 | 대시보드 | Chart.js 60s 슬라이딩 + Canvas 레이더 포팅 | 브라우저에서 라이브 렌더 |
| 4 | 멀티룸 + Redis Leaderboard | `roomId` + `go-redis` SortedSet | Top-10 패널 렌더 |
| 5 | Docker + compose | Dockerfile distroless + compose default/load | `docker compose --profile load up` |
| 6 | MySQL + goose | Aurora 연결 + `goose up` + `PlayerRepository` | `mysql:8` 컨테이너에서 profile upsert |
| 7 | KPI + P99 히스토그램 (Prometheus) | prometheus Histogram + `/metrics` + P99 차트 | Prometheus scrape 스크린샷 |
| 8 | Tests + Bench CI | `testing` + `testify` + `-bench` in GH Actions | 녹색 뱃지 |
| 9 | K8s + HPA + Terraform | k8s/ + Terraform GKE + HPA | `terraform apply` → 실 클러스터 스크린샷 |
| 10 | Redis Pub/Sub Backplane | 자작 `broker.Redis` + compose scale profile | 서버 2대 룸 동기 시연 |
| 11 | UUID v7 + Write-Behind | `chan MatchRecord` + flush loop + bulk INSERT | 핫패스에 `select default` drop 시연 |
| 12 | 부하 시나리오 + k6 | `cmd/bots -scenario={even,herd,cluster}` + `k6.js` | README GIF (cluster 모드 → P99 스파이크) |
| 13 | Graceful Shutdown | `signal.NotifyContext` + drain loop + preStop | `kubectl delete pod` 후 유저 무단절 영상 |
| 14 | Hybrid API | chi 로 `/api/profile` `/api/gacha` `/api/mail` | E2E 통과 |
| 15 | Spanner 듀얼 | spanner-go + `MatchRepositorySpanner` | README 에 MySQL vs Spanner latency 표 |
| 16 | AI Ops Assistant | `llm.Provider` + `SpikeAnalyzer` + SSE | "Analyze Spike" 버튼 스트리밍 |
| 17 | README + GIF + JP 번역 | bilingual README + demo GIF | 최종 제출 |

### 마일스톤 묶기
- **v0.1 (MVP)**: Phase 0–5 — "Go 로 MagicOnion 같은 걸 만들었다" 수준
- **v0.2 (관측)**: Phase 6–9 — "프로덕션급 관측성 + 인프라 IaC"
- **v0.3 (분산)**: Phase 10–15 — "Scale-out + NewSQL 친화"
- **v1.0 (시연)**: Phase 16–17 — "AI Ops + 제출 가능"

---

## 10. 두 포폴 대칭 비교 (면접 스크립트 원안)

**현 C# 포폴 + Go 포폴 둘 다 제출하는 전제**로, 두 레포를 나란히 보여주는 1분 피치.

> *"같은 주제를 C# .NET 10 과 Go 두 언어로 구현했습니다.
> **주제**는 '실시간 게임 서버의 5대 난제(핫패스 · 수평확장 · 관측성 · 생명주기 · AI Ops)
> 를 부하봇 + 관제 대시보드 + LLM 운영 보조자 일체형으로 증명'. 동일합니다.
>
> **다른 점**은 언어와 생태계입니다.
> C# 쪽은 **MagicOnion StreamingHub + MessagePack + ArrayPool + lock-free 자작 히스토그램**
> 으로 .NET 런타임 생태계를 밀도 있게 다룹니다.
> Go 쪽은 **gRPC bidi stream + sync.Pool + Prometheus + pprof + ctx 전파 + Terraform**
> 로 Go 관용구와 퍼블릭 클라우드 표준 툴체인에 정렬됩니다.
>
> 두 레포의 같은 Phase 숫자는 같은 문제를 다룹니다 — 예를 들어 Phase 2 는 양쪽 모두
> 핫패스 할당 최적화 토글이고, Phase 13 은 양쪽 모두 K8s Graceful drain 입니다.
> 이 대칭이 **'언어 선택이 설계 결정에 어떤 영향을 주는지' 를 제가 분리해서 사고한다**
> 는 증거입니다."*

### 이 대칭 구조의 면접관 가치
- **"생태계 의존도가 낮은 엔지니어"** 시그널 — 프레임워크에 종속되지 않고 패턴을 뽑아낸다.
- **"동일 요구를 두 가지 방법으로 풀 줄 안다"** — 기술 선택의 이유를 설명할 수 있다.
- **"문서 일관성"** — 같은 5축 / 같은 Phase 번호 / 같은 측정 지표로 교차 참조 가능.

---

## 부록 A — "이것만은 반드시 옮겨라" 체크리스트

Go 레포 초기화 시 첫 커밋에 들어가야 하는 **테마 자산 7종**:

- [ ] `README.md` 상단 — 5축 프레임 + GIF 플레이스홀더 + 30초 스크립트
- [ ] `docs/JOB_COLORFUL_PALETTE.md` — 공고 bullet ↔ Phase 매핑 표 (본 문서 §8)
- [ ] `docs/PLAN.md` — Phase 0~17 로드맵 (본 문서 §9)
- [ ] `cmd/bots` CLI 인자: `<botCount> <serverAddrs> <tickMs> [roomCount] [scenario]` — 동일 시그니처 유지
- [ ] `web/index.html` — 현 `Server/wwwroot/index.html` 포팅 (Chart.js / Canvas 그대로)
- [ ] `POST /api/optimize` + 런타임 토글 — 테마의 간판
- [ ] `/api/ops/analyze/spike` SSE + MockLlmProvider — AI Ops 축은 초기부터

## 부록 B — "이것은 버려도 된다"
- **MessagePack 직접 의존**: protobuf 로 충분
- **Dapper 직접 에뮬레이션**: `sqlx` 로 족함
- **DbUp 의 `SchemaVersions` 테이블 이름 호환**: goose 가 자체 `goose_db_version` 사용해도 무방
- **자작 lock-free 히스토그램**: `prometheus.Histogram` 으로 대체하는 게 어필이 더 큼
- **`IHostedLifecycleService` 의 이름**: Go 에선 관용구가 다르므로 동일 네이밍 고집 안 해도 됨

## 부록 C — 파일 크기 가이드
현 C# 포폴이 **약 1800줄** (`Server/` + `Shared/` + `BotClients/`). Go 는 동일 기능 기준
**1500~2000줄** 목표. 더 크면 "간결함" 시그널이 약해진다. gRPC 자동생성 코드는 제외 카운트.

---

_이 문서는 Go 레포의 `docs/PORT_FROM_CSHARP.md` 로 그대로 복사해 사용할 수 있다._
_본문 중 "현 포폴"은 [Aiming_PoC_SyncServer](../README.md) 를 가리킨다._
