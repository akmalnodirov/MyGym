using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyGym.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    first_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    language_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    birth_year = table.Column<int>(type: "integer", nullable: true),
                    bodyweight_value = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    bodyweight_unit = table.Column<int>(type: "integer", nullable: true),
                    preferred_unit = table.Column<int>(type: "integer", nullable: false),
                    loading_increment_value = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    loading_increment_unit = table.Column<int>(type: "integer", nullable: false),
                    experience_level = table.Column<int>(type: "integer", nullable: false),
                    available_days_per_week = table.Column<int>(type: "integer", nullable: true),
                    session_minutes = table.Column<int>(type: "integer", nullable: true),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_telegram_user_id",
                schema: "identity",
                table: "users",
                column: "telegram_user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");
        }
    }
}
