using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using qlthucung.Libraries;
using qlthucung.Models.vnpay;
using System;
using System.Globalization;

namespace qlthucung.Services.vnpay
{
    public class VnPayService : IVnPayService
    {
        private readonly IConfiguration _configuration;

        public VnPayService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string CreatePaymentUrl(PaymentInformationModel model, HttpContext context)
        {
            var timeZoneById = TimeZoneInfo.FindSystemTimeZoneById(_configuration["TimeZoneId"]);
            var timeNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZoneById);
            string tick = DateTime.Now.ToString("yyyyMMddHHmmss") + new Random().Next(1000, 9999);
            var pay = new VnpayLibrary();
            var ipAddress = pay.GetIpAddress(context);
            if (ipAddress == "::1") ipAddress = "127.0.0.1"; // Chuyển IPv6 localhost về IPv4  
            pay.AddRequestData("vnp_Version", _configuration["Vnpay:Version"]);
            pay.AddRequestData("vnp_Command", _configuration["Vnpay:Command"]);
            pay.AddRequestData("vnp_TmnCode", _configuration["Vnpay:TmnCode"]);
            var amount = (model.Amount * 100).ToString("F0");
            Console.WriteLine($"DEBUG vnp_Amount: {amount}");
            pay.AddRequestData("vnp_Amount", amount);
            pay.AddRequestData("vnp_CreateDate", timeNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
            pay.AddRequestData("vnp_CurrCode", _configuration["Vnpay:CurrCode"]);
            pay.AddRequestData("vnp_IpAddr", ipAddress);
            pay.AddRequestData("vnp_Locale", _configuration["Vnpay:Locale"]);
            pay.AddRequestData("vnp_OrderInfo", $"{model.OrderDescription} Ma don%3A{model.Madon}");
            pay.AddRequestData("vnp_OrderType", model.OrderType);
            pay.AddRequestData("vnp_ReturnUrl", "http://localhost:5172/OnlineCheckout/PaymentCallbackVnpay");
            pay.AddRequestData("vnp_TxnRef", tick);
            DateTime expireTime = timeNow.AddMinutes(15); // Thêm 15 phút từ thời điểm hiện tại
            string vnp_ExpireDate = expireTime.ToString("yyyyMMddHHmmss");
            pay.AddRequestData("vnp_ExpireDate", vnp_ExpireDate);

            var paymentUrl =
                pay.CreateRequestUrl(_configuration["Vnpay:BaseUrl"], _configuration["Vnpay:HashSecret"]);

            return paymentUrl;
        }


        public PaymentResponseModel PaymentExecute(IQueryCollection collections)
        {
            var pay = new VnpayLibrary();
            var response = pay.GetFullResponseData(collections, _configuration["Vnpay:HashSecret"]);

            return response;
        }
    }
}
