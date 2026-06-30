-- Additive compatibility migration for the integer-id baseline schema.
-- This file intentionally does not recreate base tables with UUID ids.
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

ALTER TABLE ai_workspaces ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ DEFAULT NOW();

ALTER TABLE ai_projects ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ DEFAULT NOW();

CREATE UNIQUE INDEX IF NOT EXISTS ai_projects_root_path_key
ON ai_projects(root_path);

ALTER TABLE ai_workspace_projects ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ DEFAULT NOW();

ALTER TABLE ai_chunks ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ DEFAULT NOW();
ALTER TABLE ai_chunks ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT NOW();

ALTER TABLE ai_business_rules ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ DEFAULT NOW();
ALTER TABLE ai_business_rules ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT NOW();
ALTER TABLE ai_business_rules ALTER COLUMN source_file DROP NOT NULL;
ALTER TABLE ai_business_rules ALTER COLUMN content_hash DROP NOT NULL;
ALTER TABLE ai_business_rules ALTER COLUMN confidence TYPE NUMERIC(5,2);

ALTER TABLE ai_knowledge ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ DEFAULT NOW();
ALTER TABLE ai_knowledge ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT NOW();
ALTER TABLE ai_knowledge ALTER COLUMN content_hash DROP NOT NULL;
ALTER TABLE ai_knowledge ALTER COLUMN confidence TYPE NUMERIC(5,2);

ALTER TABLE ai_extraction_chunk_state ADD COLUMN IF NOT EXISTS id UUID DEFAULT gen_random_uuid();
ALTER TABLE ai_extraction_chunk_state ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ DEFAULT NOW();
ALTER TABLE ai_extraction_chunk_state ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT NOW();

CREATE TABLE IF NOT EXISTS ai_symbols (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id INTEGER REFERENCES ai_projects(id) ON DELETE CASCADE,
    chunk_id UUID REFERENCES ai_chunks(id) ON DELETE CASCADE,
    kind TEXT NOT NULL,
    full_name TEXT NOT NULL,
    file_path TEXT NOT NULL,
    line_start INTEGER,
    line_end INTEGER,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(project_id, full_name)
);

CREATE TABLE IF NOT EXISTS ai_symbol_relations (
    source_id UUID REFERENCES ai_symbols(id) ON DELETE CASCADE,
    target_id UUID REFERENCES ai_symbols(id) ON DELETE CASCADE,
    relation TEXT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    PRIMARY KEY (source_id, target_id, relation)
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ai_business_rules_status_check') THEN
        ALTER TABLE ai_business_rules ADD CONSTRAINT ai_business_rules_status_check CHECK (status IN ('candidate', 'accepted', 'rejected'));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ai_knowledge_status_check') THEN
        ALTER TABLE ai_knowledge ADD CONSTRAINT ai_knowledge_status_check CHECK (status IN ('candidate', 'accepted', 'rejected'));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ai_extraction_chunk_state_stage_check') THEN
        ALTER TABLE ai_extraction_chunk_state ADD CONSTRAINT ai_extraction_chunk_state_stage_check CHECK (stage IN ('rules', 'knowledge'));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ai_extraction_chunk_state_status_check') THEN
        ALTER TABLE ai_extraction_chunk_state ADD CONSTRAINT ai_extraction_chunk_state_status_check CHECK (status IN ('processed', 'failed'));
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ai_chunks_embedding_idx
ON ai_chunks
USING hnsw (embedding vector_cosine_ops);

CREATE INDEX IF NOT EXISTS ai_chunks_project_updated_idx
ON ai_chunks(project_id, updated_at DESC);

CREATE INDEX IF NOT EXISTS ai_chunks_project_file_idx
ON ai_chunks(project_id, file_path);

CREATE INDEX IF NOT EXISTS ai_chunks_project_symbol_idx
ON ai_chunks(project_id, symbol_name);

CREATE INDEX IF NOT EXISTS ai_chunks_project_hash_idx
ON ai_chunks(project_id, content_hash);

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

CREATE INDEX IF NOT EXISTS ai_extraction_chunk_state_stage_status_idx
ON ai_extraction_chunk_state(stage, status);

CREATE INDEX IF NOT EXISTS ai_extraction_chunk_state_chunk_stage_hash_idx
ON ai_extraction_chunk_state(chunk_id, stage, content_hash);

CREATE INDEX IF NOT EXISTS ai_symbols_project_full_name_idx
ON ai_symbols(project_id, full_name);

CREATE INDEX IF NOT EXISTS ai_symbol_relations_target_relation_idx
ON ai_symbol_relations(target_id, relation);

CREATE INDEX IF NOT EXISTS ai_symbol_relations_source_relation_idx
ON ai_symbol_relations(source_id, relation);
