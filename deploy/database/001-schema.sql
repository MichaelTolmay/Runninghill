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
