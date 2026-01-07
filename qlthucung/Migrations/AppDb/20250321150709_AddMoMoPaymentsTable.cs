using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace qlthucung.Migrations.AppDb
{
    public partial class AddMoMoPaymentsTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MoMoPayments",
                columns: table => new
                {
                    PaymentId = table.Column<string>(type: "varchar(255)", nullable: false),
                    Madon = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Magiaodich = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Trangthaithanhtoan = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Tinnhantrave = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoMoPayments", x => x.PaymentId);
                    table.ForeignKey(
                        name: "FK_MoMoPayments_Orders",
                        column: x => x.Madon,
                        principalTable: "DonHang",
                        principalColumn: "madon",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MoMoPayments_Madon",
                table: "MoMoPayments",
                column: "Madon");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MoMoPayments");
        }
    }
}
