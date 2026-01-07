using Microsoft.AspNetCore.Http;
using qlthucung.Models.vnpay;

namespace qlthucung.Services.vnpay
{
    public interface IVnPayService
    {
        string CreatePaymentUrl(PaymentInformationModel model, HttpContext context);
        PaymentResponseModel PaymentExecute(IQueryCollection collections);
    }
}
