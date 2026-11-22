using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyGym.Modules.ExerciseCatalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "exercises",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    movement_pattern = table.Column<int>(type: "integer", nullable: false),
                    primary_muscles = table.Column<int[]>(type: "integer[]", nullable: false),
                    secondary_muscles = table.Column<int[]>(type: "integer[]", nullable: false),
                    equipment = table.Column<int>(type: "integer", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    aliases = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exercises", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exercise_names",
                schema: "catalog",
                columns: table => new
                {
                    language_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exercise_names", x => new { x.exercise_id, x.language_code });
                    table.ForeignKey(
                        name: "fk_exercise_names_exercises_exercise_id",
                        column: x => x.exercise_id,
                        principalSchema: "catalog",
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exercises_owner_user_id",
                schema: "catalog",
                table: "exercises",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_exercises_slug",
                schema: "catalog",
                table: "exercises",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercise_names",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "exercises",
                schema: "catalog");
        }
    }
}
