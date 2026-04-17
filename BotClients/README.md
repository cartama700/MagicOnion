# BotClients

**헤드리스 부하 발생기. Unity 클라이언트 대신 C# Console 에서 수천 개의 가상 유저 세션을 띄운다.**

단일 프로세스에서 `Task` 기반으로 봇 N 개를 병렬 실행하고,
각 봇이 MagicOnion 스트리밍 허브에 연결해서 `JoinAsync` → 주기적 `MoveAsync` → (종료 시) `LeaveAsync` 흐름을 반복한다.

## 실행

```bash
dotnet run --project BotClients -c Release -- <botCount> <serverUrl(s)> <tickMs> [roomCount] [scenario]
```

### 인자

| 위치 | 이름 | 기본 | 설명 |
|---|---|---|---|
| 1 | `botCount` | `1000` | 띄울 봇 수 |
| 2 | `serverUrl(s)` | `http://localhost:5001` | gRPC 서버 URL. **콤마로 구분**하면 다중 서버 라운드 로빈 (Phase 10) |
| 3 | `tickMs` | `100` | 각 봇의 `MoveAsync` 송신 주기 |
| 4 | `roomCount` | `1` | 봇을 N개 룸에 `i % roomCount` 로 분산 |
| 5 | `scenario` | `even` | `even` / `herd` / `cluster` |

### 시나리오

- **`even`** — 랜덤 워킹. 평상시 고른 부하.
- **`herd`** — 접속 스태거 제거, 모든 봇이 1초 안에 동시 Join → 커넥션/`Group.AddAsync` 병목 시연.
- **`cluster`** — 모든 봇이 좌표 `(600, 360)` 로 drift. 반경 200px AOI 안에 밀집 → 브로드캐스트 O(N²) 폭증.
  Zero-Alloc 토글 효과가 가장 극적으로 드러남.

## 예시

```bash
# 1,000 봇, 4 룸, 평상시 부하
dotnet run --project BotClients -c Release -- 1000 http://localhost:5001 100 4

# 보스 레이드처럼 한 점으로 몰리는 부하 — GC / P99 스파이크 유도
dotnet run --project BotClients -c Release -- 1000 http://localhost:5001 100 4 cluster

# Scale-Out: 2대 서버에 라운드 로빈 분산 접속 (Phase 10 Redis Backplane 시연)
dotnet run --project BotClients -c Release -- 1000 http://server:5001,http://server2:5001 100 4 even
```

## 구성

단일 [Program.cs](Program.cs) 파일. 주요 흐름:

1. 인자 파싱 + 서버 URL 리스트 분리
2. `Task` 배열로 `RunBotAsync(id, roomId, url, tickMs, scenario)` 병렬 기동
3. `herd` 가 아니면 100봇마다 10ms `Task.Delay` 로 스태거 (커넥션 러시 완화)
4. 각 봇은 `GrpcChannel.ForAddress(url)` → `StreamingHubClient.ConnectAsync<IMovementHub, IMovementHubReceiver>` 로 허브 접속
5. 송신 시 `PlayerMoveDto.SentAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` 채움 → 서버가 P99 latency 계산

서버→클라 알림은 쓰지 않으므로 `IMovementHubReceiver` 는 `NullReceiver` (빈 구현) 사용.

## 의존성

```
Shared (ProjectReference)
MagicOnion.Client 7.10.0
Grpc.Net.Client 2.76.0
```

## 빌드/배포

- [Dockerfile](Dockerfile) — `dotnet/runtime:10.0` (ASP.NET 불필요)
- K8s Job: 루트의 [../k8s/bots-job.yaml](../k8s/bots-job.yaml) — `args` 로 시나리오 전달

## 언제 이 프로젝트를 손대나

- 새로운 부하 패턴 추가 (예: `boss-raid` — cluster 에서 주기적으로 좌표 점프, `pvp` — 두 그룹 간 이동)
- 봇의 가상 행동 복잡화 (공격/스킬 RPC 가 추가되면 Move 외에도 호출)
- 측정용 클라이언트 사이드 지표 수집 (왕복 지연, 수신 패킷 수 등)
