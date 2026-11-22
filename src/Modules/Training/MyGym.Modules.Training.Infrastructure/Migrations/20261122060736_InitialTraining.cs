using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyGym.Modules.Training.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialTraining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "training");

            migrationBuilder.CreateTable(
                name: "programs",
                schema: "training",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    visibility = table.Column<int>(type: "integer", nullable: false),
                    forked_from_program_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_programs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "training",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    bodyweight_unit = table.Column<int>(type: "integer", nullable: true),
                    bodyweight_value = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    program_day_id = table.Column<Guid>(type: "uuid", nullable: true),
                    program_id = table.Column<Guid>(type: "uuid", nullable: true),
                    program_revision = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "program_days",
                schema: "training",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    program_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_program_days", x => x.id);
                    table.ForeignKey(
                        name: "fk_program_days_programs_program_id",
                        column: x => x.program_id,
                        principalSchema: "training",
                        principalTable: "programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session_exercises",
                schema: "training",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    alternative_exercise_id = table.Column<Guid>(type: "uuid", nullable: true),
                    exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    rest = table.Column<string>(type: "text", nullable: false),
                    target_reps = table.Column<string>(type: "text", nullable: false),
                    warmup = table.Column<string>(type: "jsonb", nullable: false),
                    working_set_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_exercises", x => x.id);
                    table.ForeignKey(
                        name: "fk_session_exercises_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "training",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exercise_prescriptions",
                schema: "training",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    program_day_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alternative_exercise_id = table.Column<Guid>(type: "uuid", nullable: true),
                    working_set_count = table.Column<int>(type: "integer", nullable: false),
                    target_reps = table.Column<string>(type: "text", nullable: false),
                    rest = table.Column<string>(type: "text", nullable: false),
                    warmup = table.Column<string>(type: "jsonb", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exercise_prescriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_exercise_prescriptions_program_days_program_day_id",
                        column: x => x.program_day_id,
                        principalSchema: "training",
                        principalTable: "program_days",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "logged_sets",
                schema: "training",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    reps = table.Column<int>(type: "integer", nullable: false),
                    rir = table.Column<int>(type: "integer", nullable: true),
                    client_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    logged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    weight_unit = table.Column<int>(type: "integer", nullable: false),
                    weight_value = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logged_sets", x => x.id);
                    table.ForeignKey(
                        name: "fk_logged_sets_session_exercise_session_exercise_id",
                        column: x => x.session_exercise_id,
                        principalSchema: "training",
                        principalTable: "session_exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exercise_prescriptions_program_day_id_order",
                schema: "training",
                table: "exercise_prescriptions",
                columns: new[] { "program_day_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_logged_sets_session_exercise_id_client_set_id",
                schema: "training",
                table: "logged_sets",
                columns: new[] { "session_exercise_id", "client_set_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_program_days_program_id_order",
                schema: "training",
                table: "program_days",
                columns: new[] { "program_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_programs_owner_user_id",
                schema: "training",
                table: "programs",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_exercises_session_id_order",
                schema: "training",
                table: "session_exercises",
                columns: new[] { "session_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id_in_progress",
                schema: "training",
                table: "sessions",
                column: "user_id",
                unique: true,
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id_started_at",
                schema: "training",
                table: "sessions",
                columns: new[] { "user_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercise_prescriptions",
                schema: "training");

            migrationBuilder.DropTable(
                name: "logged_sets",
                schema: "training");

            migrationBuilder.DropTable(
                name: "program_days",
                schema: "training");

            migrationBuilder.DropTable(
                name: "session_exercises",
                schema: "training");

            migrationBuilder.DropTable(
                name: "programs",
                schema: "training");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "training");
        }
    }
}
