CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS ai_projects (
    id BIGSERIAL PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    root_path TEXT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS ai_chunks (
    id BIGSERIAL PRIMARY KEY,
    project_id BIGINT NOT NULL REFERENCES ai_projects(id),
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
    id BIGSERIAL PRIMARY KEY,
    project_id BIGINT REFERENCES ai_projects(id),
    title TEXT NOT NULL,
    description TEXT NOT NULL,
    source_file TEXT,
    confidence NUMERIC(5,2),
    embedding VECTOR(1024),
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS ai_knowledge (
    id BIGSERIAL PRIMARY KEY,
    project_id BIGINT REFERENCES ai_projects(id),
    kind TEXT NOT NULL,
    title TEXT NOT NULL,
    content TEXT NOT NULL,
    source TEXT,
    confidence NUMERIC(5,2),
    embedding VECTOR(1024),
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ai_chunks_embedding_idx
ON ai_chunks
USING hnsw (embedding vector_cosine_ops);

CREATE INDEX IF NOT EXISTS ai_business_rules_embedding_idx
ON ai_business_rules
USING hnsw (embedding vector_cosine_ops);

CREATE INDEX IF NOT EXISTS ai_knowledge_embedding_idx
ON ai_knowledge
USING hnsw (embedding vector_cosine_ops);
