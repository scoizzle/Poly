#nullable enable
using System;
using System.Collections.Generic;

public enum CartStage
{
    Active = 0,
    CheckedOut = 1
}

public class Cart
{
    private List<Line> _lines;
    private Cart()
    {
        this._lines = new List<Line>();
    }
    private Cart(string name, IEnumerable<Line>? lines = null, long itemCount = 0L, long total = 0L)
    {
        this.ItemCount = itemCount;
        this.Name = name;
        this.Total = total;
        this._lines = new List<Line>(lines ?? new List<Line>());
        this.CurrentStage = CartStage.Active;
    }
    public long ItemCount { get; private set; }
    public string Name { get; private set; } = default!;
    public long Total { get; private set; }
    public IReadOnlyList<Line> Lines
    {
        get => this._lines;
    }
    public CartStage CurrentStage { get; private set; }
    private DomainResult<Line> CreateLines(long price, long qty, string sku)
    {
        var lineResult = Line.Create(price, qty, sku);
        if (!lineResult.IsSuccess)
        {
            return DomainResult<Line>.Failure(lineResult.ErrorMessage ?? "");
        }
        var line = lineResult.Value;
        this._lines.Add(line);
        return DomainResult<Line>.Success(line);
    }
    internal void AttachLines(Line child)
    {
        this._lines.Add(child);
    }
    public DomainResult AddItem(string sku, long price, long qty)
    {
        var create0 = this.CreateLines(price, qty, sku);
        if (!create0.IsSuccess)
        {
            return DomainResult.Failure(create0.ErrorMessage ?? "");
        }
        this.Total = this.Total + price * qty;
        this.ItemCount = this.ItemCount + 1L;
        return DomainResult.Success();
    }
    public DomainResult Checkout()
    {
        if (this.CurrentStage != CartStage.Active)
        {
            return DomainResult.Failure("'Checkout' requires stage 'Active' on entity 'Cart'.");
        }
        this.CurrentStage = CartStage.CheckedOut;
        return DomainResult.Success();
    }
    public static DomainResult<Cart> Create(string name, IEnumerable<Line>? lines = null, long itemCount = 0L, long total = 0L)
    {
        if (string.IsNullOrEmpty(name))
        {
            return DomainResult<Cart>.Failure("'Name' is required.");
        }
        var created = new Cart(name, lines, itemCount, total);
        return DomainResult<Cart>.Success(created);
    }
}