-- =============================================================================
-- NexusPM Database Initialization Script
-- Runs once on first container start via docker-entrypoint-initdb.d
-- For production: use EF Core migrations instead
-- =============================================================================

-- Enable required extensions
CREATE EXTENSION IF NOT EXISTS "pgcrypto";     -- gen_random_uuid()
CREATE EXTENSION IF NOT EXISTS "pg_trgm";      -- trigram indexes for LIKE search
CREATE EXTENSION IF NOT EXISTS "unaccent";     -- accent-insensitive search

-- =============================================================================
-- USERS & IDENTITY
-- =============================================================================

CREATE TABLE IF NOT EXISTS users (
    user_id          UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    email            VARCHAR(320) NOT NULL,
    email_normalized VARCHAR(320) NOT NULL,
    display_name     VARCHAR(100) NOT NULL,
    avatar_url       VARCHAR(2048),
    password_hash    VARCHAR(128),
    email_verified   BOOLEAN      NOT NULL DEFAULT FALSE,
    is_active        BOOLEAN      NOT NULL DEFAULT TRUE,
    last_login_at    TIMESTAMPTZ,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_id    UUID,
    updated_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_id    UUID,
    deleted_at       TIMESTAMPTZ
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email_normalized
    ON users(email_normalized)
    WHERE deleted_at IS NULL;

-- =============================================================================
-- REFRESH TOKENS
-- =============================================================================

CREATE TABLE IF NOT EXISTS refresh_tokens (
    token_id      UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id       UUID         NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    token_hash    VARCHAR(128) NOT NULL,
    expires_at    TIMESTAMPTZ  NOT NULL,
    revoked_at    TIMESTAMPTZ,
    replaced_by_id UUID        REFERENCES refresh_tokens(token_id),
    ip_address    INET,
    user_agent    TEXT,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_refresh_tokens_hash
    ON refresh_tokens(token_hash);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user_id
    ON refresh_tokens(user_id);

-- =============================================================================
-- WORKSPACES
-- =============================================================================

CREATE TABLE IF NOT EXISTS workspaces (
    workspace_id UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    slug         VARCHAR(63)  NOT NULL,
    name         VARCHAR(100) NOT NULL,
    logo_url     VARCHAR(2048),
    tier         VARCHAR(20)  NOT NULL DEFAULT 'Free',
    owner_id     UUID         NOT NULL REFERENCES users(user_id),
    settings     JSONB        NOT NULL DEFAULT '{}',
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_id UUID,
    updated_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_id UUID,
    deleted_at   TIMESTAMPTZ
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_workspaces_slug
    ON workspaces(slug)
    WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS workspace_memberships (
    membership_id UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    workspace_id  UUID        NOT NULL REFERENCES workspaces(workspace_id) ON DELETE CASCADE,
    user_id       UUID        NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    role          VARCHAR(20) NOT NULL,
    invited_by_id UUID        REFERENCES users(user_id),
    joined_at     TIMESTAMPTZ,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_workspace_memberships
    ON workspace_memberships(workspace_id, user_id);

-- =============================================================================
-- PROJECTS
-- =============================================================================

CREATE TABLE IF NOT EXISTS projects (
    project_id   UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    workspace_id UUID         NOT NULL REFERENCES workspaces(workspace_id) ON DELETE CASCADE,
    name         VARCHAR(200) NOT NULL,
    key          VARCHAR(10)  NOT NULL,
    description  TEXT,
    is_archived  BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_id UUID        NOT NULL REFERENCES users(user_id),
    updated_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_id UUID,
    deleted_at   TIMESTAMPTZ
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_projects_workspace_key
    ON projects(workspace_id, key)
    WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_projects_workspace_id
    ON projects(workspace_id)
    WHERE deleted_at IS NULL;

-- =============================================================================
-- WORKFLOW STATES
-- =============================================================================

CREATE TABLE IF NOT EXISTS workflow_states (
    state_id    UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id  UUID        NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    name        VARCHAR(100) NOT NULL,
    color       VARCHAR(20)  NOT NULL DEFAULT '#6B7280',
    is_terminal BOOLEAN      NOT NULL DEFAULT FALSE,
    position    INTEGER      NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS ix_workflow_states_project_id
    ON workflow_states(project_id);

-- =============================================================================
-- SPRINTS
-- =============================================================================

CREATE TABLE IF NOT EXISTS sprints (
    sprint_id    UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id   UUID        NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    workspace_id UUID        NOT NULL,
    name         VARCHAR(200) NOT NULL,
    goal         TEXT,
    status       VARCHAR(20)  NOT NULL DEFAULT 'Planned',
    start_date   DATE,
    end_date     DATE,
    closed_at    TIMESTAMPTZ,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_id UUID        NOT NULL,
    updated_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_id UUID,
    CONSTRAINT chk_sprint_dates CHECK (start_date IS NULL OR end_date IS NULL OR start_date <= end_date)
);

CREATE INDEX IF NOT EXISTS ix_sprints_project_id   ON sprints(project_id);
CREATE INDEX IF NOT EXISTS ix_sprints_workspace_id ON sprints(workspace_id);

-- =============================================================================
-- TASKS
-- =============================================================================

CREATE TABLE IF NOT EXISTS tasks (
    task_id          UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    workspace_id     UUID         NOT NULL,
    project_id       UUID         NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    sprint_id        UUID         REFERENCES sprints(sprint_id) ON DELETE SET NULL,
    parent_task_id   UUID         REFERENCES tasks(task_id) ON DELETE CASCADE,
    task_key         VARCHAR(20)  NOT NULL,
    title            VARCHAR(500) NOT NULL,
    description      TEXT,
    status_id        UUID         NOT NULL REFERENCES workflow_states(state_id),
    priority         SMALLINT     NOT NULL DEFAULT 3,
    assignee_id      UUID         REFERENCES users(user_id) ON DELETE SET NULL,
    reporter_id      UUID         NOT NULL REFERENCES users(user_id),
    story_points     SMALLINT,
    due_date         DATE,
    estimated_hours  NUMERIC(6,2),
    logged_hours     NUMERIC(6,2) NOT NULL DEFAULT 0,
    labels           TEXT[]       NOT NULL DEFAULT '{}',
    position         INTEGER      NOT NULL DEFAULT 0,
    nesting_level    SMALLINT     NOT NULL DEFAULT 0,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by_id    UUID         NOT NULL,
    updated_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by_id    UUID,
    deleted_at       TIMESTAMPTZ,
    -- Generated full-text search vector
    search_vector TSVECTOR GENERATED ALWAYS AS (
        to_tsvector('english',
            coalesce(title, '') || ' ' || coalesce(description, ''))
    ) STORED,
    CONSTRAINT chk_priority      CHECK (priority BETWEEN 1 AND 5),
    CONSTRAINT chk_nesting_level CHECK (nesting_level BETWEEN 0 AND 3),
    CONSTRAINT chk_logged_hours  CHECK (logged_hours >= 0)
);

-- Core access pattern indexes
CREATE INDEX IF NOT EXISTS ix_tasks_workspace_id
    ON tasks(workspace_id) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_tasks_project_id
    ON tasks(project_id) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_tasks_assignee_id
    ON tasks(assignee_id) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_tasks_sprint_id
    ON tasks(sprint_id) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_tasks_project_status
    ON tasks(project_id, status_id) WHERE deleted_at IS NULL;

-- Full-text search index (GIN for tsvector)
CREATE INDEX IF NOT EXISTS ix_tasks_search_vector
    ON tasks USING GIN(search_vector);

-- Array containment index for label filtering
CREATE INDEX IF NOT EXISTS ix_tasks_labels
    ON tasks USING GIN(labels);

-- =============================================================================
-- TASK COMMENTS
-- =============================================================================

CREATE TABLE IF NOT EXISTS task_comments (
    comment_id   UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    task_id      UUID        NOT NULL REFERENCES tasks(task_id) ON DELETE CASCADE,
    workspace_id UUID        NOT NULL,
    content      TEXT        NOT NULL,
    author_id    UUID        NOT NULL REFERENCES users(user_id),
    is_edited    BOOLEAN     NOT NULL DEFAULT FALSE,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at   TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS ix_task_comments_task_id
    ON task_comments(task_id) WHERE deleted_at IS NULL;

-- =============================================================================
-- TASK ATTACHMENTS
-- =============================================================================

CREATE TABLE IF NOT EXISTS task_attachments (
    attachment_id UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    task_id       UUID         NOT NULL REFERENCES tasks(task_id) ON DELETE CASCADE,
    workspace_id  UUID         NOT NULL,
    file_name     VARCHAR(255) NOT NULL,
    blob_url      VARCHAR(2048) NOT NULL,
    size_bytes    BIGINT       NOT NULL,
    uploaded_by_id UUID        NOT NULL REFERENCES users(user_id),
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_task_attachments_task_id ON task_attachments(task_id);

-- =============================================================================
-- TASK DEPENDENCIES
-- =============================================================================

CREATE TABLE IF NOT EXISTS task_dependencies (
    dependency_id   UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    source_task_id  UUID        NOT NULL REFERENCES tasks(task_id) ON DELETE CASCADE,
    target_task_id  UUID        NOT NULL REFERENCES tasks(task_id) ON DELETE CASCADE,
    dependency_type VARCHAR(20) NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_no_self_dep CHECK (source_task_id != target_task_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_task_dependencies
    ON task_dependencies(source_task_id, target_task_id, dependency_type);

-- =============================================================================
-- NOTIFICATIONS
-- =============================================================================

CREATE TABLE IF NOT EXISTS notifications (
    notification_id UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    workspace_id    UUID        NOT NULL,
    user_id         UUID        NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    type            VARCHAR(50) NOT NULL,
    title           VARCHAR(200) NOT NULL,
    body            TEXT,
    resource_type   VARCHAR(50),
    resource_id     UUID,
    resource_url    VARCHAR(500),
    is_read         BOOLEAN     NOT NULL DEFAULT FALSE,
    read_at         TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_notifications_user_unread
    ON notifications(user_id, is_read, created_at DESC)
    WHERE is_read = FALSE;

-- =============================================================================
-- AUDIT LOG
-- =============================================================================

CREATE TABLE IF NOT EXISTS audit_log_entries (
    entry_id      BIGSERIAL    PRIMARY KEY,
    workspace_id  UUID         NOT NULL,
    entity_type   VARCHAR(100) NOT NULL,
    entity_id     UUID         NOT NULL,
    action        VARCHAR(50)  NOT NULL,
    actor_id      UUID         REFERENCES users(user_id),
    actor_email   VARCHAR(320),
    changes       JSONB        NOT NULL DEFAULT '{}',
    metadata      JSONB        NOT NULL DEFAULT '{}',
    occurred_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_audit_workspace_entity
    ON audit_log_entries(workspace_id, entity_type, entity_id);
CREATE INDEX IF NOT EXISTS ix_audit_occurred_at
    ON audit_log_entries(occurred_at DESC);

-- =============================================================================
-- ROW-LEVEL SECURITY
-- =============================================================================

ALTER TABLE tasks                  ENABLE ROW LEVEL SECURITY;
ALTER TABLE projects               ENABLE ROW LEVEL SECURITY;
ALTER TABLE workspace_memberships  ENABLE ROW LEVEL SECURITY;
ALTER TABLE task_comments          ENABLE ROW LEVEL SECURITY;
ALTER TABLE task_attachments       ENABLE ROW LEVEL SECURITY;
ALTER TABLE notifications          ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_log_entries      ENABLE ROW LEVEL SECURITY;

-- RLS policy: rows visible only when workspace_id matches session variable
-- This is a second layer of defense behind EF Core Global Query Filters
CREATE POLICY tenant_isolation_tasks ON tasks
    USING (workspace_id::text = current_setting('app.current_tenant_id', true));

CREATE POLICY tenant_isolation_projects ON projects
    USING (workspace_id::text = current_setting('app.current_tenant_id', true));

CREATE POLICY tenant_isolation_notifications ON notifications
    USING (workspace_id::text = current_setting('app.current_tenant_id', true));

-- The application role bypasses RLS (for migrations, background jobs)
-- In production: CREATE ROLE nexuspm_admin BYPASSRLS;
-- The API role enforces RLS
-- In production: CREATE ROLE nexuspm_app;

-- =============================================================================
-- PERFORMANCE: TRIGRAM INDEXES FOR FUZZY SEARCH
-- =============================================================================

CREATE INDEX IF NOT EXISTS ix_tasks_title_trgm
    ON tasks USING GIN(title gin_trgm_ops)
    WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_projects_name_trgm
    ON projects USING GIN(name gin_trgm_ops)
    WHERE deleted_at IS NULL;
