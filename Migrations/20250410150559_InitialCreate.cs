using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace techNL_Forms_app.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Forms",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "BannerDescription",
                table: "Forms",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BannerImage",
                table: "Forms",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FormAnalytics",
                columns: table => new
                {
                    AnalyticsId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FormId = table.Column<int>(type: "int", nullable: false),
                    TotalViews = table.Column<int>(type: "int", nullable: false),
                    UniqueViews = table.Column<int>(type: "int", nullable: false),
                    TotalSubmissions = table.Column<int>(type: "int", nullable: false),
                    LinkClicks = table.Column<int>(type: "int", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAnalytics", x => x.AnalyticsId);
                    table.ForeignKey(
                        name: "FK_FormAnalytics_Forms_FormId",
                        column: x => x.FormId,
                        principalTable: "Forms",
                        principalColumn: "FormId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormAnalytics_FormId",
                table: "FormAnalytics",
                column: "FormId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormAnalytics");

            migrationBuilder.DropColumn(
                name: "BannerDescription",
                table: "Forms");

            migrationBuilder.DropColumn(
                name: "BannerImage",
                table: "Forms");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Forms",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
