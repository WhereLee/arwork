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
	created_at  TEXT NOT NULL,
	updated_at  TEXT NOT NULL
);`

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

	return &Store{db: db}, nil
}

func (s *Store) Close() error {
	return s.db.Close()
}

func (s *Store) Create(issue Issue) error {
	_, err := s.db.Exec(
		`INSERT INTO issues (id, title, description, priority, status, pos_x, pos_y, pos_z, created_at, updated_at)
		 VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
		issue.ID,
		issue.Title,
		issue.Description,
		issue.Priority,
		issue.Status,
		issue.Position.X,
		issue.Position.Y,
		issue.Position.Z,
		issue.CreatedAt.Format(time.RFC3339Nano),
		issue.UpdatedAt.Format(time.RFC3339Nano),
	)
	return err
}

func (s *Store) List() ([]Issue, error) {
	rows, err := s.db.Query(
		`SELECT id, title, description, priority, status, pos_x, pos_y, pos_z, created_at, updated_at
		 FROM issues ORDER BY created_at ASC`)
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
		`SELECT id, title, description, priority, status, pos_x, pos_y, pos_z, created_at, updated_at
		 FROM issues WHERE id = ?`, id)

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
