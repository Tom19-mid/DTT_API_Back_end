using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DTT_Backend_API.Models
{
    public sealed class PaypalClient
    {
        public string Mode { get; }
        public string ClientId { get; }
        public string ClientSecret { get; }
        public string BaseUrl => Mode == "Live"
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";

        public PaypalClient(string clientId, string clientSecret, string mode)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;
            Mode = mode;
        }

        public async Task<AuthResponse> Authenticate()
        {
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
            var content = new List<KeyValuePair<string, string>>
            {
                new("grant_type", "client_credentials")
            };
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"{BaseUrl}/v1/oauth2/token"),
                Method = HttpMethod.Post,
                Headers =
                {
                    { "Authorization", $"Basic {auth}" }
                },
                Content = new FormUrlEncodedContent(content)
            };
            using var httpClient = new HttpClient();
            var httpResponse = await httpClient.SendAsync(request);
            var jsonResponse = await httpResponse.Content.ReadAsStringAsync();
            var response = JsonSerializer.Deserialize<AuthResponse>(jsonResponse);
            return response ?? new AuthResponse();
        }

        public async Task<CreateOrderResponse?> CreateOrder(string value, string currency, string reference)
        {
            var auth = await Authenticate();
            var request = new CreateOrderRequest
            {
                intent = "CAPTURE",
                purchase_units = new List<PurchaseUnit>
                {
                    new()
                    {
                        reference_id = reference,
                        amount = new Amount
                        {
                            currency_code = currency,
                            value = value
                        }
                    }
                }
            };
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {auth.access_token}");
            var httpResponse = await httpClient.PostAsJsonAsync($"{BaseUrl}/v2/checkout/orders", request);
            var jsonResponse = await httpResponse.Content.ReadAsStringAsync();
            var response = JsonSerializer.Deserialize<CreateOrderResponse>(jsonResponse);
            return response;
        }

        public async Task<CaptureOrderResponse?> CaptureOrder(string orderId)
        {
            var auth = await Authenticate();
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {auth.access_token}");
            var httpContent = new StringContent("", Encoding.Default, "application/json");
            var httpResponse = await httpClient.PostAsync($"{BaseUrl}/v2/checkout/orders/{orderId}/capture", httpContent);
            var jsonResponse = await httpResponse.Content.ReadAsStringAsync();
            var response = JsonSerializer.Deserialize<CaptureOrderResponse>(jsonResponse);
            return response;
        }
    }

    public sealed class AuthResponse
    {
        public string scope { get; set; } = string.Empty;
        public string access_token { get; set; } = string.Empty;
        public string token_type { get; set; } = string.Empty;
        public string app_id { get; set; } = string.Empty;
        public int expires_in { get; set; }
        public string nonce { get; set; } = string.Empty;
    }

    public sealed class CreateOrderRequest
    {
        public string intent { get; set; } = "CAPTURE";
        public List<PurchaseUnit> purchase_units { get; set; } = new();
    }

    public sealed class CreateOrderResponse
    {
        public string id { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty;
        public List<Link> links { get; set; } = new();
    }

    public sealed class CaptureOrderResponse
    {
        public string id { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty;
        public PaymentSource? payment_source { get; set; }
        public List<PurchaseUnit> purchase_units { get; set; } = new();
        public Payer? payer { get; set; }
        public List<Link> links { get; set; } = new();
    }

    public sealed class PurchaseUnit
    {
        public Amount amount { get; set; } = new();
        public string reference_id { get; set; } = string.Empty;
        public Shipping? shipping { get; set; }
        public Payments? payments { get; set; }
    }

    public sealed class Payments
    {
        public List<Capture> captures { get; set; } = new();
    }

    public sealed class Shipping
    {
        public Address? address { get; set; }
    }

    public class Capture
    {
        public string id { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty;
        public Amount? amount { get; set; }
        public SellerProtection? seller_protection { get; set; }
        public bool final_capture { get; set; }
        public string disbursement_mode { get; set; } = string.Empty;
        public SellerReceivableBreakdown? seller_receivable_breakdown { get; set; }
        public DateTime create_time { get; set; }
        public DateTime update_time { get; set; }
        public List<Link> links { get; set; } = new();
    }

    public class Amount
    {
        public string currency_code { get; set; } = "USD";
        public string value { get; set; } = "0.00";
    }

    public sealed class Link
    {
        public string href { get; set; } = string.Empty;
        public string rel { get; set; } = string.Empty;
        public string method { get; set; } = string.Empty;
    }

    public sealed class Name
    {
        public string given_name { get; set; } = string.Empty;
        public string surname { get; set; } = string.Empty;
    }

    public sealed class SellerProtection
    {
        public string status { get; set; } = string.Empty;
        public List<string> dispute_categories { get; set; } = new();
    }

    public sealed class SellerReceivableBreakdown
    {
        public Amount? gross_amount { get; set; }
        public PaypalFee? paypal_fee { get; set; }
        public Amount? net_amount { get; set; }
    }

    public sealed class Paypal
    {
        public Name? name { get; set; }
        public string email_address { get; set; } = string.Empty;
        public string account_id { get; set; } = string.Empty;
    }

    public sealed class PaypalFee
    {
        public string currency_code { get; set; } = "USD";
        public string value { get; set; } = "0.00";
    }

    public class Address
    {
        public string address_line_1 { get; set; } = string.Empty;
        public string address_line_2 { get; set; } = string.Empty;
        public string admin_area_2 { get; set; } = string.Empty;
        public string admin_area_1 { get; set; } = string.Empty;
        public string postal_code { get; set; } = string.Empty;
        public string country_code { get; set; } = string.Empty;
    }

    public sealed class Payer
    {
        public Name? name { get; set; }
        public string email_address { get; set; } = string.Empty;
        public string payer_id { get; set; } = string.Empty;
    }

    public sealed class PaymentSource
    {
        public Paypal? paypal { get; set; }
    }
}
