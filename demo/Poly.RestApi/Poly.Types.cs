#nullable enable
using System;
using System.Collections.Generic;

public enum Genre {
    Fiction = 0,
    NonFiction = 1,
    Reference = 2
}

public enum PatronStatus {
    Active = 0,
    Suspended = 1,
    Closed = 2
}

public enum FineStatus {
    Unpaid = 0,
    Resolved = 1
}

public enum PremiumTier {
    Silver = 0,
    Gold = 1,
    Platinum = 2
}

public record DomainResult {
    private DomainResult(bool isSuccess, string? errorMessage) {
        this.IsSuccess = isSuccess;
        this.ErrorMessage = errorMessage;
    }
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public static DomainResult Success() => new DomainResult(true, null);
    public static DomainResult Failure(string message) => new DomainResult(false, message);
}

public record DomainResult<T> {
    private DomainResult(bool isSuccess, T value, string? errorMessage) {
        this.IsSuccess = isSuccess;
        this.Value = value;
        this.ErrorMessage = errorMessage;
    }
    public bool IsSuccess { get; }
    public T Value { get; }
    public string? ErrorMessage { get; }
    public static DomainResult<T> Success(T value) => new DomainResult<T>(true, value, null);
    public static DomainResult<T> Failure(string message) => new DomainResult<T>(false, default!, message);
}