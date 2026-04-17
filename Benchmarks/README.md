# Benchmarks

**BenchmarkDotNet 기반 마이크로벤치 스위트.**

서버 핫패스의 Zero-Allocation 최적화 효과를 숫자로 증명하는 전용 프로젝트.
CI 회귀 감시 용도로도 쓴다 (`.github/workflows/ci.yml`).

## 실행

```bash
# 빠른 측정 (ShortRun: warmup 3, iteration 3 — 약 40초)
dotnet run --project Benchmarks -c Release -- --job short --filter '*'

# 정밀 측정 (기본 프로필: 약 5~10분)
dotnet run --project Benchmarks -c Release -- --filter '*'
```

> ⚠️ 반드시 **Release 빌드** + Release 실행. Debug 로 돌리면 수치가 무의미하다.
> BenchmarkDotNet 가 자체적으로 새 프로세스를 띄우므로 `Debug` 로 실행하면 거부된다.

## 현재 벤치

### [AoiBenchmarks.cs](AoiBenchmarks.cs)

`MovementHub.MoveAsync` 의 AOI 필터 핫패스를 **Naive(LINQ) vs Optimized(ArrayPool)** 로 비교.

| 파라미터 | 값 |
|---|---|
| `PlayerCount` | 100 / 1,000 / 5,000 |
| Method | `Naive_LinqFilter` (baseline) vs `Optimized_PoolBuffer` |
| Attrs | `[MemoryDiagnoser]`, `[GcServer(true)]` |

**결과 (Apple M4 Max, .NET 10 Arm64 Server GC):** 5,000명 기준 **3.8× 속도, 할당량 82KB → 0B**.
자세한 표는 [../docs/BENCHMARK.md](../docs/BENCHMARK.md) 참조.

## 구조

- [Program.cs](Program.cs) — `BenchmarkSwitcher.FromAssembly(...).Run(args)` 한 줄
- [AoiBenchmarks.cs](AoiBenchmarks.cs) — 측정 대상 클래스

BenchmarkDotNet 은 측정 대상을 자체적으로 복제 빌드하므로,
**측정 코드는 의도적으로 `Server` 프로젝트와 분리**되어 있다 (ProjectReference 없음).
서버 쪽의 `AoiFilter` 와 논리적으로 동일한 구현을 벤치 안에 옮겨와 두는 식.

## 의존성

```
BenchmarkDotNet 0.14.0
```

## 언제 이 프로젝트를 손대나

- 새 핫패스 최적화를 계측하고 싶을 때 — 새 `*Benchmarks.cs` 추가
- 기존 알고리즘의 회귀를 감시하고 싶을 때 — 같은 파일에 `[Benchmark]` 케이스 추가
- CI 회귀 임계값 조정 — `.github/workflows/ci.yml` 의 `benchmark-regression` 잡

## 팁

- `[Params]` 값이 늘어날수록 측정 시간이 곱절로 증가한다. 개발 중에는 `--job short` 로.
- `dotnet-counters` 는 프로세스 전체 지표라 여기서 재는 것과 목적이 다르다.
  이 프로젝트는 **단일 메서드의 ns/op 와 alloc/op** 에 집중.
