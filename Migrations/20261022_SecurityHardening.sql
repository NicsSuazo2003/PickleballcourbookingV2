-- ═══════════════════════════════════════════════════════════════════════
-- Full security hardening migration
-- Applies to: reference codes, users, courts, timeslots, blocked dates
-- Review each section before running. Back up first.
-- ═══════════════════════════════════════════════════════════════════════

BEGIN;

-- ─── 1. Users: case-insensitive unique email ───────────────────────────
-- Before creating this index, find duplicates:
--   SELECT LOWER("Email"), COUNT(*) FROM users GROUP BY 1 HAVING COUNT(*) > 1;
-- If duplicates exist, merge them manually first.
CREATE UNIQUE INDEX IF NOT EXISTS ix_users_email_lower
    ON users (LOWER("Email"));

-- ─── 2. Users: login lockout columns ───────────────────────────────────
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS "FailedLoginCount" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "LockoutUntil" timestamptz NULL;

-- ─── 3. Bookings: unique reference code ────────────────────────────────
-- Before creating this index:
--   SELECT "ReferenceCode", COUNT(*) FROM bookings GROUP BY 1 HAVING COUNT(*) > 1;
CREATE UNIQUE INDEX IF NOT EXISTS ix_bookings_reference_code
    ON bookings ("ReferenceCode");

-- ─── 4. Bookings: dedupe index for pending-payment checks ──────────────
CREATE INDEX IF NOT EXISTS ix_bookings_pending_dedupe
    ON bookings ("ClientId", LOWER("CustomerEmail"), "Status")
    WHERE "Status" = 'pending_payment';

-- ─── 5. TimeSlots: prevent double-booking ─────────────────────────────
-- Before creating, check for existing overlaps:
--   SELECT "CourtId","Date","StartTime",COUNT(*) FROM "TimeSlots"
--   WHERE "CourtId" IS NOT NULL
--   GROUP BY 1,2,3 HAVING COUNT(*) > 1;
CREATE UNIQUE INDEX IF NOT EXISTS ix_timeslots_unique_active
    ON "TimeSlots" ("CourtId", "Date", "StartTime")
    WHERE "CourtId" IS NOT NULL;

-- ─── 6. Courts: speed up tenant-scoped queries ────────────────────────
CREATE INDEX IF NOT EXISTS ix_courts_client_status
    ON courts (client_id, "Status");

-- ─── 7. Courts: prevent cascade-delete of bookings ────────────────────
-- The EF model now uses Restrict, but the existing FK was CASCADE.
-- Drop + recreate to match.
ALTER TABLE bookings
    DROP CONSTRAINT IF EXISTS "FK_bookings_courts_CourtId";

ALTER TABLE bookings
    ADD CONSTRAINT "FK_bookings_courts_CourtId"
    FOREIGN KEY ("CourtId") REFERENCES courts ("Id")
    ON DELETE RESTRICT;

-- ─── 8. Audit log (recommended even if not wired yet) ─────────────────
CREATE TABLE IF NOT EXISTS booking_audit_logs (
    "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "BookingId" uuid NOT NULL,
    "ClientId" uuid NOT NULL,
    "ActorUserId" uuid NULL,
    "Action" text NOT NULL,
    "FromStatus" text NULL,
    "ToStatus" text NULL,
    "FromAmount" numeric(12,2) NULL,
    "ToAmount" numeric(12,2) NULL,
    "Reason" text NULL,
    "IpAddress" text NULL,
    "CreatedAt" timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_audit_booking ON booking_audit_logs ("BookingId");
CREATE INDEX IF NOT EXISTS ix_audit_client
    ON booking_audit_logs ("ClientId", "CreatedAt" DESC);

    -- ─── 9. Backfill MaxAdvanceBookingDays for pre-existing rows ────────
-- Rows created before the column existed have CLR default 0, not 90.
-- 0 means "no limit" in the frontend → users can book 10 years ahead.
UPDATE clients
   SET max_advance_booking_days = 90
 WHERE max_advance_booking_days IS NULL
    OR max_advance_booking_days = 0;

-- ─── 10. Backfill BookingsEnabled for pre-existing rows ─────────────
-- Same story: default false would silently disable all new bookings.
UPDATE clients
   SET bookings_enabled = true
 WHERE bookings_enabled IS NULL;

-- ─── 11. Sanity: no orphaned courts ─────────────────────────────────
-- The new Restrict FK on bookings.CourtId requires every booking to
-- reference a real court. If any exist as orphans (from a past hard delete
-- with Cascade), the FK creation in step 7 already failed — good.
-- This is just a check you can run manually afterwards:
--   SELECT COUNT(*) FROM bookings b
--   LEFT JOIN courts c ON c."Id" = b."CourtId"
--   WHERE c."Id" IS NULL;

COMMIT;