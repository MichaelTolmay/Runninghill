-- Applied only to an empty database. Future changes need versioned migrations.
-- Keep setup atomic: either the schema and its marker are both created, or neither is.
BEGIN;
CREATE SCHEMA runninghill;
-- Exactly one marker row is allowed. The service expects version 1 before reporting ready.
CREATE TABLE runninghill.schema_info (
    id integer PRIMARY KEY CHECK (id = 1),
    version integer NOT NULL
);
INSERT INTO runninghill.schema_info (id, version) VALUES (1, 1);
COMMIT;
-- Safe to re-run against an existing version 1 database. Never erase a user's collection.
BEGIN;
SELECT pg_advisory_xact_lock(2026100802);
CREATE TABLE IF NOT EXISTS runninghill.words (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    word text NOT NULL CHECK (length(word) BETWEEN 1 AND 80),
    type text NOT NULL CHECK (type IN ('Noun','Verb','Adjective','Adverb','Pronoun','Preposition','Interjection','Conjunction','Determiner'))
);
CREATE UNIQUE INDEX IF NOT EXISTS words_word_type ON runninghill.words (lower(word), type);
CREATE INDEX IF NOT EXISTS words_type_id ON runninghill.words (type, id);
-- Prefix search can use this index instead of examining every word.
CREATE INDEX IF NOT EXISTS words_prefix ON runninghill.words (lower(word) text_pattern_ops);
CREATE TABLE IF NOT EXISTS runninghill.sentences (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    request_id uuid NOT NULL UNIQUE,
    word_ids bigint[] NOT NULL,
    text text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
-- Sentences store a text snapshot, so deleting a word does not destroy saved sentences.
UPDATE runninghill.schema_info SET version = 2 WHERE id = 1 AND version = 1;
COMMIT;
