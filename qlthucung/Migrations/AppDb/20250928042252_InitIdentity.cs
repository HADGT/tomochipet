using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace qlthucung.Migrations.AppDb
{
    /// <inheritdoc />
    public partial class InitIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK__KhachHan__7A21BB4CE42C9226",
                table: "KhachHang");

            migrationBuilder.AlterColumn<string>(
                name: "makh",
                table: "KhachHang",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "makh",
                table: "DonHang",
                type: "nvarchar(36)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "makh",
                table: "DichVu",
                type: "nvarchar(36)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "id_kh",
                table: "DanhGia",
                type: "nvarchar(36)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "BirthDate",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddPrimaryKey(
                name: "PK_KhachHang",
                table: "KhachHang",
                column: "makh");

            migrationBuilder.CreateTable(
                name: "SPDichVu",
                columns: table => new
                {
                    DichVuID = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TenDichVu = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MoTa = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Gia = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ThoiGianDuKien = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SPDichVu", x => x.DichVuID);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SPDichVu");

            migrationBuilder.DropPrimaryKey(
                name: "PK_KhachHang",
                table: "KhachHang");

            migrationBuilder.AlterColumn<string>(
                name: "makh",
                table: "KhachHang",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(36)",
                oldMaxLength: 36);

            migrationBuilder.AlterColumn<string>(
                name: "makh",
                table: "DonHang",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(36)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "makh",
                table: "DichVu",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(36)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "id_kh",
                table: "DanhGia",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(36)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "BirthDate",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK__KhachHan__7A21BB4CE42C9226",
                table: "KhachHang",
                column: "makh");
        }
    }
}
