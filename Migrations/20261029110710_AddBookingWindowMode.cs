using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickleballBookingSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingWindowMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bookings_courts_CourtId",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "IX_timeslots_CourtId",
                table: "timeslots");

            migrationBuilder.DropIndex(
                name: "IX_courts_client_id",
                table: "courts");

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutUntil",
                table: "users",
                type: "timestamp with time zone",
                nullable: true,
                defaultValueSql: "NOW() AT TIME ZONE 'UTC'");

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetExpiresAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true,
                defaultValueSql: "NOW() AT TIME ZONE 'UTC'");

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetTokenHash",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "courts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW() AT TIME ZONE 'UTC'");

            migrationBuilder.AlterColumn<DateTime>(
                name: "booking_open_date",
                table: "clients",
                type: "date",
                nullable: true,
                defaultValueSql: "NOW() AT TIME ZONE 'UTC'",
                oldClrType: typeof(DateTime),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "booking_close_date",
                table: "clients",
                type: "date",
                nullable: true,
                defaultValueSql: "NOW() AT TIME ZONE 'UTC'",
                oldClrType: typeof(DateTime),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "available_amenities_json",
                table: "clients",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "bookings_enabled",
                table: "clients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "max_advance_booking_days",
                table: "clients",
                type: "integer",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.CreateTable(
                name: "security_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW() AT TIME ZONE 'UTC'"),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorEmail = table.Column<string>(type: "text", nullable: true),
                    IpHash = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<IPAddress>(type: "inet", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Resource = table.Column<string>(type: "text", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorCode = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_timeslots_CourtId_Date_StartTime",
                table: "timeslots",
                columns: new[] { "CourtId", "Date", "StartTime" },
                unique: true,
                filter: "\"CourtId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_courts_client_id_Status",
                table: "courts",
                columns: new[] { "client_id", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_bookings_ReferenceCode",
                table: "bookings",
                column: "ReferenceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_secevents_client_time",
                table: "security_events",
                columns: new[] { "ClientId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "ix_secevents_type_time",
                table: "security_events",
                columns: new[] { "ClientId", "EventType", "OccurredAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_courts_CourtId",
                table: "bookings",
                column: "CourtId",
                principalTable: "courts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bookings_courts_CourtId",
                table: "bookings");

            migrationBuilder.DropTable(
                name: "security_events");

            migrationBuilder.DropIndex(
                name: "IX_timeslots_CourtId_Date_StartTime",
                table: "timeslots");

            migrationBuilder.DropIndex(
                name: "IX_courts_client_id_Status",
                table: "courts");

            migrationBuilder.DropIndex(
                name: "IX_bookings_ReferenceCode",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LockoutUntil",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PasswordResetExpiresAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenHash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "courts");

            migrationBuilder.DropColumn(
                name: "available_amenities_json",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "bookings_enabled",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "max_advance_booking_days",
                table: "clients");

            migrationBuilder.AlterColumn<DateTime>(
                name: "booking_open_date",
                table: "clients",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "date",
                oldNullable: true,
                oldDefaultValueSql: "NOW() AT TIME ZONE 'UTC'");

            migrationBuilder.AlterColumn<DateTime>(
                name: "booking_close_date",
                table: "clients",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "date",
                oldNullable: true,
                oldDefaultValueSql: "NOW() AT TIME ZONE 'UTC'");

            migrationBuilder.CreateIndex(
                name: "IX_timeslots_CourtId",
                table: "timeslots",
                column: "CourtId");

            migrationBuilder.CreateIndex(
                name: "IX_courts_client_id",
                table: "courts",
                column: "client_id");

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_courts_CourtId",
                table: "bookings",
                column: "CourtId",
                principalTable: "courts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
