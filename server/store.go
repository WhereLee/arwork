package main

import (
	"database/sql"
	"strings"
	"time"

	_ "modernc.org/sqlite"
)

const schema = `
CREATE TABLE IF NOT EXISTS issues (
	id             TEXT PRIMARY KEY,
	title          TEXT NOT NULL,
	description    TEXT NOT NULL,
	priority       TEXT NOT NULL,
	status         TEXT NOT NULL,
	pos_x          REAL NOT NULL,
	pos_y          REAL NOT NULL,
	pos_z          REAL NOT NULL,
	reporter       TEXT NOT NULL DEFAULT '',
	category       TEXT NOT NULL DEFAULT '',
	ai_confidence  REAL NOT NULL DEFAULT 0,
	ai_reason      TEXT NOT NULL DEFAULT '',
	dispatch_mode  TEXT NOT NULL DEFAULT '',
	assignee       TEXT NOT NULL DEFAULT '',
	assigned_at    TEXT NOT NULL DEFAULT '',
	assigned_by    TEXT NOT NULL DEFAULT '',
	submitted_at   TEXT NOT NULL DEFAULT '',
	repair_note    TEXT NOT NULL DEFAULT '',
	repair_summary TEXT NOT NULL DEFAULT '',
	created_at     TEXT NOT NULL,
	updated_at     TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS users (
	username      TEXT PRIMARY KEY,
	password_hash TEXT NOT NULL,
	role          TEXT NOT NULL,
	specialties   TEXT NOT NULL DEFAULT '',
	active        INTEGER NOT NULL DEFAULT 1,
	created_at    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS issue_events (
	id          INTEGER PRIMARY KEY AUTOINCREMENT,
	issue_id    TEXT NOT NULL,
	actor       TEXT NOT NULL,
	action      TEXT NOT NULL,
	from_status TEXT NOT NULL,
	to_status   TEXT NOT NULL,
	note        TEXT NOT NULL,
	created_at  TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_issue_events_issue ON issue_events(issue_id, id);`

// issueColumns 列出一致顺序的字段，避免多处 SELECT 写得不一致。
const issueColumns = `id, title, description, priority, status, pos_x, pos_y, pos_z, reporter,
	category, ai_confidence, ai_reason, dispatch_mode, assignee, assigned_at, assigned_by,
	submitted_at, repair_note, repair_summary, created_at, updated_at`

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

// columnMigrations 是旧库需要补的列。新库由 schema 一次建全，这里只处理升级。
var columnMigrations = []struct {
	table  string
	column string
	ddl    string
}{
	{"issues", "reporter", `ALTER TABLE issues ADD COLUMN reporter TEXT NOT NULL DEFAULT ''`},
	{"issues", "category", `ALTER TABLE issues ADD COLUMN category TEXT NOT NULL DEFAULT ''`},
	{"issues", "ai_confidence", `ALTER TABLE issues ADD COLUMN ai_confidence REAL NOT NULL DEFAULT 0`},
	{"issues", "ai_reason", `ALTER TABLE issues ADD COLUMN ai_reason TEXT NOT NULL DEFAULT ''`},
	{"issues", "dispatch_mode", `ALTER TABLE issues ADD COLUMN dispatch_mode TEXT NOT NULL DEFAULT ''`},
	{"issues", "assignee", `ALTER TABLE issues ADD COLUMN assignee TEXT NOT NULL DEFAULT ''`},
	{"issues", "assigned_at", `ALTER TABLE issues ADD COLUMN assigned_at TEXT NOT NULL DEFAULT ''`},
	{"issues", "assigned_by", `ALTER TABLE issues ADD COLUMN assigned_by TEXT NOT NULL DEFAULT ''`},
	{"issues", "submitted_at", `ALTER TABLE issues ADD COLUMN submitted_at TEXT NOT NULL DEFAULT ''`},
	{"issues", "repair_note", `ALTER TABLE issues ADD COLUMN repair_note TEXT NOT NULL DEFAULT ''`},
	{"issues", "repair_summary", `ALTER TABLE issues ADD COLUMN repair_summary TEXT NOT NULL DEFAULT ''`},
	{"users", "specialties", `ALTER TABLE users ADD COLUMN specialties TEXT NOT NULL DEFAULT ''`},
	{"users", "active", `ALTER TABLE users ADD COLUMN active INTEGER NOT NULL DEFAULT 1`},
}

// migrate 处理旧库：CREATE TABLE IF NOT EXISTS 不会给已存在的表补列。
func (s *Store) migrate() error {
	for _, m := range columnMigrations {
		if err := s.ensureColumn(m.table, m.column, m.ddl); err != nil {
			return err
		}
	}
	return nil
}

