# Shared

**클라이언트(봇) ↔ 서버가 공유하는 계약(contract) 전용 클래스 라이브러리.**

MagicOnion 은 같은 C# 인터페이스를 서버가 구현하고 클라가 호출하는 구조라서,
이 인터페이스와 DTO 만 따로 분리해 양쪽이 참조하게 한다. 이 프로젝트가
`Server` · `BotClients` · `Tests` 양쪽에서 ProjectReference 로 끌려간다.

## 무엇이 들어있나

| 파일 | 역할 |
|---|---|
| [IMovementHub.cs](IMovementHub.cs) | `IStreamingHub<IMovementHub, IMovementHubReceiver>` — Join/Move/Leave + 서버→클라 알림 3종 (OnPlayerMoved/OnPlayerJoined/OnPlayerLeft) |
| [PlayerState.cs](PlayerState.cs) | `PlayerMoveDto` — `[MessagePackObject]` 구조체. `PlayerId`, `X`, `Y`, `SentAtMs` (P99 latency 측정용) |

## 의존성

```
MagicOnion.Abstractions 7.10.0
MessagePack 3.1.4
```

서버 전용(`MagicOnion.Server`) 이나 클라 전용(`MagicOnion.Client`) 은 **절대 넣지 않는다**.
양쪽 모두에 무관해야 하는 계약만 있는 곳.

## 언제 이 프로젝트를 손대나

- 새로운 RPC 메서드를 추가/변경할 때 (예: `ChatAsync`, `UseSkillAsync`)
- DTO 필드를 추가/변경할 때 (MessagePack `Key` 번호 충돌 주의 — 기존 번호를 재사용하거나 빼면 안 됨)

## 무엇을 넣으면 안 되나

- 서버 로직 (→ `Server`)
- 부하/시나리오 로직 (→ `BotClients`)
- DB/Redis 의존 코드 (→ `Server/Persistence` or `Server/Services`)

계약만 바뀌어도 Server / BotClients 양쪽을 다시 빌드해야 하므로,
**변경은 최대한 보수적으로**. MessagePack 후방 호환성 규칙을 따른다.
