using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using OnlineBookstore.CatalogService.Models;

namespace OnlineBookstore.CatalogService.Services
{
    public class ElasticsearchService
    {
        private readonly ElasticsearchClient _client;
        private const string BookIndexName = "books";

        public ElasticsearchService(ElasticsearchClient client)
        {
            _client = client;
        }

        public async Task EnsureIndexCreatedAsync()
        {
            var indexExistsResponse = await _client.Indices.ExistsAsync(BookIndexName);
            if (!indexExistsResponse.Exists)
            {
                var createIndexResponse = await _client.Indices.CreateAsync(BookIndexName, i => i
                    .Mappings(m => m
                        .Properties(p => p
                            .Text(t => t.Name("title"))
                            .Text(t => t.Name("author"))
                            .Text(t => t.Name("description"))
                            .Keyword(k => k.Name("categoryId"))
                            .DenseVector(v => v
                                .Name("vectorEmbedding")
                                .Dims(384) // Using 384 dimensions for embeddings
                                .Index(true)
                                .Similarity("cosine")
                            )
                        )
                    )
                );

                if (!createIndexResponse.IsSuccess())
                {
                    throw new Exception($"Failed to create index: {createIndexResponse.DebugInformation}");
                }
            }
        }

        public async Task IndexBookAsync(Book book)
        {
            await _client.IndexAsync(new
            {
                id = book.Id,
                title = book.Title,
                author = book.Author,
                description = book.Description,
                categoryId = book.CategoryId,
                price = book.Price,
                stock = book.Stock,
                createdAt = book.CreatedAt,
                vectorEmbedding = book.VectorEmbedding ?? Array.Empty<float>()
            }, i => i.Index(BookIndexName).Id(book.Id.ToString()));
        }

        public async Task<List<Book>> SearchBooksAsync(string searchTerm, int size = 10)
        {
            var searchResponse = await _client.SearchAsync<Book>(s => s
                .Index(BookIndexName)
                .Query(q => q
                    .MultiMatch(mm => mm
                        .Fields(f => f.Field(p => p.Title, 3.0).Field(p => p.Author, 2.0).Field(p => p.Description))
                        .Query(searchTerm)
                        .Type(TextQueryType.BestFields)
                        .Fuzziness(Fuzziness.Auto)
                    )
                )
                .Size(size)
            );

            return searchResponse.Documents.ToList();
        }

        public async Task<List<Book>> SimilarBooksAsync(float[] embedding, int size = 5)
        {
            var searchResponse = await _client.SearchAsync<Book>(s => s
                .Index(BookIndexName)
                .Knn(k => k
                    .Field("vectorEmbedding")
                    .QueryVector(embedding)
                    .K(size)
                    .NumCandidates(100)
                )
                .Size(size)
            );

            return searchResponse.Documents.ToList();
        }
    }
}