func (s *Store) ensureColumn(table, column, ddl string) error {
	var count int
	// table 是上面硬编码的，不存在注入
	query := `SELECT COUNT(*) FROM pragma_table_info('` + table + `') WHERE name = ?`
	if err := s.db.QueryRow(query, column).Scan(&count); err != nil {
		return err
	}

	if count > 0 {
		return nil
	}

	_, err := s.db.Exec(ddl)
	return err
}

func (s *Store) Close() error {
	return s.db.Close()
}

// Create 写入工单并记一条 created 事件，两步在同一个事务里。
func (s *Store) Create(issue Issue, event IssueEvent) error {
	tx, err := s.db.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()

	if _, err := tx.Exec(
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
	); err != nil {
		return err
	}

	if err := insertEvent(tx, event); err != nil {
		return err
	}

	return tx.Commit()
}

func (s *Store) List() ([]Issue, error) {
	return s.query(`SELECT ` + issueColumns + ` FROM issues ORDER BY created_at ASC`)
}

// ListByReporter 只返回某个巡检员自己报的问题。
func (s *Store) ListByReporter(username string) ([]Issue, error) {
	return s.query(
		`SELECT `+issueColumns+` FROM issues WHERE reporter = ? ORDER BY created_at ASC`, username)
}

// ListByAssignee 返回派给某个修复者的工单。
// 处理中和待确认的排前面，已解决的排后面，让他一打开就看到手头的活。
func (s *Store) ListByAssignee(username string) ([]Issue, error) {
	return s.query(
		`SELECT `+issueColumns+` FROM issues WHERE assignee = ?
		 ORDER BY CASE status
		   WHEN '`+StatusInProgress+`' THEN 0
		   WHEN '`+StatusPendingReview+`' THEN 1
		   ELSE 2 END, created_at ASC`, username)
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

// ApplyChange 在「状态仍是 expectedStatus」的前提下写回工单，并记一条事件。
// 返回 false 表示状态已被别人改过（并发冲突），调用方应回 409。
// 不做「读—改—写」，所以两个并发请求不会相互覆盖。
func (s *Store) ApplyChange(issue Issue, expectedStatus string, event IssueEvent) (bool, error) {
	tx, err := s.db.Begin()
	if err != nil {
		return false, err
	}
	defer tx.Rollback()

	res, err := tx.Exec(
		`UPDATE issues SET
			title = ?, description = ?, priority = ?, status = ?,
			category = ?, ai_confidence = ?, ai_reason = ?,
			dispatch_mode = ?, assignee = ?, assigned_at = ?, assigned_by = ?,
			submitted_at = ?, repair_note = ?, repair_summary = ?,
			updated_at = ?
		 WHERE id = ? AND status = ?`,
		issue.Title, issue.Description, issue.Priority, issue.Status,
		issue.Category, issue.AIConfidence, issue.AIReason,
		issue.DispatchMode, issue.Assignee, issue.AssignedAt, issue.AssignedBy,
		issue.SubmittedAt, issue.RepairNote, issue.RepairSummary,
		issue.UpdatedAt.Format(time.RFC3339Nano),
		issue.ID, expectedStatus,
	)
	if err != nil {
		return false, err
	}

	affected, err := res.RowsAffected()
	if err != nil {
		return false, err
	}

	if affected == 0 {
		return false, nil
	}

	if err := insertEvent(tx, event); err != nil {
		return false, err
	}

	if err := tx.Commit(); err != nil {
		return false, err
	}

	return true, nil
}

// RecordEvent 只记一条事件，不动工单。用于 AI 判定失败这类不改状态的情况。
func (s *Store) RecordEvent(event IssueEvent) error {
	return insertEvent(s.db, event)
}

// ListEvents 按发生顺序返回一条工单的全部留痕。
func (s *Store) ListEvents(issueID string) ([]IssueEvent, error) {
	rows, err := s.db.Query(
		`SELECT id, issue_id, actor, action, from_status, to_status, note, created_at
		 FROM issue_events WHERE issue_id = ? ORDER BY id ASC`, issueID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	events := make([]IssueEvent, 0)
	for rows.Next() {
		var e IssueEvent
		if err := rows.Scan(&e.ID, &e.IssueID, &e.Actor, &e.Action,
			&e.FromStatus, &e.ToStatus, &e.Note, &e.CreatedAt); err != nil {
			return nil, err
		}
		events = append(events, e)
	}

	return events, rows.Err()
}

const userColumns = `username, password_hash, role, specialties, active`

// CreateUser 新建账号，用户名已存在时返回 ErrUserExists。
func (s *Store) CreateUser(u User, now time.Time) error {
	_, err := s.db.Exec(
		`INSERT INTO users (username, password_hash, role, specialties, active, created_at)
		 VALUES (?, ?, ?, ?, ?, ?)`,
		u.Username, u.PasswordHash, u.Role, JoinSpecialties(u.Specialties), boolToInt(u.Active),
		now.UTC().Format(time.RFC3339Nano))

	// SQLite 的唯一约束错误会带 UNIQUE 字样，转成业务错误好让上层回 409
	if err != nil && strings.Contains(err.Error(), "UNIQUE") {
		return ErrUserExists
	}

	return err
}

// UpdateUserSkills 改技能。
func (s *Store) UpdateUserSkills(username string, skills []string) error {
	_, err := s.db.Exec(
		`UPDATE users SET specialties = ? WHERE username = ?`,
		JoinSpecialties(skills), username)
	return err
}

// SetUserActive 启停用。
func (s *Store) SetUserActive(username string, active bool) error {
	_, err := s.db.Exec(
		`UPDATE users SET active = ? WHERE username = ?`,
		boolToInt(active), username)
	return err
}

// EnsureUser 只在用户名不存在时插入，重启不会覆盖已经改过的密码。
func (s *Store) EnsureUser(u User, now time.Time) error {
	_, err := s.db.Exec(
		`INSERT INTO users (username, password_hash, role, specialties, active, created_at)
		 VALUES (?, ?, ?, ?, ?, ?)
		 ON CONFLICT(username) DO NOTHING`,
		u.Username, u.PasswordHash, u.Role, JoinSpecialties(u.Specialties), boolToInt(u.Active),
		now.UTC().Format(time.RFC3339Nano))
	return err
}

func (s *Store) FindUser(username string) (User, bool, error) {
	row := s.db.QueryRow(`SELECT `+userColumns+` FROM users WHERE username = ?`, username)

	u, err := scanUser(row)
	if err == sql.ErrNoRows {
		return User{}, false, nil
	}
	if err != nil {
		return User{}, false, err
	}

	return u, true, nil
}

func (s *Store) ListUsers() ([]User, error) {
	rows, err := s.db.Query(`SELECT ` + userColumns + ` FROM users ORDER BY username ASC`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	users := make([]User, 0)
	for rows.Next() {
		u, err := scanUser(rows)
		if err != nil {
			return nil, err
		}
		users = append(users, u)
	}

	return users, rows.Err()
}

// ListRepairers 返回在用且具备该类技能的修复者。
// 技能存在逗号分隔的字符串里，用户量很小，取回来在内存里筛更安全。
func (s *Store) ListRepairers(category string) ([]User, error) {
	rows, err := s.db.Query(
		`SELECT `+userColumns+` FROM users WHERE role = ? ORDER BY username ASC`, RoleRepairer)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	users := make([]User, 0)
	for rows.Next() {
		u, err := scanUser(rows)
		if err != nil {
			return nil, err
		}

		if u.Active && u.HasSkill(category) {
			users = append(users, u)
		}
	}

	return users, rows.Err()
}

// CountActiveWork 统计某人名下未完成的工单（处理中 + 待确认），派单时用来做负载均衡。
func (s *Store) CountActiveWork(username string) (int, error) {
	var n int
	err := s.db.QueryRow(
		`SELECT COUNT(*) FROM issues WHERE assignee = ? AND status IN (?, ?)`,
		username, StatusInProgress, StatusPendingReview).Scan(&n)
	return n, err
}

// CountReturns 统计某人历史上退回过多少条同类工单。
// 用事件的 actor 而不是工单当前的 assignee，避免改派后把旧账算到新人头上。
func (s *Store) CountReturns(username, category string) (int, error) {
	var n int
	err := s.db.QueryRow(
		`SELECT COUNT(*) FROM issue_events e JOIN issues i ON i.id = e.issue_id
		 WHERE e.action = ? AND e.actor = ? AND i.category = ?`,
		ActionReturned, username, category).Scan(&n)
	return n, err
}

// ReturnersOf 返回把这条工单退回过的人，派单时要排除，避免又派回同一个坑。
func (s *Store) ReturnersOf(issueID string) ([]string, error) {
	rows, err := s.db.Query(
		`SELECT DISTINCT actor FROM issue_events WHERE issue_id = ? AND action = ?`,
		issueID, ActionReturned)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	names := make([]string, 0)
	for rows.Next() {
		var name string
		if err := rows.Scan(&name); err != nil {
			return nil, err
		}
		names = append(names, name)
	}

	return names, rows.Err()
}

func scanUser(scanner interface{ Scan(dest ...any) error }) (User, error) {
	var (
		u         User
		specs     string
		activeInt int
	)

	if err := scanner.Scan(&u.Username, &u.PasswordHash, &u.Role, &specs, &activeInt); err != nil {
		return User{}, err
	}

	u.Specialties = SplitSpecialties(specs)
	u.Active = activeInt != 0

	return u, nil
}

func boolToInt(b bool) int {
	if b {
		return 1
	}
	return 0
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
		&issue.Category,
		&issue.AIConfidence,
		&issue.AIReason,
		&issue.DispatchMode,
		&issue.Assignee,
		&issue.AssignedAt,
		&issue.AssignedBy,
		&issue.SubmittedAt,
		&issue.RepairNote,
		&issue.RepairSummary,
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
