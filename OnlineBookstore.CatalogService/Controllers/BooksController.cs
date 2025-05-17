using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineBookstore.CatalogService.Data;
using OnlineBookstore.CatalogService.Models;
using OnlineBookstore.CatalogService.Services;

namespace OnlineBookstore.CatalogService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BooksController : ControllerBase
    {
        private readonly CatalogDbContext _dbContext;
        private readonly BookCacheService _cacheService;
        private readonly ElasticsearchService _elasticsearchService;
        private readonly ILogger<BooksController> _logger;

        public BooksController(
            CatalogDbContext dbContext, 
            BookCacheService cacheService,
            ElasticsearchService elasticsearchService,
            ILogger<BooksController> logger)
        {
            _dbContext = dbContext;
            _cacheService = cacheService;
            _elasticsearchService = elasticsearchService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Book>>> GetBooks()
        {
            // Try to get from cache first
            var popularBooks = await _cacheService.GetPopularBooksAsync();
            if (popularBooks != null)
            {
                return popularBooks;
            }

            // Get from database if not in cache
            var books = await _dbContext.Books
                .Include(b => b.Category)
                .Take(20)
                .ToListAsync();

            // Cache the results
            await _cacheService.CachePopularBooksAsync(books);

            return books;
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<Book>> GetBook(Guid id)
        {
            // Try to get from cache first
            var book = await _cacheService.GetBookAsync(id);
            if (book != null)
            {
                return book;
            }

            // Get from database if not in cache
            book = await _dbContext.Books
                .Include(b => b.Category)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (book == null)
            {
                return NotFound();
            }

            // Cache the book
            await _cacheService.CacheBookAsync(book);

            return book;
        }

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<Book>>> SearchBooks([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return await GetBooks();
            }

            // Search in Elasticsearch
            var searchResults = await _elasticsearchService.SearchBooksAsync(query);
            
            // If results from Elasticsearch, return them
            if (searchResults.Any())
            {
                return searchResults;
            }
            
            // Fallback to database search if Elasticsearch has no results
            var books = await _dbContext.Books
                .Include(b => b.Category)
                .Where(b => b.Title.Contains(query) || b.Author.Contains(query) || b.Description.Contains(query))
                .Take(20)
                .ToListAsync();
                
            return books;
        }

        [HttpPost]
        public async Task<ActionResult<Book>> CreateBook(Book book)
        {
            _dbContext.Books.Add(book);
            await _dbContext.SaveChangesAsync();

            // Index in Elasticsearch
            await _elasticsearchService.IndexBookAsync(book);

            return CreatedAtAction(nameof(GetBook), new { id = book.Id }, book);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateBook(Guid id, Book book)
        {
            if (id != book.Id)
            {
                return BadRequest();
            }

            _dbContext.Entry(book).State = EntityState.Modified;

            try
            {
                await _dbContext.SaveChangesAsync();
                
                // Update in Elasticsearch
                await _elasticsearchService.IndexBookAsync(book);
                
                // Update in cache
                await _cacheService.CacheBookAsync(book);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await BookExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteBook(Guid id)
        {
            var book = await _dbContext.Books.FindAsync(id);
            if (book == null)
            {
                return NotFound();
            }

            _dbContext.Books.Remove(book);
            await _dbContext.SaveChangesAsync();
            
            // Remove from cache
            await _cacheService.RemoveBookFromCacheAsync(id);

            return NoContent();
        }

        private async Task<bool> BookExists(Guid id)
        {
            return await _dbContext.Books.AnyAsync(e => e.Id == id);
        }
    }
}