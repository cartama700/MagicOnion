CREATE TABLE IF NOT EXISTS player_profile (
  player_id    INT          NOT NULL,
  display_name VARCHAR(64)  NOT NULL,
  first_seen   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  last_seen    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (player_id),
  KEY idx_player_profile_last_seen (last_seen)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
