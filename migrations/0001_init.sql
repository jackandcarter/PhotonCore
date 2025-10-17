CREATE TABLE IF NOT EXISTS accounts (
  id            CHAR(36)      NOT NULL,
  username      VARCHAR(32)   NOT NULL UNIQUE,
  password_hash VARCHAR(100)  NOT NULL,
  flags         JSON          NULL,
  created_at    TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS sessions (
  id            CHAR(36)     NOT NULL,
  account_id    CHAR(36)     NOT NULL,
  created_at    TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  expires_at    TIMESTAMP    NULL,
  PRIMARY KEY (id),
  INDEX (account_id),
  CONSTRAINT fk_sessions_accounts FOREIGN KEY (account_id) REFERENCES accounts(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
