# Server.Tests

**xUnit 기반 단위 + 통합 테스트 스위트.**

Phase 8 (테스트) 의 산출물. `Server` 프로젝트의 핵심 경로를 방어한다.

## 실행

```bash
# 루트에서
dotnet test Tests/Server.Tests -c Release

# 개별 파일만
dotnet test Tests/Server.Tests -c Release --filter FullyQualifiedName~AoiFilterTests
```

현재 상태: **35 / 35 통과**.

## 테스트 구성

| 파일 | 무엇을 검증 | 스타일 |
|---|---|---|
| [AoiFilterTests.cs](AoiFilterTests.cs) | Naive vs Optimized **결과 동치성** (3종 [Theory] 랜덤 시드), 경계 케이스, 자기 제외, 경계값 inclusive | Property-based |
| [SnapshotServiceTests.cs](SnapshotServiceTests.cs) | `Set` / `Remove` / `RoomList` / `SerializeFlat` 각 경로 | 단위 |
| [LeaderboardTests.cs](LeaderboardTests.cs) | `InMemoryLeaderboard` + `KpiSnapshot` lock-free 누적 검증 | 단위 |
| [LatencyHistogramTests.cs](LatencyHistogramTests.cs) | 빈 상태 / 동일값 / P50<P99 스파이크 / 리셋 | 단위 |
| [MatchWriteQueueTests.cs](MatchWriteQueueTests.cs) | Channel enqueue/read, UUID v7 time-ordered 프리픽스 | 단위 |
| [LifecycleTests.cs](LifecycleTests.cs) | `ReadinessGate` 초기값 + flip | 단위 |
| [ApiEndpointTests.cs](ApiEndpointTests.cs) | **E2E** — `/api/metrics`, `/api/snapshot`, `/api/optimize`, `/api/rooms`, `/health/ready`, `/api/profile`, `/api/gacha` → `/api/mail` | `WebApplicationFactory<Program>` |

## 의존성

```
Microsoft.NET.Test.Sdk 17.11
xunit 2.9 + xunit.runner.visualstudio
FluentAssertions 6.12
Microsoft.AspNetCore.Mvc.Testing 10.0   # WebApplicationFactory<Program>
```

E2E 테스트가 동작하려면 `Server/Program.cs` 맨 아래에

```csharp
public partial class Program { }
```

가 있어야 `WebApplicationFactory` 가 Program 타입을 찾을 수 있다.

## 언제 이 프로젝트를 손대나

- `Server/Services/*` 에 새 로직이 들어갈 때 — 단위 테스트 한 파일 추가
- 새 REST 엔드포인트가 `Program.cs` 에 추가될 때 — `ApiEndpointTests.cs` 에 케이스 추가
- 새 `PlayerMoveDto` 필드나 `IMovementHub` 메서드가 추가될 때 — 동치성 테스트 복기

## 지금 빠진 항목 (의도적 보류)

- **`MagicOnion.Integration.TestKit` 기반 Hub 테스트** — Phase 8 체크리스트에 남아있음.
  Hub 의 Join/Move/Leave 시퀀스를 `IGroup` 목업 없이 테스트하려면 TestKit 이 필요.
  현재는 `AoiFilter` 를 static 으로 분리해 테스트 가능하게 만들어두었고, Hub 자체는 ApiEndpointTests 간접 검증에 의존.
- **Reqnroll (SpecFlow 후속) BDD** — "토글 ON 시 할당이 0" 시나리오. 현재는 BenchmarkDotNet 으로 대체 증명.

## CI 연동

`.github/workflows/ci.yml` 의 `build-and-test` 잡이 PR 마다 실행.
빌드 + 복원 → 이 프로젝트의 테스트 전부 통과 → 후속 `benchmark-regression` / `docker-build` 잡이 돈다.
