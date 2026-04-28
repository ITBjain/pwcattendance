using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PwcApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 🔥 ONLY add the 5 new telemetry columns to the existing table
            migrationBuilder.AddColumn<int>(
                name: "TotalCalls",
                table: "ResourceAttendances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalEmails",
                table: "ResourceAttendances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalParentsTargeted",
                table: "ResourceAttendances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalWhatsApp",
                table: "ResourceAttendances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Remark",
                table: "ResourceAttendances",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tell it how to undo the migration if needed
            migrationBuilder.DropColumn(
                name: "TotalCalls",
                table: "ResourceAttendances");

            migrationBuilder.DropColumn(
                name: "TotalEmails",
                table: "ResourceAttendances");

            migrationBuilder.DropColumn(
                name: "TotalParentsTargeted",
                table: "ResourceAttendances");

            migrationBuilder.DropColumn(
                name: "TotalWhatsApp",
                table: "ResourceAttendances");

            migrationBuilder.DropColumn(
                name: "Remark",
                table: "ResourceAttendances");
        }
    }
}