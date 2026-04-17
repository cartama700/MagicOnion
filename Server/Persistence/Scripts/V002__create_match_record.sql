-- Phase 11: UUID v7 PK 로 변경 — AUTO_INCREMENT 핫스팟(TiDB/Spanner) 회피.
-- BINARY(16) 은 UUID v7 이 time-ordered 라서 클러스터드 인덱스 로컬리티도 유지.
CREATE TABLE IF NOT EXISTS match_record (
  id         BINARY(16)   NOT NULL,
  player_id  INT          NOT NULL,
  room_id    VARCHAR(64)  NOT NULL,
  score      BIGINT       NOT NULL DEFAULT 0,
  joined_at  DATETIME(3)  NOT NULL,
  left_at    DATETIME(3)  NULL,
  PRIMARY KEY (id),
  KEY idx_match_record_room_left (room_id, left_at),
  KEY idx_match_record_player    (player_id, joined_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
