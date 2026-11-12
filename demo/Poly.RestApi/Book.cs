#nullable enable
public class Book
{
    private Book() { }
    private Book(string title, string author, string isbn, long pages, Genre genre)
    {
        this.Title = title;
        this.Author = author;
        this.ISBN = isbn;
        this.Pages = pages;
        this.Genre = genre;
    }
    public string Title { get; private set; } = default!;
    public string Author { get; private set; } = default!;
    public string ISBN { get; private set; } = default!;
    public long Pages { get; private set; }
    public Genre Genre { get; private set; }
    public static DomainResult<Book> Create(string title, string author, string isbn, long pages, Genre genre, Poly.Runtime.IStoragePort db)
    {
        if (string.IsNullOrEmpty(title))
        {
            return DomainResult<Book>.Failure("'Title' is required.");
        }
        if (string.IsNullOrEmpty(author))
        {
            return DomainResult<Book>.Failure("'Author' is required.");
        }
        if (string.IsNullOrEmpty(isbn))
        {
            return DomainResult<Book>.Failure("'ISBN' is required.");
        }
        if (isbn.Length < 10L)
        {
            return DomainResult<Book>.Failure("'ISBN' must be at least 10 characters.");
        }
        if (isbn.Length > 17L)
        {
            return DomainResult<Book>.Failure("'ISBN' must be at most 17 characters.");
        }
        if (pages < 1L)
        {
            return DomainResult<Book>.Failure("'Pages' must be >= 1.");
        }
        if (pages > 10000L)
        {
            return DomainResult<Book>.Failure("'Pages' must be <= 10000.");
        }
        if ((object)genre == null)
        {
            return DomainResult<Book>.Failure("'Genre' is required.");
        }
        if (genre != Genre.Fiction && genre != Genre.NonFiction && genre != Genre.Reference)
        {
            return DomainResult<Book>.Failure(string.Concat("'", genre, "' is not a valid member of Genre for property 'Genre'."));
        }
        if (Book.ExistsWithISBN(db, isbn))
        {
            return DomainResult<Book>.Failure("Unique constraint violated: 'ISBN' value is already used on another 'Book'.");
        }
        var created = new Book(title, author, isbn, pages, genre);
        db.Record("Book", "ISBN", isbn);
        return DomainResult<Book>.Success(created);
    }
    public DomainResult EnsureUnique(string propertyName, object value)
    {
        if (value == null)
        {
            return DomainResult.Success();
        }
        if (propertyName == "ISBN" && object.Equals(this.ISBN, value))
        {
            return DomainResult.Success();
        }
        return DomainResult.Failure("A unique assign needs the storage port.");
    }
    public static System.Boolean ExistsWithISBN(Poly.Runtime.IStoragePort db, System.String isbn) => db.Exists("Book", "ISBN", isbn);
}