#nullable enable
using System;
using System.Collections.Generic;

public enum Outcome
{
    Pending = 0,
    Won = 1,
    Lost = 2
}

public record Money
{
    public long Amount { get; set; }
    public string Currency { get; set; } = default!;
}

public record DomainResult
{
    private DomainResult(bool isSuccess, string? errorMessage)
    {
        this.IsSuccess = isSuccess;
        this.ErrorMessage = errorMessage;
    }
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public static DomainResult Success() => new DomainResult(true, null);
    public static DomainResult Failure(string message) => new DomainResult(false, message);
}

public record DomainResult<T>
{
    private DomainResult(bool isSuccess, T value, string? errorMessage)
    {
        this.IsSuccess = isSuccess;
        this.Value = @value;
        this.ErrorMessage = errorMessage;
    }
    public bool IsSuccess { get; }
    public T Value { get; }
    public string? ErrorMessage { get; }
    public static DomainResult<T> Success(T value) => new DomainResult<T>(true, @value, null);
    public static DomainResult<T> Failure(string message) => new DomainResult<T>(false, default!, message);
}

public class ConstraintFailureException : System.Exception
{
    public ConstraintFailureException(string message) : base(message) { }
}