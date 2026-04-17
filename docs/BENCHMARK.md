# AOI 필터 벤치마크 — Naive vs Zero-Allocation

`MovementHub.MoveAsync` 의 핫패스인 **AOI(Area of Interest) 필터**를 두 가지 방식으로
구현하고 BenchmarkDotNet 으로 비교한 결과.

- 측정 코드: [Benchmarks/AoiBenchmarks.cs](../Benchmarks/AoiBenchmarks.cs)
- 실행: `dotnet run --project Benchmarks -c Release -- --job short --filter '*'`

## 환경

| 항목 | 값 |
|---|---|
| OS | macOS 26.3.1 (Darwin 25.3.0) |
| CPU | Apple M4 Max — 16 logical / 16 physical cores |
| Runtime | .NET 10.0.3 (Arm64 RyuJIT AdvSIMD) |
| GC | Concurrent Server |
| Job | ShortRun (warmup 3, iteration 3) |

## 결과

| Method               | PlayerCount | Mean        | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------------- |------------:|------------:|------:|-------:|----------:|------------:|
| Naive_LinqFilter     |         100 |    508.1 ns |  1.00 | 0.0124 |   2,112 B |        1.00 |
| Optimized_PoolBuffer |         100 |    176.6 ns |  0.35 |      - |       0 B |        0.00 |
| Naive_LinqFilter     |       1,000 |  4,319.4 ns |  1.00 | 0.0992 |  16,928 B |        1.00 |
| Optimized_PoolBuffer |       1,000 |  1,148.3 ns |  0.27 |      - |       0 B |        0.00 |
| Naive_LinqFilter     |       5,000 | 14,582.7 ns |  1.00 | 0.5035 |  82,840 B |        1.00 |
| Optimized_PoolBuffer |       5,000 |  3,826.0 ns |  0.26 |      - |       0 B |        0.00 |

## 해석

- **속도**: 5,000 명 동시 접속 기준 **3.8 배** 빠름 (14.6μs → 3.8μs).
- **GC 압박**: per-call 할당량이 **82KB → 0B**. Gen0 컬렉션 빈도 자체가 사라짐.
- **틱당 1만 패킷** (1,000 봇 × 100ms tick × 10TPS) 환경 기준:
  - Naive: 1초당 약 **165MB** 신규 할당 → Gen0 GC 폭증.
  - Optimized: 할당량 평행선, GC 일시정지 없음.

## 어디가 달라졌나

### Naive (LINQ 기반)

```csharp
var copy = _world.ToArray();          // KeyValuePair[] 신규 할당
var hits = copy
    .Where(kv => kv.Key != self)      // WhereEnumerator 박싱
    .Where(kv => InRange(kv.Value))   // 두 번째 박싱
    .Select(kv => kv.Key)             // SelectEnumerator 박싱
    .ToList();                        // List<int> 할당
```

LINQ 체인은 가독성은 좋지만 호출당 4종의 객체를 새로 만든다.

### Optimized (ArrayPool + 수동 루프)

```csharp
var buf = ArrayPool<int>.Shared.Rent(256);  // 풀에서 재사용
try
{
    foreach (var kv in _world)              // struct enumerator
    {
        if (kv.Key == self) continue;
        var dx = kv.Value.X - x;
        var dy = kv.Value.Y - y;
        if (dx*dx + dy*dy > sq) continue;
        buf[count++] = kv.Key;
    }
}
finally { ArrayPool<int>.Shared.Return(buf); }
```

`ConcurrentDictionary.Enumerator` 는 struct 라 박싱이 발생하지 않고,
결과 버퍼는 ArrayPool 에서 빌려 쓰므로 **할당이 0** 이 된다.

## 재현

```bash
dotnet build -c Release
dotnet run --project Benchmarks -c Release --no-build -- --job short --filter '*'
```

전체 정밀 모드(권장 측정):

```bash
dotnet run --project Benchmarks -c Release --no-build -- --filter '*'
```
