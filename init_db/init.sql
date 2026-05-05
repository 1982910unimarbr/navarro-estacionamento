-- Initial schema for parking MVP (Postgres)

CREATE TABLE IF NOT EXISTS spots (
  spotId TEXT PRIMARY KEY,
  sectorId TEXT NOT NULL,
  currentState TEXT NOT NULL,
  lastChangeTs TIMESTAMP WITH TIME ZONE,
  lastEventId TEXT
);

CREATE TABLE IF NOT EXISTS spot_events (
  eventId TEXT PRIMARY KEY,
  ts TIMESTAMP WITH TIME ZONE,
  sectorId TEXT,
  spotId TEXT,
  state TEXT,
  rawPayloadJson JSONB
);

CREATE TABLE IF NOT EXISTS sector_snapshots (
  ts TIMESTAMP WITH TIME ZONE,
  sectorId TEXT,
  occupiedCount INTEGER,
  freeCount INTEGER,
  occupancyRate NUMERIC,
  PRIMARY KEY (ts, sectorId)
);

CREATE TABLE IF NOT EXISTS incidents (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tsOpen TIMESTAMP WITH TIME ZONE,
  tsClose TIMESTAMP WITH TIME ZONE,
  type TEXT,
  severity INTEGER,
  sectorId TEXT,
  spotId TEXT,
  evidenceJson JSONB,
  status TEXT
);

CREATE TABLE IF NOT EXISTS recommendations_log (
  ts TIMESTAMP WITH TIME ZONE,
  fromSector TEXT,
  recommendedSector TEXT,
  reason TEXT,
  dataJson JSONB
);
