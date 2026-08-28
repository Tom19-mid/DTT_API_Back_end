using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Services
{
    public interface IVnPayService
    {
        string CreatePaymentUrl(HttpContext context, VnPaymentRequestModel model);
        VnPaymentResponseModel PaymentExecute(IQueryCollection collections);
    }

    public class VnPayService : IVnPayService
    {
        private readonly IConfiguration _config;

        public VnPayService(IConfiguration config)
        {
            _config = config;
        }

        public string CreatePaymentUrl(HttpContext context, VnPaymentRequestModel model)
        {
            var tick = DateTime.Now.Ticks.ToString();
            var vnpay = new VnPayLibrary();
            vnpay.AddRequestData("vnp_Version", _config["VnPay:Version"] ?? "2.1.0");
            vnpay.AddRequestData("vnp_Command", _config["VnPay:Command"] ?? "pay");
            vnpay.AddRequestData("vnp_TmnCode", _config["VnPay:TmnCode"] ?? "CGXZLS0Z");
            // Số tiền thanh toán nhân 100 theo quy định VNPAY
            vnpay.AddRequestData("vnp_Amount", ((long)(model.Amount * 100)).ToString());
            vnpay.AddRequestData("vnp_CreateDate", model.CreatedDate.ToString("yyyyMMddHHmmss"));
            vnpay.AddRequestData("vnp_CurrCode", _config["VnPay:CurrCode"] ?? "VND");
            vnpay.AddRequestData("vnp_IpAddr", Utils.GetIpAddress(context));
            vnpay.AddRequestData("vnp_Locale", _config["VnPay:Locale"] ?? "vn");
            vnpay.AddRequestData("vnp_OrderInfo", !string.IsNullOrEmpty(model.Description) ? model.Description : ("Thanh toan cho ca kham:" + model.OrderId));
            vnpay.AddRequestData("vnp_OrderType", "other");
            vnpay.AddRequestData("vnp_ReturnUrl", _config["VnPay:PaymentBackReturnUrl"] ?? _config["VnPay:ReturnUrl"] ?? "http://localhost:5000/api/Invoices/vnpay-callback");
            vnpay.AddRequestData("vnp_TxnRef", $"DTT_{model.OrderId}_{tick}");

            var paymentUrl = vnpay.CreateRequestUrl(_config["VnPay:BaseUrl"] ?? "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html", _config["VnPay:HashSecret"] ?? "XNBCJFAKAZQSGTARRLGCHVZWCIOIGSHN");
            return paymentUrl;
        }

        public VnPaymentResponseModel PaymentExecute(IQueryCollection collections)
        {
            var vnpay = new VnPayLibrary();
            foreach (var (key, value) in collections)
            {
                if (!string.IsNullOrEmpty(key) && key.StartsWith("vnp_"))
                {
                    vnpay.AddResponseData(key, value.ToString());
                }
            }

            var vnp_TxnRef = vnpay.GetResponseData("vnp_TxnRef");
            var vnp_TransactionId = vnpay.GetResponseData("vnp_TransactionNo");
            var vnp_SecureHash = collections.FirstOrDefault(p => p.Key == "vnp_SecureHash").Value.ToString();
            var vnp_ResponseCode = vnpay.GetResponseData("vnp_ResponseCode");
            var vnp_OrderInfo = vnpay.GetResponseData("vnp_OrderInfo");

            bool checkSignature = vnpay.ValidateSignature(vnp_SecureHash, _config["VnPay:HashSecret"] ?? "XNBCJFAKAZQSGTARRLGCHVZWCIOIGSHN");
            if (!checkSignature)
            {
                return new VnPaymentResponseModel
                {
                    Success = false,
                    VnPayResponseCode = vnp_ResponseCode
                };
            }

            // Tách OrderId từ TxnRef DTT_{orderId}_{tick}
            string orderIdStr = vnp_TxnRef;
            if (!string.IsNullOrEmpty(vnp_TxnRef) && vnp_TxnRef.StartsWith("DTT_"))
            {
                var parts = vnp_TxnRef.Split('_');
                if (parts.Length >= 2) orderIdStr = parts[1];
            }

            return new VnPaymentResponseModel
            {
                Success = true,
                PaymentMethod = "VnPay",
                OrderDescription = vnp_OrderInfo,
                OrderId = orderIdStr,
                TransactionId = vnp_TransactionId,
                Token = vnp_SecureHash,
                VnPayResponseCode = vnp_ResponseCode
            };
        }
    }
}
