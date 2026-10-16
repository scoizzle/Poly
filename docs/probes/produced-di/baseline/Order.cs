#nullable enable
using System;
using System.Collections.Generic;

public class Order
{
    private Order()
    { }
    private Order(string number)
    {
        this.Number = number;
    }
    public string Number { get; private set; } = default!;
    public DomainResult Pay(ChargeRequest request)
    {
        var adapterResult = BillingAdapters.Charge(request);
        if (!adapterResult.IsSuccess)
        {
            return adapterResult;
        }
        return DomainResult.Success();
    }
    public static DomainResult<Order> Create(string number)
    {
        var created = new Order(number);
        return DomainResult<Order>.Success(created);
    }
    public DomainResult EnsureUnique(string propertyName, object @value) => DomainResult.Success();
}