namespace qlthucung.Models.vnpay
{
    public class PaymentInformationModel
    {
        public string OrderType { get; set; }
        public decimal Amount { get; set; }
        public string OrderDescription { get; set; }
        public string Name { get; set; }
        public string FullName { get; set; }
        public string Madon { get; set; }
    }
}
