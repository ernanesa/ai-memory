CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS ai_workspaces (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT NOT NULL UNIQUE,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS ai_projects (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT NOT NULL,
    root_path TEXT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ai_projects_root_path_key
ON ai_projects(root_path);

CREATE TABLE IF NOT EXISTS ai_workspace_projects (
    workspace_id UUID NOT NULL REFERENCES ai_workspaces(id) ON DELETE CASCADE,
    project_id UUID NOT NULL REFERENCES ai_projects(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    PRIMARY KEY (workspace_id, project_id)
);

CREATE TABLE IF NOT EXISTS ai_chunks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES ai_projects(id),
    file_path TEXT NOT NULL,
    language TEXT,
    chunk_type TEXT,
    symbol_name TEXT,
    content TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    embedding VECTOR(1024),
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(project_id, file_path, content_hash)
);

CREATE TABLE IF NOT EXISTS ai_business_rules (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID REFERENCES ai_projects(id),
    chunk_id UUID REFERENCES ai_chunks(id) ON DELETE SET NULL,
    title TEXT NOT NULL,
    description TEXT NOT NULL,
    source_file TEXT,
    symbol_name TEXT,
    status TEXT NOT NULL DEFAULT 'candidate',
    evidence TEXT,
    content_hash TEXT,
    confidence NUMERIC(5,2),
    embedding VECTOR(1024),
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    CHECK (status IN ('candidate', 'accepted', 'rejected'))
);

CREATE TABLE IF NOT EXISTS ai_knowledge (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID REFERENCES ai_projects(id),
    chunk_id UUID REFERENCES ai_chunks(id) ON DELETE SET NULL,
    kind TEXT NOT NULL,
    title TEXT NOT NULL,
    content TEXT NOT NULL,
    source TEXT,
    symbol_name TEXT,
    status TEXT NOT NULL DEFAULT 'candidate',
    evidence TEXT,
    content_hash TEXT,
    confidence NUMERIC(5,2),
    embedding VECTOR(1024),
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    CHECK (status IN ('candidate', 'accepted', 'rejected'))
);

CREATE INDEX IF NOT EXISTS ai_chunks_embedding_idx
ON ai_chunks
USING hnsw (embedding vector_cosine_ops);

CREATE INDEX IF NOT EXISTS ai_business_rules_embedding_idx
ON ai_business_rules
USING hnsw (embedding vector_cosine_ops);

CREATE INDEX IF NOT EXISTS ai_business_rules_project_status_idx
ON ai_business_rules(project_id, status);

CREATE INDEX IF NOT EXISTS ai_business_rules_chunk_id_idx
ON ai_business_rules(chunk_id);

CREATE INDEX IF NOT EXISTS ai_knowledge_embedding_idx
ON ai_knowledge
USING hnsw (embedding vector_cosine_ops);

CREATE INDEX IF NOT EXISTS ai_knowledge_project_status_idx
ON ai_knowledge(project_id, status);

CREATE INDEX IF NOT EXISTS ai_knowledge_chunk_id_idx
ON ai_knowledge(chunk_id);
