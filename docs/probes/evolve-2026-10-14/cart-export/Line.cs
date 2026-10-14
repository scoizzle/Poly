#nullable enable
using System;
using System.Collections.Generic;

public enum LineStage
{
    New = 0
}

public class Line
{
    private Line()
    { }
    private Line(long price, long qty, string sku)
    {
        this.Price = price;
        this.Qty = qty;
        this.Sku = sku;
        this.CurrentStage = LineStage.New;
    }
    public long Price { get; private set; }
    public long Qty { get; private set; }
    public string Sku { get; private set; } = default!;
    public LineStage CurrentStage { get; private set; }
    public static DomainResult<Line> Create(long price, long qty, string sku)
    {
        if (price < 0L)
        {
            return DomainResult<Line>.Failure("'Price' must be >= 0.");
        }
        if (qty < 1L)
        {
            return DomainResult<Line>.Failure("'Qty' must be >= 1.");
        }
        if (string.IsNullOrEmpty(sku))
        {
            return DomainResult<Line>.Failure("'Sku' is required.");
        }
        var created = new Line(price, qty, sku);
        return DomainResult<Line>.Success(created);
    }
}