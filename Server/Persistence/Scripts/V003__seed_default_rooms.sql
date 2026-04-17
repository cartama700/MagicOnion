CREATE TABLE IF NOT EXISTS room_meta (
  room_id    VARCHAR(64)  NOT NULL,
  created_at DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (room_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT IGNORE INTO room_meta (room_id) VALUES ('world'), ('room-00'), ('room-01'), ('room-02'), ('room-03');
