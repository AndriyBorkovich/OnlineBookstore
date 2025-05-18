using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;
using OnlineBookstore.CatalogService.Models;

namespace OnlineBookstore.CatalogService.Services
{
    public sealed class ElasticsearchService(ElasticsearchClient client)
    {
        private const string BookIndexName = "books";

        public async Task EnsureIndexCreatedAsync()
        {
            var indexExistsResponse = await client.Indices.ExistsAsync(BookIndexName);
            if (!indexExistsResponse.Exists)
            {
                var createIndexResponse = await client.Indices.CreateAsync(BookIndexName, c => c
                     .Mappings(m => m
                         .Properties(new Properties
                         {
                            { "title",          new TextProperty() },
                            { "author",         new TextProperty() },
                            { "description",    new TextProperty() },
                            { "categoryId",     new KeywordProperty() },
                            { "vectorEmbedding", new DenseVectorProperty
                              {
                                  Dims       = 384,
                                  Index      = true,
                                  Similarity = DenseVectorSimilarity.Cosine
                              }
                            }
                         })
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
            await client.IndexAsync(new
            {
                id = book.Id,
                title = book.Title,
                author = book.Author,
                description = book.Description,
                categoryId = book.CategoryId,
                price = book.Price,
                stock = book.Stock,
                createdAt = book.CreatedAt,
                vectorEmbedding = book.VectorEmbedding ?? []
            }, i => i.Index(BookIndexName).Id(book.Id.ToString()));
        }

        public async Task<List<Book>> SearchBooksAsync(string searchTerm, int size = 10)
        {
            var searchResponse = await client.SearchAsync<Book>(s => s
                .Index(BookIndexName)
                .Query(q => q
                    .MultiMatch(mm => mm
                        .Fields(new[] { "title^3", "author^2", "description" }) // Fix: Use string array for fields with weights
                        .Query(searchTerm)
                        .Type(TextQueryType.BestFields)
                    )
                )
                .Size(size)
            );

            return [.. searchResponse.Documents];
        }

        public async Task<List<Book>> SimilarBooksAsync(float[] embedding, int size = 5)
        {
            var searchResponse = await client.SearchAsync<Book>(s => s
                .Index(BookIndexName)
                .Knn(k => k
                    .Field("vectorEmbedding")
                    .QueryVector(embedding)
                    .k(size)
                    .NumCandidates(100)
                )
                .Size(size)
            );

            return [.. searchResponse.Documents];
        }
    }
}