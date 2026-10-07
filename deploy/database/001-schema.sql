-- Applied only to an empty database. Future changes need versioned migrations.
BEGIN;
CREATE SCHEMA runninghill;
CREATE TABLE runninghill.schema_info (
    id integer PRIMARY KEY CHECK (id = 1),
    version integer NOT NULL
);
INSERT INTO runninghill.schema_info (id, version) VALUES (1, 1);
COMMIT;
