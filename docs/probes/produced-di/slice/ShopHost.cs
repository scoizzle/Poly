var closed = BillingAdapters.Charge(new ChargeRequest { Amount = 1, Currency = "USD" });
if (closed.IsSuccess
    || closed.ErrorMessage is null
    || !closed.ErrorMessage.Contains("no in-process adapter", StringComparison.Ordinal))
    return 1;

var emptyBuilder = WebApplication.CreateBuilder(Array.Empty<string>());
await using var emptyApp = emptyBuilder.Build();
if (emptyApp.Services.GetService<IBillingCharge>() is not null)
    return 2;

var builder = WebApplication.CreateBuilder(Array.Empty<string>());
builder.Services.AddSingleton<IBillingCharge, RecordingCharge>();
await using var app = builder.Build();
var billing = app.Services.GetRequiredService<IBillingCharge>();

var created = Order.Create("1001");
if (!created.IsSuccess)
    return 3;

var paid = created.Value.Pay(new ChargeRequest { Amount = 2500, Currency = "USD" }, billing);
if (!paid.IsSuccess)
    return 4;
if (RecordingCharge.Calls != 1 || RecordingCharge.LastAmount != 2500 || RecordingCharge.LastCurrency != "USD")
    return 5;

Console.WriteLine("resolved 2500 USD");
return 0;

sealed class RecordingCharge : IBillingCharge
{
    public static int Calls;
    public static long LastAmount;
    public static string? LastCurrency;

    public DomainResult Charge(ChargeRequest request)
    {
        Calls++;
        LastAmount = request.Amount;
        LastCurrency = request.Currency;
        return DomainResult.Success();
    }
}
