using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_API.Migrations
{
    /// <inheritdoc />
    public partial class DeviceBindingMachineIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MachineName",
                table: "DeviceBindings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WindowsUsername",
                table: "DeviceBindings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MachineName",
                table: "DeviceBindings");

            migrationBuilder.DropColumn(
                name: "WindowsUsername",
                table: "DeviceBindings");
        }
    }
}
