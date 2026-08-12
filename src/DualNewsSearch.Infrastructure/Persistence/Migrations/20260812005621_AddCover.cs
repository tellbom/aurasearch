using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DualNewsSearch.Infrastructure.Persistence.Migrations
{
    public partial class AddCover : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cover",
                table: "aurasearch_desired_documents",
                type: "NVARCHAR2(2048)",
                maxLength: 2048,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cover",
                table: "aurasearch_desired_documents");
        }
    }
}
