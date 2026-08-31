using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Asterloom.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationMemberships",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationMemberships", x => new { x.ApplicationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ApplicationMemberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationMemberships_TenantId_ApplicationId_Status",
                schema: "identity",
                table: "ApplicationMemberships",
                columns: new[] { "TenantId", "ApplicationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationMemberships_UserId_Status_ApplicationId",
                schema: "identity",
                table: "ApplicationMemberships",
                columns: new[] { "UserId", "Status", "ApplicationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationMemberships",
                schema: "identity");
        }
    }
}
