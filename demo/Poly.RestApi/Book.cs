#nullable enable
using System;
using System.Collections.Generic;

public class Book {
    private Book() {
        // EF materialization.
    }
    private Book(string author, Genre genre, string isbn, long pages, string title) {
        this.Author = author;
        this.Genre = genre;
        this.ISBN = isbn;
        this.Pages = pages;
        this.Title = title;
    }
    public string Author { get; private set; } = default!;
    public Genre Genre { get; private set; }
    public string ISBN { get; private set; } = default!;
    public long Pages { get; private set; }
    public string Title { get; private set; } = default!;
    public static DomainResult<Book> Create(string author, Genre genre, string isbn, long pages, string title) {
        if (string.IsNullOrEmpty(author)) {
            return DomainResult<Book>.Failure("'Author' is required.");
        }
        if (isbn.Length < 10L) {
            return DomainResult<Book>.Failure("'ISBN' must be at least 10 characters.");
        }
        if (isbn.Length > 17L) {
            return DomainResult<Book>.Failure("'ISBN' must be at most 17 characters.");
        }
        if (pages < 1L) {
            return DomainResult<Book>.Failure("'Pages' must be >= 1.");
        }
        if (pages > 10000L) {
            return DomainResult<Book>.Failure("'Pages' must be <= 10000.");
        }
        if (string.IsNullOrEmpty(title)) {
            return DomainResult<Book>.Failure("'Title' is required.");
        }
        return DomainResult<Book>.Success(new Book(author, genre, isbn, pages, title));
    }
}