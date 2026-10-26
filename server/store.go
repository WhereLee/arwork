package main

import (
	"database/sql"
	"time"

	_ "modernc.org/sqlite"
)

const schema = `
CREATE TABLE IF NOT EXISTS issues (
	id          TEXT PRIMARY KEY,
	title       TEXT NOT NULL,
	description TEXT NOT NULL,
	priority    TEXT NOT NULL,
	status      TEXT NOT NULL,
	pos_x       REAL NOT NULL,
	pos_y       REAL NOT NULL,
	pos_z       REAL NOT NULL,
	reporter    TEXT NOT NULL DEFAULT '',
	created_at  TEXT NOT NULL,
	updated_at  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS users (
	username      TEXT PRIMARY KEY,
	password_hash TEXT NOT NULL,
	role          TEXT NOT NULL,
	created_at    TEXT NOT NULL
);`

// issueColumns 列出一致顺序的字段，避免三处 SELECT 写得不一致。
const issueColumns = `id, title, description, priority, status, pos_x, pos_y, pos_z, reporter, created_at, updated_at`

// Store 负责 Issue 的持久化，SQLite 保证进程重启后数据仍在。
type Store struct {
	db *sql.DB
}

func NewStore(path string) (*Store, error) {
	db, err := sql.Open("sqlite", path)
	if err != nil {
		return nil, err
	}

	if _, err := db.Exec(schema); err != nil {
		db.Close()
		return nil, err
	}

	store := &Store{db: db}
	if err := store.migrate(); err != nil {
		db.Close()
		return nil, err
	}

	return store, nil
}

// migrate 处理旧库：CREATE TABLE IF NOT EXISTS 不会给已存在的表补列。
func (s *Store) migrate() error {
	var count int
	if err := s.db.QueryRow(
		`SELECT COUNT(*) FROM pragma_table_info('issues') WHERE name = 'reporter'`).Scan(&count); err != nil {
		return err
	}

	if count > 0 {
		return nil
	}

	_, err := s.db.Exec(`ALTER TABLE issues ADD COLUMN reporter TEXT NOT NULL DEFAULT ''`)
	return err
}

func (s *Store) Close() error {
	return s.db.Close()
}

func (s *Store) Create(issue Issue) error {
	_, err := s.db.Exec(
		`INSERT INTO issues (id, title, description, priority, status, pos_x, pos_y, pos_z, reporter, created_at, updated_at)
		 VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
		issue.ID,
		issue.Title,
		issue.Description,
		issue.Priority,
		issue.Status,
		issue.Position.X,
		issue.Position.Y,
		issue.Position.Z,
		issue.Reporter,
		issue.CreatedAt.Format(time.RFC3339Nano),
		issue.UpdatedAt.Format(time.RFC3339Nano),
	)
	return err
}

func (s *Store) List() ([]Issue, error) {
	return s.query(`SELECT ` + issueColumns + ` FROM issues ORDER BY created_at ASC`)
}

// ListByReporter 只返回某个巡检员自己报的问题。
func (s *Store) ListByReporter(username string) ([]Issue, error) {
	return s.query(
		`SELECT `+issueColumns+` FROM issues WHERE reporter = ? ORDER BY created_at ASC`, username)
}

func (s *Store) query(sqlText string, args ...any) ([]Issue, error) {
	rows, err := s.db.Query(sqlText, args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	issues := make([]Issue, 0)
	for rows.Next() {
		issue, err := scanIssue(rows)
		if err != nil {
			return nil, err
		}
		issues = append(issues, issue)
	}

	return issues, rows.Err()
}

func (s *Store) Get(id string) (Issue, bool, error) {
	row := s.db.QueryRow(
		`SELECT `+issueColumns+` FROM issues WHERE id = ?`, id)

	issue, err := scanIssue(row)
	if err == sql.ErrNoRows {
		return Issue{}, false, nil
	}
	if err != nil {
		return Issue{}, false, err
	}

	return issue, true, nil
}

// Update 按 id 写回实体的可变字段；id 不存在时 found 为 false。
func (s *Store) Update(issue Issue) (bool, error) {
	res, err := s.db.Exec(
		`UPDATE issues SET title = ?, description = ?, priority = ?, status = ?, updated_at = ?
		 WHERE id = ?`,
		issue.Title,
		issue.Description,
		issue.Priority,
		issue.Status,
		issue.UpdatedAt.Format(time.RFC3339Nano),
		issue.ID,
	)
	if err != nil {
		return false, err
	}

	affected, err := res.RowsAffected()
	if err != nil {
		return false, err
	}

	return affected > 0, nil
}

// EnsureUser 只在用户名不存在时插入，重启不会覆盖已经改过的密码。
func (s *Store) EnsureUser(u User, now time.Time) error {
	_, err := s.db.Exec(
		`INSERT INTO users (username, password_hash, role, created_at)
		 VALUES (?, ?, ?, ?)
		 ON CONFLICT(username) DO NOTHING`,
		u.Username, u.PasswordHash, u.Role, now.UTC().Format(time.RFC3339Nano))
	return err
}

func (s *Store) FindUser(username string) (User, bool, error) {
	row := s.db.QueryRow(
		`SELECT username, password_hash, role FROM users WHERE username = ?`, username)

	var u User
	err := row.Scan(&u.Username, &u.PasswordHash, &u.Role)
	if err == sql.ErrNoRows {
		return User{}, false, nil
	}
	if err != nil {
		return User{}, false, err
	}

	return u, true, nil
}

func scanIssue(scanner interface{ Scan(dest ...any) error }) (Issue, error) {
	var (
		issue     Issue
		createdAt string
		updatedAt string
	)

	err := scanner.Scan(
		&issue.ID,
		&issue.Title,
		&issue.Description,
		&issue.Priority,
		&issue.Status,
		&issue.Position.X,
		&issue.Position.Y,
		&issue.Position.Z,
		&issue.Reporter,
		&createdAt,
		&updatedAt,
	)
	if err != nil {
		return Issue{}, err
	}

	issue.CreatedAt, err = time.Parse(time.RFC3339Nano, createdAt)
	if err != nil {
		return Issue{}, err
	}

	issue.UpdatedAt, err = time.Parse(time.RFC3339Nano, updatedAt)
	if err != nil {
		return Issue{}, err
	}

	return issue, nil
}
