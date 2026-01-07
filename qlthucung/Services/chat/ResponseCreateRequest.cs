namespace qlthucung.Services.chat
{
    internal class ResponseCreateRequest
    {
        public string Model { get; set; }
        public string Input { get; set; }
        public double Temperature { get; set; }
    }
}